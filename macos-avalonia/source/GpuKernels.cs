namespace MandelbrotViewer;

// GPU kernel/shader sources (§4.3) — GLSL 410 for the OpenGL backend
// (whatever GPU API the host platform provides, §1.6; macOS offers OpenGL 4.1).
//
// Semantics (not syntax): 2D launch grid — fragment (x,y) maps directly to
// pixel/sample (x,y), no index division/modulo. Two programs, float only:
// macOS OpenGL has no FP64 and the MSL route is blocked (SharpMetal 1.1.0
// returns unusable library objects), so 64-bit is exposed as unavailable
// (allowed: "a GPU implementation that only supports float may expose 64-bit
// as unavailable").
//
// - RenderFloat: one invocation per OUTPUT PIXEL. originX = CenterX -
//   FullW*k*0.5*PixelSize (host op order); loops sy,sx over k×k;
//   globalBx = (OffsetX+x)*k+sx (global k-times grid, §1.5.2); Julia/Mandelbrot
//   seeding per §4.3; InteriorBulb skip (§2.5, Mandelbrot only, float —
//   allowed); §2.1 escape loop in float; smooth + LUT lerp (§2.4, within
//   1 LSB); 32-bit channel accumulate (k²=64 samples × 255 fits int);
//   average → RGBA8. Fragment y maps directly to grid rows (no flip:
//   readback is bottom-first and CPU row 0 is grid row 0).
// - BenchFloat: one invocation per SAMPLE (W×H elementary grid),
//   iterations-only §2.1 loop, no coloring/smoothing/early-out, count → R32UI.
public static class GpuKernels
{
    public const string FullscreenVertex = @"#version 410
void main(){
  vec2 v[3] = vec2[3](vec2(-1.0,-1.0), vec2(3.0,-1.0), vec2(-1.0,3.0));
  gl_Position = vec4(v[gl_VertexID], 0.0, 1.0);
}";

    public const string RenderFragmentFloat = @"#version 410
uniform float uCx; uniform float uPx; uniform float uTop;
uniform int uW; uniform int uH; uniform int uMI; uniform int uK; uniform int uJ;
uniform float uJx; uniform float uJy;
uniform int uFW; uniform int uOX; uniform int uOY;
uniform sampler2D uLut;
out vec4 oColor;
bool bulb(float x, float y){
  float q = (x - 0.25) * (x - 0.25) + y * y;
  if (q * (q + (x - 0.25)) <= 0.25 * y * y) return true;
  float xm = x + 1.0;
  return xm * xm + y * y <= 0.0625;
}
vec3 lutColor(float sm, float invMI){
  float pos = clamp(sm * invMI * 4096.0 - 0.5, 0.0, 4095.0);
  float i0 = floor(pos);
  float f = pos - i0;
  vec3 c0 = texelFetch(uLut, ivec2(int(i0), 0), 0).rgb;
  vec3 c1 = texelFetch(uLut, ivec2(min(int(i0) + 1, 4095), 0), 0).rgb;
  return mix(c0, c1, f);
}
void main(){
  int x = int(gl_FragCoord.x);
  // No Y flip: framebuffer fragment y=0 is read back as buffer row 0, and CPU
  // row 0 is grid row 0, so fragment y maps directly to the grid row.
  int outY = int(gl_FragCoord.y);
  float ox = uCx - float(uFW) * float(uK) * 0.5 * uPx;
  float invMI = 1.0 / float(uMI);
  int rs = 0, gs = 0, bs = 0;
  for (int sy = 0; sy < uK; ++sy) {
    for (int sx = 0; sx < uK; ++sx) {
      int gbx = (uOX + x) * uK + sx;
      int gby = (uOY + outY) * uK + sy;
      float ccx = ox + float(gbx) * uPx;
      float ccy = uTop + float(gby) * uPx;
      float z0x, z0y, ex, ey;
      if (uJ != 0) { z0x = ccx; z0y = ccy; ex = uJx; ey = uJy; }
      else { z0x = 0.0; z0y = 0.0; ex = ccx; ey = ccy; }
      uint col;
      if (uJ == 0 && bulb(ccx, ccy)) { col = 0xFF000000u; }
      else {
        float zx = z0x, zy = z0y, zx2 = zx * zx, zy2 = zy * zy;
        int it = 0;
        while (it < uMI && zx2 + zy2 <= 4.0) {
          zy = 2.0 * zx * zy + ey; zx = zx2 - zy2 + ex;
          zx2 = zx * zx; zy2 = zy * zy; ++it;
        }
        if (it >= uMI) { col = 0xFF000000u; }
        else {
          float sm = float(it) + 1.0 - log2(0.5 * log(max(zx2 + zy2, 4.0)));
          vec3 c = lutColor(sm, invMI);
          col = 0xFF000000u | (uint(c.r * 255.0) << 16) | (uint(c.g * 255.0) << 8) | uint(c.b * 255.0);
        }
      }
      rs += int((col >> 16) & 0xFFu);
      gs += int((col >> 8) & 0xFFu);
      bs += int(col & 0xFFu);
    }
  }
  int d = uK * uK;
  oColor = vec4(float(rs / d) / 255.0, float(gs / d) / 255.0, float(bs / d) / 255.0, 1.0);
}";

    public const string BenchFragmentFloat = @"#version 410
uniform float uCx; uniform float uPx; uniform float uTop;
uniform int uW; uniform int uMI;
layout(location = 0) out uint oIter;
void main(){
  int x = int(gl_FragCoord.x);
  int y = int(gl_FragCoord.y);
  float ccx = uCx + (float(x) - float(uW) * 0.5) * uPx;
  float ccy = uTop + float(y) * uPx;
  float zx = 0.0, zy = 0.0, zx2 = 0.0, zy2 = 0.0;
  int it = 0;
  while (it < uMI && zx2 + zy2 <= 4.0) {
    zy = 2.0 * zx * zy + ccy; zx = zx2 - zy2 + ccx;
    zx2 = zx * zx; zy2 = zy * zy; ++it;
  }
  oIter = uint(it);
}";
}
