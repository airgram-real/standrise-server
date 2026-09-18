"""Rank crest PNGs from OBB (Standoff 0.17) -> public/ranks + wwwroot."""
from pathlib import Path
import zipfile
from PIL import Image

obb = Path(r"C:\Users\Administrator\Desktop\0.17.0\main.961.com.axlebolt.standoff2.obb")
unity = Path(r"C:\StandRise\Web\tools\_unity3d\data.unity3d")
OBB_DUMP = Path(r"C:\StandRise\Web\wwwroot\ranks\_obb")
OUTS = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\ranks"),
    Path(r"C:\StandRise\Web\wwwroot\ranks"),
]
SIZE = 128

# rank id -> filename stem in _obb (from game UI assets)
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


def ensure_obb_dump():
    if not OBB_DUMP.is_dir() or len(list(OBB_DUMP.glob("bronze_one*.png"))) == 0:
        print("refreshing _obb dump from unity...")
        import subprocess
        subprocess.check_call(["python", str(Path(__file__).with_name("extract_obb_ranks.py"))])


def fit(im: Image.Image) -> Image.Image:
    w, h = im.size
    scale = min(SIZE / w, SIZE / h)
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    im = im.resize((nw, nh), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(im, ((SIZE - nw) // 2, (SIZE - nh) // 2), im)
    return out


def main():
    ensure_obb_dump()
    for folder in OUTS:
        folder.mkdir(parents=True, exist_ok=True)

    cal = fit(Image.open(OBB_DUMP / MAP[-1]).convert("RGBA"))
    px = cal.load()
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = px[x, y]
            if a:
                px[x, y] = (int(r * 0.5), int(g * 0.5), int(b * 0.5), a)
    for folder in OUTS:
        cal.save(folder / "rank-cal.png", optimize=True)

    for rid, fname in MAP.items():
        if rid < 0:
            continue
        p = OBB_DUMP / fname
        if not p.exists():
            raise FileNotFoundError(p)
        img = fit(Image.open(p).convert("RGBA"))
        for folder in OUTS:
            img.save(folder / f"rank-{rid}.png", optimize=True)
        print("ok", rid, fname)

    # BP icon
    bp_src = OBB_DUMP / "battle_pass_medal_gold_141x161.png"
    if bp_src.exists():
        bp = fit(Image.open(bp_src).convert("RGBA"))
        for folder in OUTS:
            bp.save(folder / "bp.png", optimize=True)

    print("ranks ready")


if __name__ == "__main__":
    main()
