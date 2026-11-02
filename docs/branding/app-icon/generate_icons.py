#!/usr/bin/env python3
"""Generate HeadsetHolodeck app icons in the app's LCARS visual language.

Renders three candidate concepts at 2048 px, then a review contact sheet that
shows each concept at 512 / 192 / 96 / 48 px and through the two launcher masks
that matter (PICO dock circle, Android squircle).

Run:  python generate_icons.py
Out:  docs/branding/app-icon/concepts/*.png
      docs/branding/app-icon/concepts/contact-sheet.png
"""

from __future__ import annotations

import math
import os

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
CONCEPTS = os.path.join(HERE, "concepts")

# --- LCARS palette, sampled from Assets/App/UI/LCARS/Prefabs (m_Color) ------
BLACK = (7, 8, 12)
AMBER = (255, 122, 10)         # LCARS primary: the Ops panel frame
AMBER_HI = (255, 160, 48)      # LCARS button amber
AMBER_LO = (206, 58, 8)        # shaded end of the amber gradient
LAVENDER = (206, 158, 202)     # LCARS "#cc99cc"
CYAN = (120, 214, 255)
TEAL = (74, 232, 214)
MAGENTA = (255, 96, 176)

S = 2048  # render size


# ---------------------------------------------------------------- helpers ---


def canvas(color=BLACK) -> Image.Image:
    return Image.new("RGB", (S, S), color)


def mask(size=S) -> Image.Image:
    return Image.new("L", (size, size), 0)


def linear_gradient(size: int, top: tuple, bottom: tuple) -> Image.Image:
    t = np.linspace(0.0, 1.0, size, dtype=np.float32)[:, None]
    row = (np.array(top, np.float32)[None, :] * (1.0 - t)
           + np.array(bottom, np.float32)[None, :] * t)
    arr = np.repeat(row[:, None, :], size, axis=1)
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def radial(size: int, stops: list[tuple[float, tuple]], center) -> Image.Image:
    """Radial gradient clipped to the unit disc, transparent-feeling at the rim."""
    cy, cx = center
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    r = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2) / (size * 0.5)
    out = np.zeros((size, size, 3), np.float32)
    out[:] = np.array(stops[0][1], np.float32)
    prev_r, prev_c = stops[0][0], np.array(stops[0][1], np.float32)
    for rf, col in stops[1:]:
        c = np.array(col, np.float32)
        sel = (r >= prev_r) & (r <= rf)
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


def glow(img: Image.Image, m: Image.Image, color: tuple, radius: float,
         strength: float = 1.0) -> Image.Image:
    soft = m.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: int(v * strength))
    return paste(img, Image.new("RGB", img.size, color), soft)


def rounded_mask(inset: float, radius: float, size: int = S) -> Image.Image:
    m = mask(size)
    ImageDraw.Draw(m).rounded_rectangle(
        [inset, inset, size - inset - 1, size - inset - 1], radius=radius, fill=255)
    return m


def ring_mask(inset: float, radius: float, width: float, size: int = S) -> Image.Image:
    outer = rounded_mask(inset, radius, size)
    inner = rounded_mask(inset + width, max(radius - width, 1), size)
    return ImageChops.subtract(outer, inner)


def arc_band(size: int, box: tuple, width: float, start: float = 0, end: float = 360
             ) -> Image.Image:
    """Ring band. A full sweep uses two filled ellipses (clean, no seam/caps);
    a partial sweep falls back to a stroked arc with round caps."""
    m = mask(size)
    d = ImageDraw.Draw(m)
    if abs((end - start) % 360) < 1e-6:
        d.ellipse(box, fill=255)
        inset = width
        d.ellipse([box[0] + inset, box[1] + inset, box[2] - inset, box[3] - inset],
                  fill=0)
        return m
    d.arc(box, start=start, end=end, fill=255, width=int(round(width)))
    cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
    rad = (box[2] - box[0]) / 2
    for ang in (start, end):
        a = math.radians(ang)
        x, y = cx + math.cos(a) * rad, cy + math.sin(a) * rad
        d.ellipse([x - width / 2, y - width / 2, x + width / 2, y + width / 2], fill=255)
    return m


def world_orb(size: int, cx: float, cy: float, r: float, seed: int = 3) -> Image.Image:
    """A generated-world sphere: teal/magenta splat haze inside a lit globe."""
    body = radial(size, [
        (0.00, (255, 255, 255)),
        (0.06, (232, 255, 252)),
        (0.20, (150, 245, 236)),
        (0.38, (86, 214, 226)),
        (0.56, (74, 150, 232)),
        (0.74, (112, 74, 206)),
        (0.88, (54, 28, 96)),
        (1.00, (12, 10, 22)),
    ], center=(cy, cx))

    disc = mask(size)
    ImageDraw.Draw(disc).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)

    # interior splat haze, clipped to the sphere
    rng = np.random.default_rng(seed)
    haze = Image.new("RGB", (size, size), (0, 0, 0))
    hz = ImageDraw.Draw(haze)
    for _ in range(22):
        br = r * rng.uniform(0.12, 0.42)
        bx = cx + rng.normal(0, 0.30) * r
        by = cy + rng.normal(0, 0.30) * r
        col = [TEAL, MAGENTA, CYAN, AMBER_HI][rng.integers(0, 4)]
        hz.ellipse([bx - br, by - br, bx + br, by + br],
                   fill=tuple(int(c * rng.uniform(0.08, 0.22)) for c in col))
    haze = haze.filter(ImageFilter.GaussianBlur(r * 0.22))
    body_img = add(Image.new("RGB", (size, size), (0, 0, 0)),
                   Image.composite(haze, Image.new("RGB", (size, size), (0, 0, 0)), disc))

    out = Image.new("RGB", (size, size), (0, 0, 0))
    out = paste(out, body, disc.filter(ImageFilter.GaussianBlur(size * 0.004)))
    out = add(out, body_img)

    # terminator: darken the lower-right limb for a spherical read
    shade = radial(size, [(0.0, (0, 0, 0)), (1.0, (255, 255, 255))],
                   center=(cy + r * 0.55, cx + r * 0.45))
    sh = Image.eval(shade.convert("L"), lambda v: int(v * 0.55))
    sh = Image.composite(sh, Image.new("L", (size, size), 0), disc)
    out = paste(out, Image.new("RGB", (size, size), (0, 0, 0)), sh)

    # specular highlight, upper-left
    hl = mask(size)
    ImageDraw.Draw(hl).ellipse([cx - r * 0.70, cy - r * 0.80,
                                cx - r * 0.02, cy - r * 0.26], fill=255)
    hl = hl.filter(ImageFilter.GaussianBlur(r * 0.30)).point(lambda v: int(v * 0.50))
    out = paste(out, Image.new("RGB", (size, size), (255, 255, 255)), hl)

    # soft atmosphere bloom just outside the limb
    limb = disc.filter(ImageFilter.GaussianBlur(r * 0.10)).point(lambda v: int(v * 0.35))
    out = glow(out, limb, (90, 190, 255), r * 0.16, 0.9)
    return out


def waveform(size: int, cx: float, base: float, unit: float,
             heights: tuple[float, ...], color: tuple, gap: float = 0.42) -> Image.Image:
    """Chunky LCARS voice bars; `unit` is the bar width."""
    m = mask(size)
    d = ImageDraw.Draw(m)
    total = unit * len(heights) + gap * unit * (len(heights) - 1)
    x = cx - total / 2
    for hgt in heights:
        d.rounded_rectangle([x, base - unit * hgt, x + unit, base + unit * hgt * 0.45],
                            radius=unit / 2, fill=255)
        x += unit * (1 + gap)
    return m


# --------------------------------------------------------------- concepts ---


def concept_portal() -> Image.Image:
    """A: LCARS amber ring around a glowing generated world. No text, no bars."""
    img = canvas()
    cx = cy = S * 0.5
    r = S * 0.310

    band = arc_band(S, (cx - r, cy - r, cx + r, cy + r), S * 0.070)
    img = glow(img, band, AMBER, S * 0.030, 0.85)
    img = paste(img, linear_gradient(S, AMBER_HI, AMBER_LO), band)

    world = world_orb(S, cx, cy - S * 0.018, S * 0.170)
    img = add(img, world)
    return img


def concept_window() -> Image.Image:
    """B: square LCARS panel bezel, world in the window, voice bars below."""
    img = canvas()
    frame = ring_mask(S * 0.070, S * 0.195, S * 0.052)
    img = glow(img, frame, AMBER, S * 0.018, 0.65)
    img = paste(img, linear_gradient(S, AMBER_HI, AMBER_LO), frame)

    world = world_orb(S, S * 0.50, S * 0.405, S * 0.180)
    img = add(img, world)

    bars = waveform(S, S * 0.50, S * 0.760, S * 0.058, (0.75, 1.60, 1.05), AMBER)
    img = paste(img, linear_gradient(S, AMBER_HI, AMBER_LO), bars)
    return img


def concept_pillar() -> Image.Image:
    """C: LCARS pill 'H' — amber stem, lavender stem, amber crossbar."""
    img = canvas()
    left, right = S * 0.195, S * 0.805
    top, bottom = S * 0.195, S * 0.805
    bw = S * 0.132
    r = bw / 2

    def pill(x0, y0, x1, y1, col):
        nonlocal img
        m = mask(S)
        ImageDraw.Draw(m).rounded_rectangle([x0, y0, x1, y1], radius=r, fill=255)
        img = glow(img, m, col, S * 0.016, 0.5)
        img = paste(img, Image.new("RGB", (S, S), col), m)

    pill(left, top, left + bw, bottom, AMBER)
    pill(right - bw, top, right, bottom, LAVENDER)
    pill(left + bw, S * 0.448, right - bw, S * 0.448 + bw * 0.78, AMBER_HI)
    return img


# ------------------------------------------------------------------ sheet ---


def contact_sheet(concepts: dict[str, Image.Image]) -> Image.Image:
    cell, pad, head = 340, 24, 40
    cols = 6
    W = cols * (cell + pad) + pad
    H = len(concepts) * (cell + pad + head) + pad
    sheet = Image.new("RGB", (W, H), (26, 26, 30))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 24)
        small = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 17)
    except OSError:
        font = small = ImageFont.load_default()

    labels = ["512 px", "192 px", "96 px", "48 px", "PICO circle mask",
              "Android squircle"]
    for row, (name, img) in enumerate(concepts.items()):
        y0 = pad + row * (cell + pad + head)
        d.text((pad, y0), name, fill=(240, 240, 245), font=font)
        y = y0 + head
        big = img.resize((512, 512), Image.LANCZOS)
        x = pad
        sheet.paste(big.resize((cell, cell), Image.LANCZOS), (x, y))
        x += cell + pad
        for px in (192, 96, 48):
            tile = img.resize((px, px), Image.LANCZOS)
            sheet.paste(tile, (x + 8, y + cell - px - 8))
            x += cell + pad
        circle = mask(512)
        ImageDraw.Draw(circle).ellipse([0, 0, 511, 511], fill=255)
        squircle = rounded_mask(8, 118, size=512)
        for mm in (circle, squircle):
            masked = Image.composite(big, Image.new("RGB", (512, 512), (20, 20, 24)), mm)
            sheet.paste(masked.resize((cell, cell), Image.LANCZOS), (x, y))
            x += cell + pad
        for i, lab in enumerate(labels):
            d.text((pad + i * (cell + pad), y + cell + 4), lab,
                   fill=(165, 165, 175), font=small)
    return sheet


def main() -> None:
    os.makedirs(CONCEPTS, exist_ok=True)
    concepts = {
        "A - portal arc + world": concept_portal(),
        "B - LCARS window": concept_window(),
        "C - LCARS pillar H": concept_pillar(),
    }
    for name, img in concepts.items():
        slug = name.split(" - ")[0]
        img.save(os.path.join(CONCEPTS, f"concept-{slug}.png"))
    contact_sheet(concepts).save(os.path.join(CONCEPTS, "contact-sheet.png"))
    print("wrote", CONCEPTS)


if __name__ == "__main__":
    main()
