"""Cartoon Telegram-style star tiers (s/m/l) for shop prices."""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

OUT = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\assets\icons"),
    Path(r"C:\StandRise\Web\wwwroot\assets\icons"),
]

FILL = (255, 210, 45, 255)
FILL_DARK = (255, 175, 35, 255)
OUTLINE = (45, 28, 8, 255)
GLOSS = (255, 255, 255, 170)


def star_points(cx: float, cy: float, r_outer: float, r_inner: float) -> list[tuple[float, float]]:
    pts = []
    for i in range(10):
        ang = math.radians(-90 + i * 36)
        r = r_outer if i % 2 == 0 else r_inner
        pts.append((cx + math.cos(ang) * r, cy + math.sin(ang) * r))
    return pts


def draw_star(im: Image.Image, cx: float, cy: float, size: float) -> None:
    d = ImageDraw.Draw(im)
    r_out = size * 0.52
    r_in = size * 0.22
    pts = star_points(cx, cy, r_out, r_in)
    d.polygon(pts, fill=FILL, outline=OUTLINE, width=max(2, int(size * 0.07)))
    # soft shadow layer
    shadow = star_points(cx + size * 0.04, cy + size * 0.05, r_out * 0.98, r_in * 0.98)
    d.polygon(shadow, fill=FILL_DARK)
    d.polygon(pts, fill=FILL, outline=OUTLINE, width=max(2, int(size * 0.07)))
    gloss = star_points(cx - size * 0.06, cy - size * 0.08, r_out * 0.35, r_in * 0.2)
    d.polygon(gloss, fill=GLOSS)


def render(count: int, canvas: int) -> Image.Image:
    im = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    if count == 1:
        draw_star(im, canvas * 0.5, canvas * 0.52, canvas * 0.88)
    elif count == 2:
        draw_star(im, canvas * 0.38, canvas * 0.54, canvas * 0.62)
        draw_star(im, canvas * 0.68, canvas * 0.46, canvas * 0.58)
    else:
        draw_star(im, canvas * 0.34, canvas * 0.56, canvas * 0.52)
        draw_star(im, canvas * 0.58, canvas * 0.48, canvas * 0.48)
        draw_star(im, canvas * 0.72, canvas * 0.58, canvas * 0.44)
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
        pad = max(2, canvas // 24)
        out = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0))
        out.paste(im, (pad, pad), im)
        im = out
    return im


def main() -> None:
    tiers = {"s": 1, "m": 2, "l": 3}
    for tier, n in tiers.items():
        im = render(n, 128)
        for root in OUT:
            root.mkdir(parents=True, exist_ok=True)
            im.save(root / f"star-{tier}.png", optimize=True)
        print("ok", tier, im.size)


if __name__ == "__main__":
    main()
