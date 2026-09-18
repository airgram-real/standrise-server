"""Rebuild rank-*.png from Standoff OBB extracts (public/ranks/_obb)."""
from pathlib import Path
from PIL import Image

OBB = Path(r"C:\StandRise\Web\wwwroot\ranks\_obb")
OUT_DIRS = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\ranks"),
    Path(r"C:\StandRise\Web\wwwroot\ranks"),
]

# Game rank id (-1 calibration) -> source crest in _obb
MAP = {
    -1: "bronze_n_256x256.png",
    0: "bronze_one_140x187.png",
    1: "bronze_two_141x187.png",
    2: "bronze_three_140x187.png",
    3: "bronze_four_140x187.png",
    4: "silver_one_142x187.png",
    5: "silver_two_141x187.png",
    6: "silver_three_141x187.png",
    7: "silver_four_141x187.png",
    8: "gold_one_star_175x187.png",
    9: "gold_two_stars_175x187.png",
    10: "gold_three_stars_175x187.png",
    11: "gold_four_stars_190x198.png",
    12: "season_2_ranked_reward_goldelite_131x251.png",
    13: "season_2_ranked_reward_platinum_131x251.png",
    14: "season_2_ranked_reward_diamond_131x251.png",
    15: "gold_elite_3_232x194.png",
    16: "season_2_ranked_reward_diamond_131x251.png",
}

SIZE = 128


def load(name: str) -> Image.Image:
    p = OBB / name
    if not p.exists():
        raise FileNotFoundError(p)
    return Image.open(p).convert("RGBA")


def fit(im: Image.Image) -> Image.Image:
    w, h = im.size
    scale = min(SIZE / w, SIZE / h)
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    im = im.resize((nw, nh), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(im, ((SIZE - nw) // 2, (SIZE - nh) // 2), im)
    return out


def main():
    if not OBB.is_dir():
        raise SystemExit(f"missing {OBB}")
    for folder in OUT_DIRS:
        folder.mkdir(parents=True, exist_ok=True)

    cal = fit(load(MAP[-1]))
    # Calibration: приглушить
    px = cal.load()
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = px[x, y]
            if a:
                px[x, y] = (int(r * 0.45), int(g * 0.45), int(b * 0.45), a)

    for folder in OUT_DIRS:
        cal.save(folder / "rank-cal.png", optimize=True)

    for rid, fname in MAP.items():
        if rid < 0:
            continue
        img = fit(load(fname))
        for folder in OUT_DIRS:
            img.save(folder / f"rank-{rid}.png", optimize=True)
        print("ok", rid, fname)

    print("done")


if __name__ == "__main__":
    main()
