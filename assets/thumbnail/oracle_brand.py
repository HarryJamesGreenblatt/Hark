"""Branded Oracle-eye thumbnail: the eye's gaze fixes on each word of the HARK backronym
(Hear -> Adapt -> Render -> Keep) as it fades in one at a time below. Seamless looping GIF.

Reuses the eye look from oracle_eye.py (silver frame -> socket -> breathing cornea + HAL-core
parallax) but sits the eye higher and reveals the tagline word-by-word, the eye glancing to each.
"""
import math
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

# --- Tile spec ---
W, H = 600, 400
SS = 2
FPS = 12
LOOP = 9.0
COLORS = 128

RW, RH = W * SS, H * SS
CX = RW * 0.5
EYE_CY = 150 * SS
R = 138 * SS
SOCKET_R = R * 0.856
CORNEA_R = R * 0.567
AA = 1.3 * SS
GAZE_MAX = 20 * SS
TEXT_Y = 348 * SS

_yy, _xx = np.mgrid[0:RH, 0:RW].astype(np.float32)
_dx = _xx - CX
_dy = _yy - EYE_CY
_dist = np.sqrt(_dx * _dx + _dy * _dy)


def hexc(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


SILVER1, SILVER2, SILVER3 = hexc('EDF1F4'), hexc('9AA1A8'), hexc('4E5459')
SOCKET = hexc('0A0A0B')
C0, C1, C2 = hexc('FFD2C2'), hexc('FF1E10'), hexc('4A0000')
GLOW = hexc('FF1A14')
BG = hexc('000000')
WORD_RGB = (234, 237, 241)      # bright capital — the HARK acronym letters
LOWER_RGB = (150, 154, 160)     # dimmer lowercase, so H·A·R·K stands out
# Vision-scene palette (a cinematic dusk conjured in the pupil for the "Render" beat).
VSKY, VHOR, VLOW, VSUN = hexc('223351'), hexc('D9692A'), hexc('2A1510'), hexc('FFE6B0')


def lerp(a, b, t):
    return a + (b - a) * t[..., None]


def disc_alpha(dist, radius):
    return np.clip((radius - dist) / AA + 0.5, 0.0, 1.0)[..., None]


def smoother(u):
    u = min(1.0, max(0.0, u))
    return u * u * u * (u * (u * 6 - 15) + 10)


def cornea_color(distc, radius):
    u = np.clip(distc / radius, 0.0, 1.0)
    inner = lerp(C0, C1, np.clip(u / 0.42, 0, 1))
    outer = lerp(C1, C2, np.clip((u - 0.42) / 0.58, 0, 1))
    return np.where((u < 0.42)[..., None], inner, outer)


def vision_scene(ccx, ccy, cr):
    """A small cinematic dusk (sky -> horizon -> ground + a low sun) to render inside the pupil."""
    v = np.clip((_yy - (ccy - cr)) / (2 * cr), 0.0, 1.0)   # 0 top .. 1 bottom of the pupil bbox
    horizon = 0.58
    sky = lerp(VSKY, VHOR, np.clip(v / horizon, 0, 1))
    ground = lerp(VHOR * 0.6, VLOW, np.clip((v - horizon) / (1 - horizon), 0, 1))
    scene = np.where((v < horizon)[..., None], sky, ground)
    suny = ccy - cr + 2 * cr * horizon
    sund = np.sqrt((_xx - ccx) ** 2 + (_yy - suny) ** 2)
    scene = scene + VSUN * (np.exp(-(sund / (cr * 0.30)) ** 2) * 0.9)[..., None]
    return np.clip(scene, 0, 255)


# --- Tagline layout (fit the four words to the tile width) ---
WORDS = ["Hear.", "Adapt.", "Render.", "Keep."]


def _load_font(px):
    for name in ("seguisb.ttf", "segoeuib.ttf", "segoeui.ttf", "arialbd.ttf"):
        try:
            return ImageFont.truetype(f"C:/Windows/Fonts/{name}", px)
        except OSError:
            continue
    return ImageFont.load_default()


_probe = ImageDraw.Draw(Image.new('RGB', (4, 4)))
FONT_PX = 34
while FONT_PX > 20:
    FONT = _load_font(int(FONT_PX * SS))
    WIDTHS = [_probe.textlength(w, font=FONT) for w in WORDS]
    GAP = 26 * SS
    TOTAL = sum(WIDTHS) + GAP * (len(WORDS) - 1)
    if TOTAL <= (W - 44) * SS:
        break
    FONT_PX -= 1

_startx = CX - TOTAL / 2
CENTERS = []
_x = _startx
for _w in WIDTHS:
    CENTERS.append(_x + _w / 2)
    _x += _w + GAP

# --- Timeline: word appearances + the gaze keyframes that glance at each ---
APPEAR = [1.0, 2.6, 4.2, 5.8]
FADE = 0.5
FADE_OUT = 8.0


def _word_gaze(i):
    gx = np.clip((CENTERS[i] - CX) / (TOTAL / 2) * GAZE_MAX * 0.85, -GAZE_MAX, GAZE_MAX)
    return float(gx), 0.7 * GAZE_MAX   # down toward the tagline row


def _build_kf():
    gh, ga, gr = _word_gaze(0), _word_gaze(1), _word_gaze(2)
    gm = GAZE_MAX
    return [
        (0.0, 0.0, 0.0), (0.85, 0.0, 0.0),
        (1.15, *gh), (2.45, *gh),                       # Hear — fixate the word
        # Adapt — perform "adapt" through GAZE: a quick three-point scan (as if sorting who's
        # speaking / taking in the room) before the eye settles on the word.
        (2.70, -0.75 * gm, -0.10 * gm), (2.90, -0.75 * gm, -0.10 * gm),
        (3.10, 0.82 * gm, 0.05 * gm), (3.32, 0.82 * gm, 0.05 * gm),
        (3.52, -0.10 * gm, -0.55 * gm), (3.72, -0.10 * gm, -0.55 * gm),
        (3.92, *ga), (4.15, *ga),                       # settle on "Adapt"
        (4.35, *gr), (4.75, *gr),                       # Render
        (5.25, 0.0, 0.0),                               # recentre to present the rendered vision for Keep
        (LOOP, 0.0, 0.0),
    ]


KF = _build_kf()


def gaze(t):
    gx = gy = 0.0
    for i in range(len(KF) - 1):
        t0, x0, y0 = KF[i]
        t1, x1, y1 = KF[i + 1]
        if t0 <= t <= t1:
            s = smoother((t - t0) / (t1 - t0)) if t1 > t0 else 0.0
            gx, gy = x0 + (x1 - x0) * s, y0 + (y1 - y0) * s
            break
    f = 1.0 / LOOP
    gx += (0.9 * math.sin(2 * math.pi * 3 * f * t + 0.7) + 0.5 * math.sin(2 * math.pi * 7 * f * t + 2.1)) * SS
    gy += (0.8 * math.cos(2 * math.pi * 3 * f * t + 1.3)) * SS
    return gx, gy


def word_alpha(t, i):
    if t < APPEAR[i]:
        a = 0.0
    elif t < APPEAR[i] + FADE:
        a = (t - APPEAR[i]) / FADE
    else:
        a = 1.0
    if t > FADE_OUT:
        a *= max(0.0, 1.0 - (t - FADE_OUT) / (LOOP - FADE_OUT))
    return smoother(a)


def vision_env(t):
    """A Vision renders into the pupil as the word "Render" appears, and is held through the "Keep"
    blink that captures it, then fades before the loop resets."""
    if t < 4.3 or t >= 8.9:
        return 0.0
    if t < 5.0:
        return smoother((t - 4.3) / 0.7)
    if t < 8.2:
        return 1.0
    return 1.0 - smoother((t - 8.2) / 0.7)


def blink(t):
    """A quick eyelid shutter on the "Keep" beat: reads as a camera snapshot capturing the vision."""
    t0, dur = 5.95, 0.5
    if t < t0 or t > t0 + dur:
        return 0.0
    u = (t - t0) / dur
    if u < 0.35:
        return smoother(u / 0.35)              # snap shut
    if u < 0.5:
        return 1.0                             # shutter closed
    return 1.0 - smoother((u - 0.5) / 0.5)     # reopen


def draw_eye(gx, gy, level, vis=0.0, bl=0.0):
    img = np.tile(BG, (RH, RW, 1)).astype(np.float32)

    diag = np.clip((_dx + _dy) / (2 * R) + 0.5, 0, 1)
    silver = np.where((diag < 0.5)[..., None],
                      lerp(SILVER1, SILVER2, np.clip(diag * 2, 0, 1)),
                      lerp(SILVER2, SILVER3, np.clip((diag - 0.5) * 2, 0, 1)))
    a = disc_alpha(_dist, R)
    img = img * (1 - a) + silver * a
    a = disc_alpha(_dist, SOCKET_R)
    img = img * (1 - a) + SOCKET * a

    ccx, ccy = CX + gx, EYE_CY + gy
    distc = np.sqrt((_xx - ccx) ** 2 + (_yy - ccy) ** 2)
    cr = CORNEA_R * (0.95 + 0.08 * level)

    glow = np.exp(-(distc / (cr * 1.25)) ** 2) * (0.05 + 0.7 * level)
    img = img + GLOW * (glow[..., None] * 0.75)

    corex, corey = ccx + 0.75 * gx, ccy + 0.75 * gy
    coredist = np.sqrt((_xx - corex) ** 2 + (_yy - corey) ** 2)
    cbright = 0.26 + 0.74 * level
    a = disc_alpha(distc, cr)
    img = img * (1 - a) + cornea_color(coredist, cr) * cbright * a

    # As the gaze returns to centre, a Vision renders into the pupil (the "Render" of HARK),
    # leaving a thin cornea rim so it still reads as suspended inside the eye.
    if vis > 0.004:
        scene = vision_scene(ccx, ccy, cr)
        a = disc_alpha(distc, cr * 0.92) * vis
        img = img * (1 - a) + scene * a

    # Blink: the cornea-red eyelid sweeps down over the pupil — a camera-shutter "snapshot" that
    # captures the rendered vision (the "Keep" of HARK) without any literal save iconography.
    if bl > 0.004:
        lid_line = (ccy - cr) + bl * (2.0 * cr)
        lid_edge = np.clip((lid_line - _yy) / AA + 0.5, 0.0, 1.0)[..., None]
        lidmask = disc_alpha(distc, cr) * lid_edge
        lidcol = cornea_color(coredist, cr) * (0.32 + 0.68 * level)
        img = img * (1 - lidmask) + lidcol * lidmask

    ghx, ghy = ccx - cr * 0.26, ccy - cr * 0.44
    gloss = np.exp(-(((_xx - ghx) / (cr * 0.52)) ** 2 + ((_yy - ghy) / (cr * 0.30)) ** 2)) * (0.14 + 0.30 * level)
    img = img + 255.0 * gloss[..., None]

    return np.clip(img, 0, 255).astype(np.uint8)


def frame(t):
    level = 0.5 + 0.5 * math.sin(2 * math.pi * 4 * t / LOOP)   # 4 breaths per loop
    vis = vision_env(t)
    bl = blink(t)
    gx, gy = gaze(t)
    base = Image.fromarray(draw_eye(gx, gy, max(level, 0.6 * vis), vis, bl), 'RGB').convert('RGBA')

    overlay = Image.new('RGBA', base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(overlay)
    for i, wd in enumerate(WORDS):
        al = word_alpha(t, i)
        if al <= 0.004:
            continue
        # Emphasise the capital (the HARK acronym) — bright; dim the rest of the word.
        cap, rest = wd[0], wd[1:]
        full_w = _probe.textlength(wd, font=FONT)
        cap_w = _probe.textlength(cap, font=FONT)
        left = CENTERS[i] - full_w / 2
        d.text((left, TEXT_Y), cap, font=FONT, fill=(*WORD_RGB, int(255 * al)), anchor='lm')
        d.text((left + cap_w, TEXT_Y), rest, font=FONT,
               fill=(*LOWER_RGB, int(255 * al * 0.6)), anchor='lm')
    base = Image.alpha_composite(base, overlay).convert('RGB').resize((W, H), Image.LANCZOS)
    return base


def main():
    n = int(round(LOOP * FPS))
    rgb = []
    for i in range(n):
        rgb.append(frame(i / FPS))
        print(f"render {i + 1}/{n}", end='\r')

    strip = np.concatenate([np.asarray(rgb[j]) for j in range(0, n, max(1, n // 8))], axis=0)
    pal = Image.fromarray(strip).quantize(colors=COLORS, method=Image.MEDIANCUT, dither=Image.NONE)
    frames = [im.quantize(palette=pal, dither=Image.NONE) for im in rgb]

    out = os.path.join(os.path.dirname(__file__), 'oracle-brand.gif')
    frames[0].save(out, save_all=True, append_images=frames[1:], loop=0,
                   duration=int(1000 / FPS), optimize=True, disposal=2)
    print(f"\nwrote {out}  ({os.path.getsize(out) / 1_000_000:.2f} MB, {n} frames, {W}x{H})")


if __name__ == '__main__':
    main()
