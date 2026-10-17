"""Backend Vulkan (wgpu, f32-only) + selezione adapter."""
import struct
import threading
import numpy as np
from PIL import Image
from . import state as S
from .palette import PALETTES, make_lut

def _build_vulkan_wgsl():
    return r'''
@group(0) @binding(0) var<storage, read> lut: array<u32>;
@group(0) @binding(1) var<uniform> p: vec4<f32>;
@group(0) @binding(2) var<uniform> dim: vec4<i32>;
@group(0) @binding(3) var<storage, read_write> out: array<u32>;

@compute @workgroup_size(16,16,1)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    let col = i32(gid.x);
    let row = i32(gid.y);
    let w = dim.x;
    let h = dim.y;
    if (col >= w || row >= h) { return; }
    let cx = p.x;
    let cy = p.y;
    let hs = p.z;
    let mi = dim.z;
    let pal = dim.w;

    let x0 = cx + hs * (f32(2 * col - w) / f32(w));
    let y0 = cy + hs * (f32(h) / f32(w)) * (f32(2 * row - h) / f32(h));
    let ridx = row * w + col;

    // Interior analitico: bulbo periodica-2 + cardioide principale (stesso criterio)
    if (x0 >= -2.0 && x0 <= 0.4 && y0 >= -1.3 && y0 <= 1.3) {
        let d2 = (x0 + 1.0) * (x0 + 1.0) + y0 * y0;
        if (d2 <= 0.0625) { out[ridx] = 0u; return; }
        let A = 1.0 - 4.0 * x0;
        let B = -4.0 * y0;
        let R = sqrt(A * A + B * B);
        if (R < 2.0 * sqrt(0.5 * (R + A))) { out[ridx] = 0u; return; }
    }

    let a = cx * cx + (x0 - cx);
    let two_cx = 2.0 * cx;
    var wr = -cx;
    var wi = 0.0;
    var esc = false;
    var it = 0;
    var mag2 = 0.0;
    for (var i = 0; i < mi; i = i + 1) {
        if (esc) { break; }
        let nr = wr * wr - wi * wi + two_cx * wr + a;
        let ni = two_cx * wi + 2.0 * wr * wi + y0;
        wr = nr;
        wi = ni;
        let zr = wr + cx;
        mag2 = zr * zr + wi * wi;
        if (mag2 > 4.0) { esc = true; it = i; }
    }
    if (!esc) { out[ridx] = 0u; return; }

    let nu = f32(it) + 1.0 - log2(0.5 * log(mag2));
    let t = pow(min(1.0, max(0.0, nu / f32(mi))), 0.35);
    let idx = i32(min(1.0, max(0.0, t)) * 255.0);
    out[ridx] = lut[pal * 256 + idx];
}
'''
class VulkanBackend:
    """Backend Vulkan (wgpu) f32, deterministico (2 run bit-identici).
    LUT (tutte le palette concatenate, u32 packed 0x00RRGGBB) e output
    (h*w u32 packed) sono buffer storage; i parametri (cx,cy,half in
    vec4<f32>; w,h,mi,pal in vec4<i32>) sono due buffer uniform. H2D via
    queue.write_buffer (buffer persistenti), D2H via queue.read_buffer.
    Un lock serializza le chiamate (compute sincrono: submit + read_buffer).
    """
    TH = 16  # workgroup 16x16 = 256 thread (sotto il max di AMD/NVIDIA/Intel)

    def __init__(self, adapter=None):
        import wgpu
        self._w = wgpu
        self._lock = threading.Lock()
        if adapter is None:
            if not wgpu.gpu.enumerate_adapters_sync():
                raise RuntimeError("nessun adapter GPU (Vulkan)")
            adapter = wgpu.gpu.request_adapter_sync(power_preference="high-performance")
            if adapter is None:
                raise RuntimeError("nessun adapter (Vulkan)")
        self._init_on_adapter(adapter)

    def _init_on_adapter(self, adapter):
        # (Ri)crea TUTTE le risorse device-bound su 'adapter'. Chiamato da
        # __init__ (nessuna contesa ancora) e da select_adapter (sotto lock).
        wgpu = self._w
        self.dev = adapter.request_device_sync()
        if self.dev is None:
            raise RuntimeError("device Vulkan fallito")
        try:
            self.name = dict(adapter.info).get("device", "GPU")
        except Exception:
            self.name = "GPU"
        self.lut_buf = self._make_lut_buffer()
        shader = self.dev.create_shader_module(code=_build_vulkan_wgsl())
        self.pipe = self.dev.create_compute_pipeline(
            layout="auto",
            compute=wgpu.ProgrammableStage(module=shader, entry_point="main"))
        self.layout = self.pipe.get_bind_group_layout(0)
        uflags = wgpu.BufferUsage.UNIFORM | wgpu.BufferUsage.COPY_DST
        self.p_buf = self.dev.create_buffer(size=16, usage=uflags)
        self.dim_buf = self.dev.create_buffer(size=16, usage=uflags)
        self._out = None
        self._out_size = 0
        self._bg = None
        self._bg_out = None

    def select_adapter(self, adapter):
        # v5.9.2: cambio GPU a runtime (dropdown): ricrea device, pipeline,
        # buffer e bind group sul nuovo adapter (mai in gara con compute()).
        with self._lock:
            self._init_on_adapter(adapter)

    def _make_lut_buffer(self):
        # Tutte le palette concatenate (ordine PALETTES = indice pal), u32
        # packed 0x00RRGGBB: 1 entry = 1 u32. La selezione per indice pal
        # avviene nel shader (lut[pal*256 + idx]), come CUDA/MSL.
        lut = np.empty((len(PALETTES), 256), dtype=np.uint32)
        for i, (_name, pal) in enumerate(PALETTES.items()):
            l = make_lut(pal).astype(np.uint32)  # (256,3)
            lut[i] = l[:, 0] | (l[:, 1] << 8) | (l[:, 2] << 16)
        data = np.ascontiguousarray(lut).ravel().tobytes()
        return self.dev.create_buffer_with_data(
            data=data, usage=self._w.BufferUsage.STORAGE)

    def _out_buf(self, need):
        if self._out is None or self._out_size < need:
            self._out = self.dev.create_buffer(
                size=need,
                usage=self._w.BufferUsage.STORAGE | self._w.BufferUsage.COPY_SRC)
            self._out_size = need
            self._bg = None  # il bind group punta sul buffer vecchio
        return self._out

    def _ensure_bg(self, out):
        if self._bg is None or self._bg_out is not out:
            self._bg = self.dev.create_bind_group(layout=self.layout, entries=[
                self._w.BindGroupEntry(binding=0, resource=self.lut_buf),
                self._w.BindGroupEntry(binding=1, resource=self.p_buf),
                self._w.BindGroupEntry(binding=2, resource=self.dim_buf),
                self._w.BindGroupEntry(binding=3, resource=out),
            ])
            self._bg_out = out
        return self._bg

    def compute(self, cx, cy, half, w, h, mi, pal=0):
        need = w * h * 4  # 1 u32 per pixel
        with self._lock:
            out = self._out_buf(need)
            self.dev.queue.write_buffer(
                self.p_buf, 0, struct.pack('<ffff', cx, cy, half, 0.0))
            self.dev.queue.write_buffer(
                self.dim_buf, 0, struct.pack('<iiii', w, h, mi, pal))
            bg = self._ensure_bg(out)
            enc = self.dev.create_command_encoder()
            p = enc.begin_compute_pass()
            p.set_pipeline(self.pipe)
            p.set_bind_group(0, bg)
            p.dispatch_workgroups(
                (w + self.TH - 1) // self.TH, (h + self.TH - 1) // self.TH, 1)
            p.end()
            self.dev.queue.submit([enc.finish()])
            mv = self.dev.queue.read_buffer(out, 0, w * h * 4)
            u = np.frombuffer(mv, dtype=np.uint32).reshape(h, w)
            rgb = np.empty((h, w, 3), dtype=np.uint8)
            rgb[:, :, 0] = (u & 0xFF)
            rgb[:, :, 1] = (u >> 8) & 0xFF
            rgb[:, :, 2] = (u >> 16) & 0xFF
            return rgb
S._VULKAN_OK = False
S._VULKAN_BE = None
# v5.9.2: adapter GPU fisici (backend Vulkan) enumerati all'avvio.
# S._VULKAN_ADAPTERS = [(indice_enum, nome)]; S._VULKAN_DEV = posizione
# selezionata (default 0). Il dropdown "GPU:" li mostra quando il motore
# attivo e' Vulkan (prima pilotava solo CUDA: Vulkan usava sempre
# l'high-performance -> benchmark quasi uguali su GPU diverse).
S._VULKAN_ADAPTERS = []
S._VULKAN_DEV = 0
try:
    import wgpu  # wgpu-native: cross-platform (Windows/macOS/Linux)
    _enum = wgpu.gpu.enumerate_adapters_sync()
    _seen = set()
    for _ei, _a in enumerate(_enum):
        try:
            _inf = dict(_a.info)
        except Exception:
            continue
        if _inf.get("backend_type") != "Vulkan":
            continue
        _key = (_inf.get("vendor_id"), _inf.get("device_id"))
        if _key in _seen:
            continue
        _seen.add(_key)
        S._VULKAN_ADAPTERS.append((_ei, str(_inf.get("device", "GPU")).strip()))
    if not S._VULKAN_ADAPTERS:
        raise RuntimeError("nessun adapter GPU (Vulkan)")
    # default = stesso adapter di prima (high-performance): prima voce che
    # matcha (vendor, device), altrimenti la prima enumerata.
    try:
        _hp = dict(wgpu.gpu.request_adapter_sync(
            power_preference="high-performance").info)
        _hpk = (_hp.get("vendor_id"), _hp.get("device_id"))
        for _k, (_ei, _nm) in enumerate(S._VULKAN_ADAPTERS):
            _inf = dict(_enum[_ei].info)
            if (_inf.get("vendor_id"), _inf.get("device_id")) == _hpk:
                S._VULKAN_ADAPTERS[0], S._VULKAN_ADAPTERS[_k] = \
                    S._VULKAN_ADAPTERS[_k], S._VULKAN_ADAPTERS[0]
                break
    except Exception:
        pass
    S._VULKAN_BE = VulkanBackend(_enum[S._VULKAN_ADAPTERS[0][0]])
    S._VULKAN_OK = True
except Exception:
    S._VULKAN_OK = False
    S._VULKAN_BE = None
    S._VULKAN_ADAPTERS = []
    S._VULKAN_DEV = 0
def _vulkan_short_name(name):
    # "NVIDIA GeForce RTX 5070 Ti" -> "GeForce RTX 5070 Ti" (dropdown compatto)
    if name.startswith("NVIDIA "):
        return name[len("NVIDIA "):]
    return name

def _vulkan_label(i):
    return "%d: %s" % (i, _vulkan_short_name(S._VULKAN_ADAPTERS[i][1]))

def set_vulkan_adapter(i):
    """Seleziona l'adapter Vulkan (posizione in S._VULKAN_ADAPTERS); True se ok.
    Ricrea le risorse device-bound via backend (sotto lock) e invalida la
    cache hw_name."""
    if S._VULKAN_BE is None or not S._VULKAN_ADAPTERS:
        return False
    try:
        i = int(i)
    except (TypeError, ValueError):
        return False
    i = max(0, min(i, len(S._VULKAN_ADAPTERS) - 1))
    try:
        import wgpu
        _enum = wgpu.gpu.enumerate_adapters_sync()
        S._VULKAN_BE.select_adapter(_enum[S._VULKAN_ADAPTERS[i][0]])
    except Exception:
        return False
    S._VULKAN_DEV = i
    S._HW_CACHE.pop("vulkan", None)
    return True
def _compute_gpu_vulkan(cx, cy, half, w, h, mi, prec=None):
    # Vulkan (wgpu) e' f32-only (stessa scelta di Metal).
    if prec == "f64":
        raise ValueError("Vulkan: solo f32")
    rgb = S._VULKAN_BE.compute(cx, cy, half, w, h, mi, pal=S._PAL_INDEX_CACHE)
    return Image.fromarray(rgb)

