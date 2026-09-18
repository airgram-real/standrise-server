"""Убирает белую/светлую кайму у логотипа (flood-fill от углов)."""
from collections import deque
from pathlib import Path

from PIL import Image

SRC = Path(r"C:\Users\Administrator\Downloads\photo_4999059453004942545_y.png")
OUT = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\Logo.png"),
    Path(r"C:\StandRise\Web\wwwroot\Logo.png"),
]


def is_backdrop(r: int, g: int, b: int, a: int) -> bool:
    if a < 8:
        return True
    # почти белый / светло-серый фон
    if r > 238 and g > 238 and b > 238:
        return True
    if r > 220 and g > 220 and b > 220 and max(r, g, b) - min(r, g, b) < 18:
        return True
    return False


def main() -> None:
    im = Image.open(SRC).convert("RGBA")
    w, h = im.size
    px = im.load()
    seen = [[False] * w for _ in range(h)]
    q = deque()

    for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        if is_backdrop(*px[x, y]):
            q.append((x, y))
            seen[y][x] = True

    while q:
        x, y = q.popleft()
        r, g, b, a = px[x, y]
        px[x, y] = (r, g, b, 0)
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if nx < 0 or ny < 0 or nx >= w or ny >= h or seen[ny][nx]:
                continue
            if is_backdrop(*px[nx, ny]):
                seen[ny][nx] = True
                q.append((nx, ny))

    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)

    for dst in OUT:
        dst.parent.mkdir(parents=True, exist_ok=True)
        im.save(dst, optimize=True)
    print("logo ok", im.size, "->", OUT[0])


if __name__ == "__main__":
    main()
