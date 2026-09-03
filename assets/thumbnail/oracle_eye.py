"""Animated Oracle-eye thumbnail generator (HARK hackathon tile).

Renders a seamless looping GIF of the Oracle's eye — faithful to the WPF app's OracleEyeBig
(silver frame -> black socket -> red radial cornea with the bright HAL core -> glow -> gloss),
with the same gaze model spirit (min-jerk saccades + pink-ish jitter + intra-pupil parallax where
the bright core leads the disc). Deterministic and periodic so frame N wraps cleanly to frame 0.

Output: oracle-eye.gif in this folder. Tune SIZE/FPS/LOOP/COLORS to stay under the 5 MB tile cap.
"""
import math
import numpy as np
from PIL import Image

# --- Tile spec (3:2 for the gallery tile) ---
W, H = 600, 400
SS = 2                      # supersample for anti-aliasing, then downscale
FPS = 15
LOOP = 4.0                  # seconds; the whole animation is periodic over this
COLORS = 96                # GIF palette size (shared across all frames)

RW, RH = W * SS, H * SS
cx, cy = RW * 0.5, RH * 0.5
R = 178 * SS               # silver outer radius (fits the 400px height with margin)
SOCKET_R = R * 0.856       # app: margin 26/360
CORNEA_R = R * 0.567       # app: margin 78/360
AA = 1.3 * SS

_yy, _xx = np.mgrid[0:RH, 0:RW].astype(np.float32)
_dx = _xx - cx
_dy = _yy - cy
_dist = np.sqrt(_dx * _dx + _dy * _dy)


def hexc(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


SILVER1, SILVER2, SILVER3 = hexc('EDF1F4'), hexc('9AA1A8'), hexc('4E5459')
SOCKET = hexc('0A0A0B')
C0, C1, C2 = hexc('FFD2C2'), hexc('FF1E10'), hexc('4A0000')   # cornea radial stops
GLOW = hexc('FF1A14')
BG = hexc('000000')


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def disc_alpha(dist, radius):
    return np.clip((radius - dist) / AA + 0.5, 0.0, 1.0)[..., None]


def smoother(u):
    return u * u * u * (u * (u * 6 - 15) + 10)   # min-jerk position profile


# Gaze keyframes (display px, relative to centre): start & end centred with holds between saccades.
KF = [(0.0, 0, 0), (0.9, 0, 0), (1.2, -30, 7), (1.9, -30, 7),
      (2.2, 22, -24), (2.9, 22, -24), (3.2, 0, 0), (4.0, 0, 0)]


def gaze(t):
    gx = gy = 0.0
    for i in range(len(KF) - 1):
        t0, x0, y0 = KF[i]
        t1, x1, y1 = KF[i + 1]
        if t0 <= t <= t1:
            s = smoother((t - t0) / (t1 - t0)) if t1 > t0 else 0.0
            gx, gy = x0 + (x1 - x0) * s, y0 + (y1 - y0) * s
            break
    # Periodic pink-ish jitter (frequencies are integer multiples of 1/LOOP so it wraps seamlessly).
    f = 1.0 / LOOP
    jx = 1.3 * math.sin(2 * math.pi * 3 * f * t + 0.7) + 0.7 * math.sin(2 * math.pi * 7 * f * t + 2.1)
    jy = 1.1 * math.cos(2 * math.pi * 3 * f * t + 1.3) + 0.6 * math.cos(2 * math.pi * 5 * f * t + 0.4)
    return (gx + jx) * SS, (gy + jy) * SS


def cornea_color(distc, radius):
    u = np.clip(distc / radius, 0.0, 1.0)
    inner = lerp(C0, C1, np.clip(u / 0.42, 0, 1))
    outer = lerp(C1, C2, np.clip((u - 0.42) / 0.58, 0, 1))
    return np.where((u < 0.42)[..., None], inner, outer)


def frame(t):
    img = np.tile(BG, (RH, RW, 1)).astype(np.float32)

    # Silver frame: diagonal light->dark gradient (top-left bright, like the app's LinearGradientBrush).
    diag = np.clip((_dx + _dy) / (2 * R) + 0.5, 0, 1)
    silver = np.where((diag < 0.5)[..., None],
                      lerp(SILVER1, SILVER2, np.clip(diag * 2, 0, 1)),
                      lerp(SILVER2, SILVER3, np.clip((diag - 0.5) * 2, 0, 1)))
    a = disc_alpha(_dist, R)
    img = img * (1 - a) + silver * a

    # Black socket.
    a = disc_alpha(_dist, SOCKET_R)
    img = img * (1 - a) + SOCKET * a

    gx, gy = gaze(t)
    ccx, ccy = cx + gx, cy + gy
    ddx, ddy = _xx - ccx, _yy - ccy
    distc = np.sqrt(ddx * ddx + ddy * ddy)

    # Emission pulse (0..1), twice per loop. The WHOLE cornea rides it: a dim ember at the trough,
    # a bright red bloom at the peak (mirrors the app's audio-reactive cornea), so the eye itself
    # emits the pulse rather than just a halo flickering around a constantly-red disc.
    level = 0.5 + 0.5 * math.sin(2 * math.pi * 2 * t / LOOP)
    cr = CORNEA_R * (0.95 + 0.08 * level)          # slight dilation on the pulse

    # Red glow bloom: tight enough that the black socket + silver ring stay defined even at the peak
    # (the eye should stay an EYE, not flood into a solid ball). Nearly gone at the trough.
    glow = np.exp(-(distc / (cr * 1.25)) ** 2) * (0.05 + 0.7 * level)
    img = img + GLOW * (glow[..., None] * 0.75)

    # Cornea disc; brightness breathes with the pulse. The bright core leads the disc (parallax).
    corex, corey = ccx + 0.75 * gx, ccy + 0.75 * gy
    coredist = np.sqrt((_xx - corex) ** 2 + (_yy - corey) ** 2)
    cbright = 0.26 + 0.74 * level
    a = disc_alpha(distc, cr)
    img = img * (1 - a) + cornea_color(coredist, cr) * cbright * a

    # Glass gloss: a soft top highlight that dims with the eye.
    ghx, ghy = ccx - cr * 0.26, ccy - cr * 0.44
    gdx = (_xx - ghx) / (cr * 0.52)
    gdy = (_yy - ghy) / (cr * 0.30)
    gloss = np.exp(-(gdx * gdx + gdy * gdy)) * (0.14 + 0.30 * level)
    img = img + 255.0 * gloss[..., None]

    return np.clip(img, 0, 255).astype(np.uint8)


def main():
    import os
    n = int(round(LOOP * FPS))
    rgb = []
    for i in range(n):
        arr = frame(i / FPS)
        rgb.append(Image.fromarray(arr, 'RGB').resize((W, H), Image.LANCZOS))
        print(f"render {i + 1}/{n}", end='\r')

    # One shared palette across all frames = smaller file + no per-frame colour flicker.
    strip = np.concatenate([np.asarray(rgb[j]) for j in range(0, n, max(1, n // 8))], axis=0)
    pal = Image.fromarray(strip).quantize(colors=COLORS, method=Image.MEDIANCUT, dither=Image.NONE)
    frames = [im.quantize(palette=pal, dither=Image.NONE) for im in rgb]

    out = os.path.join(os.path.dirname(__file__), 'oracle-eye.gif')
    frames[0].save(out, save_all=True, append_images=frames[1:], loop=0,
                   duration=int(1000 / FPS), optimize=True, disposal=2)
    print(f"\nwrote {out}  ({os.path.getsize(out) / 1_000_000:.2f} MB, {n} frames, {W}x{H})")


if __name__ == '__main__':
    main()
