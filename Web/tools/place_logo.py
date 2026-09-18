from collections import deque
from pathlib import Path
from PIL import Image

src = Path(r"C:\Users\Administrator\Downloads\photo_4999059453004942545_y.jpg")
im = Image.open(src).convert("RGBA")
px = im.load()
w, h = im.size


def is_bg(c):
    r, g, b, a = c
    return a > 0 and r >= 252 and g >= 252 and b >= 252


seen = bytearray(w * h)
q = deque()
for x in range(w):
    q.append((x, 0))
    q.append((x, h - 1))
for y in range(h):
    q.append((0, y))
    q.append((w - 1, y))

cleared = 0
while q:
    x, y = q.popleft()
    if x < 0 or y < 0 or x >= w or y >= h:
        continue
    i = y * w + x
    if seen[i]:
        continue
    seen[i] = 1
    c = px[x, y]
    if not is_bg(c):
        continue
    px[x, y] = (c[0], c[1], c[2], 0)
    cleared += 1
    q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))

bbox = im.getbbox()
if bbox:
    pad = 4
    x0, y0, x1, y1 = bbox
    im = im.crop((max(0, x0 - pad), max(0, y0 - pad), min(w, x1 + pad), min(h, y1 + pad)))

outs = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\Logo.png"),
    Path(r"C:\StandRise\Web\wwwroot\Logo.png"),
]
for out in outs:
    out.parent.mkdir(parents=True, exist_ok=True)
    im.save(out, "PNG", optimize=True)
    print("WROTE", out, out.stat().st_size, im.size, "cleared", cleared)
