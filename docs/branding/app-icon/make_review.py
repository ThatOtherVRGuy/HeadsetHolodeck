#!/usr/bin/env python3
"""Render a review sheet for the produced icon set (verification only)."""

from __future__ import annotations

import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
ICONS = os.path.join(REPO, "Assets", "App", "UI", "Branding", "AppIcon")
OUT = os.path.join(HERE, "review")


def load(rel: str) -> Image.Image:
    return Image.open(os.path.join(ICONS, rel)).convert("RGBA")


def circle(img: Image.Image) -> Image.Image:
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).ellipse([0, 0, img.size[0] - 1, img.size[1] - 1], fill=255)
    out = img.copy()
    out.putalpha(m)
    return out


def squircle(img: Image.Image) -> Image.Image:
    m = Image.new("L", img.size, 0)
    n = img.size[0]
    ImageDraw.Draw(m).rounded_rectangle([0, 0, n - 1, n - 1], radius=n * 0.22, fill=255)
    out = img.copy()
    out.putalpha(m)
    return out


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    master = load("HolodeckAppIcon_master_1024.png")
    meta = load("Meta/Holodeck_Icon_512.png")
    pico_bg = load("Pico/Holodeck_icon_bg_1024.png")
    pico_fg = load("Pico/Holodeck_icon_fg2_1024.png")
    android_bg = load("Android/adaptive_background_432.png")
    android_fg = load("Android/adaptive_foreground_432.png")

    pico = Image.alpha_composite(pico_bg, pico_fg).resize((512, 512), Image.LANCZOS)
    android = Image.alpha_composite(android_bg.resize((512, 512), Image.LANCZOS),
                                   android_fg.resize((512, 512), Image.LANCZOS))

    tiles = [
        ("master 1024", master.resize((320, 320), Image.LANCZOS)),
        ("Meta store 512", meta.resize((320, 320), Image.LANCZOS)),
        ("PICO bg+fg2", pico.resize((320, 320), Image.LANCZOS)),
        ("PICO masked (circle)", circle(pico).resize((320, 320), Image.LANCZOS)),
        ("Android adaptive", android.resize((320, 320), Image.LANCZOS)),
        ("Android launcher", squircle(android).resize((320, 320), Image.LANCZOS)),
    ]
    smalls = [
        ("192", load("Android/legacy_192.png")),
        ("96", load("Android/legacy_96.png")),
        ("72", load("Android/legacy_72.png")),
        ("48", load("Android/legacy_48.png")),
    ]

    pad, title_h, cell = 24, 44, 320
    W = pad + 6 * (cell + pad)
    H = title_h + cell + pad + title_h + 220 + pad
    sheet = Image.new("RGB", (W, H), (24, 24, 28))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 22)
        small = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 18)
    except OSError:
        font = small = ImageFont.load_default()

    d.text((pad, pad), "HeadsetHolodeck - app icon set (review)", fill=(240, 240, 245), font=font)
    y = pad + title_h
    for i, (label, tile) in enumerate(tiles):
        x = pad + i * (cell + pad)
        sheet.paste(tile, (x, y))
        d.text((x, y + cell + 4), label, fill=(170, 170, 182), font=small)

    y = pad + title_h + cell + title_h + 40
    x = pad
    d.text((pad, y - 34), "Legacy ladder at true pixel size", fill=(200, 200, 210), font=small)
    for label, tile in smalls:
        sheet.paste(tile, (x, y + 100 - tile.size[1]))
        d.text((x, y + 104), f"{label} px", fill=(150, 150, 162), font=small)
        x += cell // 2

    sheet.save(os.path.join(OUT, "icon-set-review.png"))
    print("wrote", os.path.join(OUT, "icon-set-review.png"))


if __name__ == "__main__":
    main()


