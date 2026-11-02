#!/usr/bin/env python3
"""Build the HeadsetHolodeck app-icon set from the chosen 'portal' design.

Design: an LCARS amber ring (the holodeck doorway) around a glowing generated
world, on a near-black navy field. No lettering, so it satisfies both stores'
icon rules.

Outputs (all under Assets/App/UI/Branding/AppIcon):
  HolodeckAppIcon_master_1024.png       opaque, squared, full-bleed master
  Android/legacy_<px>.png               48..192 px launcher ladder
  Android/round_<px>.png                round-masked legacy ladder
  Android/adaptive_foreground_432.png   transparent, art inside the 66dp safe zone
  Android/adaptive_background_432.png   opaque field
  Meta/Holodeck_Icon_512.png            512x512, opaque, squared corners
  Pico/Holodeck_icon_bg_1024.png        opaque field (inside the circle)
  Pico/Holodeck_icon_fg2_1024.png       transparent art inside the inscribed circle
  Pico/Holodeck_Icon_512.png            single-file store icon

Run:  python build_app_icons.py
"""

from __future__ import annotations

import math
import os

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(REPO, "Assets", "App", "UI", "Branding", "AppIcon")

# --- LCARS palette, sampled from Assets/App/UI/LCARS/Prefabs (m_Color) ------
FIELD_TOP = (18, 23, 38)       # deep navy: PICO asks to avoid pure black
FIELD_BOTTOM = (8, 10, 18)
AMBER_HI = (255, 160, 44)
AMBER_LO = (206, 58, 8)
CYAN_GLOW = (96, 196, 255)
AMBER_GLOW = (255, 122, 10)

S = 2048          # design resolution
RING_R = 0.390    # ring outer radius, fraction of canvas
RING_W = 0.088    # ring band width
ORB_R = 0.214     # world radius
ORB_DY = -0.022   # world is lifted slightly out of centre
ART_SPAN = 2 * RING_R   # ring outer diameter, as a fraction of the canvas


# ---------------------------------------------------------------- helpers ---


def mask(size: int = S) -> Image.Image:
    return Image.new("L", (size, size), 0)


def linear_gradient(size: int, top: tuple, bottom: tuple) -> Image.Image:
    t = np.linspace(0.0, 1.0, size, dtype=np.float32)[:, None]
    row = (np.array(top, np.float32)[None, :] * (1.0 - t)
           + np.array(bottom, np.float32)[None, :] * t)
    return Image.fromarray(
        np.repeat(row[:, None, :], size, axis=1).astype(np.uint8), "RGB")


def radial(size: int, stops: list[tuple[float, tuple]], center) -> Image.Image:
    """Radial gradient. Everything past the last stop keeps the last stop's
    colour, so a gradient that ends at r=1.0 stays continuous into the corners."""
    cy, cx = center
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    r = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2) / (size * 0.5)
    out = np.zeros((size, size, 3), np.float32)
    out[:] = np.array(stops[-1][1], np.float32)   # outside the final stop
    prev_r = stops[0][0]
    prev_c = np.array(stops[0][1], np.float32)
    out[r <= prev_r] = prev_c                      # inside the first stop
    for rf, col in stops[1:]:
        c = np.array(col, np.float32)
        sel = (r > prev_r) & (r <= rf)
        t = np.clip((r - prev_r) / max(rf - prev_r, 1e-6), 0, 1)[..., None]
        out = np.where(sel[..., None], prev_c * (1 - t) + c * t, out)
        prev_r, prev_c = rf, c
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")


def paste(img: Image.Image, layer: Image.Image, m: Image.Image) -> Image.Image:
    img.paste(layer, (0, 0), m)
    return img


def add(img: Image.Image, layer: Image.Image) -> Image.Image:
    a = np.asarray(img, np.int16) + np.asarray(layer, np.int16)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB")


def bloom(m: Image.Image, color: tuple, radius: float, strength: float) -> Image.Image:
    soft = m.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: int(v * strength))
    layer = Image.new("RGB", m.size, color)
    out = Image.new("RGB", m.size, (0, 0, 0))
    out.paste(layer, (0, 0), soft)
    return out


# ----------------------------------------------------------------- layers ---


def ring_mask(size: int, cx: float, cy: float, r: float, w: float) -> Image.Image:
    m = mask(size)
    d = ImageDraw.Draw(m)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    d.ellipse([cx - r + w, cy - r + w, cx + r - w, cy + r - w], fill=0)
    return m


def world(size: int, cx: float, cy: float, r: float, seed: int = 5):
    """Return (rgb, alpha) for the generated-world sphere."""
    body = radial(size, [
        (0.00, (255, 255, 255)),
        (0.06, (236, 255, 252)),
        (0.22, (150, 246, 242)),
        (0.40, (60, 220, 214)),
        (0.58, (40, 150, 230)),
        (0.74, (66, 80, 200)),
        (0.88, (40, 26, 96)),
        (1.00, (8, 8, 16)),
    ], center=(cy, cx))

    disc = mask(size)
    ImageDraw.Draw(disc).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)

    out = Image.new("RGB", (size, size), (0, 0, 0))
    out = paste(out, body, disc.filter(ImageFilter.GaussianBlur(size * 0.003)))

    # surface: soft "continents" so the sphere reads as a world, not a marble
    rng = np.random.default_rng(seed)
    land = Image.new("RGB", (size, size), (0, 0, 0))
    ld = ImageDraw.Draw(land)
    for _ in range(9):
        lr = r * rng.uniform(0.16, 0.42)
        lx = cx + rng.normal(0, 0.32) * r
        ly = cy + rng.normal(0, 0.26) * r
        col = [(10, 30, 60), (16, 64, 92), (40, 52, 120)][rng.integers(0, 3)]
        ld.ellipse([lx - lr, ly - lr * 0.75, lx + lr, ly + lr * 0.75], fill=col)
    land = land.filter(ImageFilter.GaussianBlur(r * 0.16))
    land_a = Image.composite(
        Image.new("L", (size, size), 60), Image.new("L", (size, size), 0), disc)
    land_a = land_a.filter(ImageFilter.GaussianBlur(size * 0.004))
    out = Image.composite(land, out, land_a)

    # splat haze: a few bright specks of generated geometry
    haze = Image.new("RGB", (size, size), (0, 0, 0))
    hd = ImageDraw.Draw(haze)
    for _ in range(14):
        br = r * rng.uniform(0.10, 0.28)
        bx = cx + rng.normal(0, 0.30) * r
        by = cy + rng.normal(0, 0.30) * r
        col = [(74, 232, 214), (255, 96, 176), (120, 214, 255)][rng.integers(0, 3)]
        hd.ellipse([bx - br, by - br, bx + br, by + br],
                   fill=tuple(int(c * rng.uniform(0.10, 0.26)) for c in col))
    haze = haze.filter(ImageFilter.GaussianBlur(r * 0.18))
    out = add(out, Image.composite(haze, Image.new("RGB", (size, size), (0, 0, 0)), disc))

    # terminator: darken the lower hemisphere so it reads as a globe
    band = mask(size)
    ImageDraw.Draw(band).rectangle([0, cy + r * 0.26, size, size], fill=255)
    band = band.filter(ImageFilter.GaussianBlur(r * 0.18))
    band = Image.composite(band, mask(size), disc)
    out = paste(out, Image.new("RGB", (size, size), (3, 5, 12)),
                band.point(lambda v: int(v * 0.62)))

    # atmosphere rim, upper-left
    rim = mask(size)
    dr = ImageDraw.Draw(rim)
    dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=0)
    dr.arc([cx - r * 0.99, cy - r * 0.99, cx + r * 0.99, cy + r * 0.99],
           start=150, end=310, fill=255, width=int(r * 0.10))
    rim = rim.filter(ImageFilter.GaussianBlur(r * 0.06)).point(lambda v: int(v * 0.55))
    out = paste(out, Image.new("RGB", (size, size), (140, 240, 255)), rim)

    # specular highlight, upper-left
    hl = mask(size)
    ImageDraw.Draw(hl).ellipse([cx - r * 0.62, cy - r * 0.74,
                                cx - r * 0.06, cy - r * 0.26], fill=255)
    hl = hl.filter(ImageFilter.GaussianBlur(r * 0.24)).point(lambda v: int(v * 0.50))
    out = paste(out, Image.new("RGB", (size, size), (255, 255, 255)), hl)
    return out, disc


def build_layers(size: int = S):
    """Return (background RGB, art RGBA) at `size`."""
    cx = cy = size / 2
    r = size * RING_R
    w = size * RING_W
    ox = cx
    oy = cy + size * ORB_DY
    orb_r = size * ORB_R

    ring = ring_mask(size, cx, cy, r, w)
    orb_rgb, orb_disc = world(size, ox, oy, orb_r)

    # background: navy field + the bloom that the art casts onto it
    bg = linear_gradient(size, FIELD_TOP, FIELD_BOTTOM)
    bg = add(bg, bloom(ring, AMBER_GLOW, size * 0.040, 0.30))
    bg = add(bg, bloom(orb_disc, CYAN_GLOW, size * 0.060, 0.16))

    # art on transparency: ring gradient + sphere, no baked shadow
    art = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ring_layer = linear_gradient(size, AMBER_HI, AMBER_LO).convert("RGBA")
    ring_layer.putalpha(ring)
    art = Image.alpha_composite(art, ring_layer)

    orb_layer = orb_rgb.convert("RGBA")
    orb_layer.putalpha(orb_disc)
    art = Image.alpha_composite(art, orb_layer)
    return bg, art


def compose(size_out: int, art_scale: float = 1.0) -> Image.Image:
    """Full icon: background + art at `art_scale`, flattened opaque."""
    bg, art = build_layers()
    if art_scale != 1.0:
        n = max(1, int(round(S * art_scale)))
        small = art.resize((n, n), Image.LANCZOS)
        canvas = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        off = (S - n) // 2
        canvas.paste(small, (off, off), small)
        art = canvas
    flat = Image.alpha_composite(bg.convert("RGBA"), art).convert("RGB")
    return flat if size_out == S else flat.resize((size_out, size_out), Image.LANCZOS)


def circle_texture(size: int, art_scale: float = 1.0) -> Image.Image:
    """Square icon pre-masked to a circle over the field colour (round icons)."""
    flat = compose(size, art_scale).convert("RGBA")
    m = mask(size)
    ImageDraw.Draw(m).ellipse([0, 0, size - 1, size - 1], fill=255)
    flat.putalpha(m)
    bg = Image.new("RGBA", (size, size), FIELD_BOTTOM + (255,))
    return Image.alpha_composite(bg, flat).convert("RGB")


# ----------------------------------------------------------------- export ---


def save(img: Image.Image, rel: str, opaque: bool = True) -> str:
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out = img.convert("RGB") if opaque else img
    out.save(path, "PNG", optimize=True)
    print(f"  {rel:44s} {out.size[0]}x{out.size[1]} {'RGB' if opaque else 'RGBA'}")
    return path


def place(art: Image.Image, span: float) -> Image.Image:
    """Centre the art so its ring outer edge spans `span` of the canvas."""
    n = int(round(S * span / ART_SPAN))
    if n == S:
        return art
    scaled = art.resize((n, n), Image.LANCZOS)
    canvas = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    off = (S - n) // 2
    canvas.paste(scaled, (off, off), scaled)
    return canvas


def main() -> None:
    print(f"building icon set in {OUT}")
    bg, art = build_layers()

    # --- Unity master + Android ladder ---
    save(compose(1024), "HolodeckAppIcon_master_1024.png")
    for px in (192, 144, 96, 72, 48):
        save(compose(px), f"Android/legacy_{px}.png")
        save(circle_texture(px), f"Android/round_{px}.png")

    # --- Android adaptive icon: 108 dp canvas, 66 dp safe zone ----
    # The art spans 78% of the canvas, so it is scaled to 56/108 to sit
    # comfortably inside the 66 dp safe zone that launchers mask and parallax.
    save(place(art, 0.56).resize((432, 432), Image.LANCZOS),
         "Android/adaptive_foreground_432.png", opaque=False)
    # Background layer: a plain field with a soft centre lift. No ring-aligned
    # glow, because Android parallaxes the two layers independently.
    adaptive_bg = add(linear_gradient(S, FIELD_TOP, FIELD_BOTTOM),
                      radial(S, [(0.0, (26, 34, 54)), (1.0, (0, 0, 0))],
                             center=(S / 2, S / 2)))
    save(adaptive_bg.resize((432, 432), Image.LANCZOS),
         "Android/adaptive_background_432.png")

    # --- Meta Horizon Store: 512x512, opaque, squared corners ---
    save(compose(512), "Meta/Holodeck_Icon_512.png")

    # --- PICO: 1024x1024 layered (bg + fg2), art inside the inscribed circle ---
    # 78% span leaves a buffer inside the circular mask PICO applies.
    save(place(art, 0.78).resize((1024, 1024), Image.LANCZOS),
         "Pico/Holodeck_icon_fg2_1024.png", opaque=False)

    # bg: the field, opaque inside the circular mask PICO applies
    pico_bg = add(linear_gradient(S, FIELD_TOP, FIELD_BOTTOM),
                  radial(S, [(0.0, (22, 30, 48)), (1.0, (0, 0, 0))],
                         center=(S / 2, S / 2)))
    save(pico_bg.resize((1024, 1024), Image.LANCZOS), "Pico/Holodeck_icon_bg_1024.png")

    save(compose(512), "Pico/Holodeck_Icon_512.png")


if __name__ == "__main__":
    main()




