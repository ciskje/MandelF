"""Backend Metal (pyobjc, f32-only)."""
import struct
import threading
import time
import numpy as np
from PIL import Image
from . import state as S
from .palette import PALETTES, make_lut
from .cuda import _fmt_lut
from .config import METAL_TIMEOUT_S

def _build_metal_msl():
    names = list(PALETTES)
    consts = "\n".join(
        "constant unsigned char LUT_%s[768] = { %s };"
        % (n.upper(), _fmt_lut(make_lut(pal))) for n, pal in PALETTES.items())
    select = "    constant unsigned char* lut = LUT_%s;" % names[0].upper()
    for i, n in enumerate(names[1:], start=1):
        select += "\n    if (p.pal == %d) lut = LUT_%s;" % (i, n.upper())
    return r'''
#include <metal_stdlib>
using namespace metal;

@@CONSTS@@

struct Params {
    float cx;
    float cy;
    float hs;
    int w;
    int h;
    int mi;
    int pal;
};

kernel void mandel(
    device unsigned char* out [[buffer(0)]],
    constant Params& p [[buffer(1)]],
    uint2 pos [[thread_position_in_grid]])
{
    int col = (int)pos.x;
    int row = (int)pos.y;
    if (col >= p.w || row >= p.h) return;

@@SELECT@@

    float cx = p.cx, cy = p.cy, hs = p.hs;
    int w = p.w, h = p.h, mi = p.mi;

    float x0 = cx + hs * ((float)(2 * col - w) / (float)w);
    float y0 = cy + hs * ((float)h / (float)w) * ((float)(2 * row - h) / (float)h);

    device unsigned char* pp = out + (size_t)(row * w + col) * 3;

    // Interior analitico: bulbo periodica-2 + cardioide principale (stesso criterio CUDA/CPU)
    if (x0 >= -2.0f && x0 <= 0.4f && y0 >= -1.3f && y0 <= 1.3f) {
        float d2 = (x0 + 1.0f) * (x0 + 1.0f) + y0 * y0;
        if (d2 <= 0.0625f) { pp[0] = 0; pp[1] = 0; pp[2] = 0; return; }
        float A = 1.0f - 4.0f * x0;
        float B = -4.0f * y0;
        float R = sqrt(A * A + B * B);
        if (R < 2.0f * sqrt(0.5f * (R + A))) { pp[0] = 0; pp[1] = 0; pp[2] = 0; return; }
    }

    float a = cx * cx + (x0 - cx);
    float two_cx = 2.0f * cx;
    float wr = -cx, wi = 0.0f;
    int it = 0;
    bool esc = false;
    float mag2 = 0.0f;
    for (int i = 0; i < mi; ++i) {
        if (esc) break;
        float nr = wr * wr - wi * wi + two_cx * wr + a;
        float ni = two_cx * wi + 2.0f * wr * wi + y0;
        wr = nr; wi = ni;
        float zr = wr + cx;
        mag2 = zr * zr + wi * wi;
        if (mag2 > 4.0f) { esc = true; it = i; }
    }
    if (!esc) { pp[0] = 0; pp[1] = 0; pp[2] = 0; return; }

    float nu = (float)it + 1.0f - log2(0.5f * log(mag2));
    float t = pow(fmin(1.0f, fmax(0.0f, nu / (float)mi)), 0.35f);
    int idx = (int)(fmin(1.0f, fmax(0.0f, t)) * 255.0f);
    pp[0] = lut[idx * 3 + 0];
    pp[1] = lut[idx * 3 + 1];
    pp[2] = lut[idx * 3 + 2];
}
'''.replace("@@CONSTS@@", consts).replace("@@SELECT@@", select)
class MetalBackend:
    """Backend Metal (Apple GPU) f32, deterministico (2 run bit-identici).
    I parametri (cx, cy, half, w, h, mi, pal) vanno in uno struct passato via
    buffer [[buffer(1)]]; l'output e' un buffer [[buffer(0)]]. H2D/D2H via
    buf.contents().as_buffer(N) (memoryview scrivibile, C-level). Un lock
    serializza le chiamate (il compute e' sincrono: commit+attesa con
    deadline METAL_TIMEOUT_S+read, v7.2.1).
    """
    TH = 16  # threads per threadgroup (16x16 = 256, sotto il max 1024 di M1)

    def __init__(self):
        import Metal
        self._Metal = Metal
        self.dev = Metal.MTLCreateSystemDefaultDevice()
        if self.dev is None:
            raise RuntimeError("nessun dispositivo Metal")
        self.q = self.dev.newCommandQueue()
        src = _build_metal_msl()
        self.lib, err = self.dev.newLibraryWithSource_options_error_(src, None, None)
        if self.lib is None:
            raise RuntimeError("compilazione MSL fallita: %s" % err)
        fn = self.lib.newFunctionWithName_('mandel')
        self.ps, err = self.dev.newComputePipelineStateWithFunction_error_(fn, None)
        if self.ps is None:
            raise RuntimeError("pipeline Metal fallita: %s" % err)
        self._out = None
        self._pbuf = self.dev.newBufferWithLength_options_(
            28, Metal.MTLResourceStorageModeShared)
        self._lock = threading.Lock()

    def _out_buf(self, need):
        if self._out is None or self._out.length() < need:
            self._out = self.dev.newBufferWithLength_options_(
                need, self._Metal.MTLResourceStorageModeShared)
        return self._out

    def compute(self, cx, cy, half, w, h, mi, pal=0):
        M = self._Metal
        need = w * h * 3
        with self._lock:
            out = self._out_buf(need)
            payload = struct.pack('<fffiiii', cx, cy, half, w, h, mi, pal)
            self._pbuf.contents().as_buffer(len(payload))[:] = payload
            gx = (w + self.TH - 1) // self.TH
            gy = (h + self.TH - 1) // self.TH
            cmd = self.q.commandBuffer()
            enc = cmd.computeCommandEncoder()
            enc.setComputePipelineState_(self.ps)
            enc.setBuffer_offset_atIndex_(out, 0, 0)         # buffer[0] = out
            enc.setBuffer_offset_atIndex_(self._pbuf, 0, 1)   # buffer[1] = params (offset=0, INDEX=1)
            enc.dispatchThreadgroups_threadsPerThreadgroup_(
                M.MTLSizeMake(gx, gy, 1), M.MTLSizeMake(self.TH, self.TH, 1))
            enc.endEncoding()
            # v7.2.1: deadline al posto di waitUntilCompleted() bloccante:
            # una GPU/driver impallassi (es. HW non collaudato) deve dare un
            # errore chiaro, non un hang infinito del benchmark.
            # Il completed handler scatta su OGNI stato terminale (ok e
            # errore); NON confrontiamo i valori di status() grezzi:
            # l'enum cambia tra versioni macOS (es. Completed = 4 su
            # macOS 26, 5 su versioni precedenti).
            ev = threading.Event()
            cmd.addCompletedHandler_(lambda cb: ev.set())
            cmd.commit()
            if not ev.wait(timeout=METAL_TIMEOUT_S):
                raise RuntimeError(
                    "Metal: rendering non completato entro %d s "
                    "(GPU bloccata?)" % METAL_TIMEOUT_S)
            r = cmd.error()
            if r is not None:
                raise RuntimeError("Metal: command buffer fallita: %s" % r)
            mv = out.contents().as_buffer(need)
            return np.frombuffer(mv, dtype=np.uint8, count=need).reshape(h, w, 3).copy()
S._METAL_OK = False
S._METAL_BE = None
try:
    import Metal  # pyobjc: disponibile su macOS con i framework di sistema
    if Metal.MTLCreateSystemDefaultDevice() is not None:
        S._METAL_BE = MetalBackend()
        S._METAL_OK = True
except Exception:
    S._METAL_OK = False
    S._METAL_BE = None
def _compute_gpu_metal(cx, cy, half, w, h, mi, prec=None):
    # Metal e' f32-only (Apple Silicon non supporta 'double').
    if prec == "f64":
        raise ValueError("Metal: solo f32 ('double' non supportato su Apple Silicon)")
    rgb = S._METAL_BE.compute(cx, cy, half, w, h, mi, pal=S._PAL_INDEX_CACHE)
    return Image.fromarray(rgb)

