"""
Иконки званий из OBB 0.17 — два режима:
  allies/       — ranked «Союзники» (bronze_one, gold_one_star, …)
  competitive/  — «Соревновательный» 2v2 (*_2v2, calibration_2v2, …)
Без season_2_ranked_reward, кланов и брелков.
"""
from pathlib import Path
import zipfile
from PIL import Image

OBB = Path(r"C:\Users\Administrator\Desktop\0.17.0\main.961.com.axlebolt.standoff2.obb")
UNITY = Path(r"C:\StandRise\Web\tools\_unity3d\data.unity3d")
OUT_ROOTS = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\ranks"),
    Path(r"C:\StandRise\Web\wwwroot\ranks"),
]
SIZE = 128

# rank id -> имя Sprite в Unity (ProfileRankSettings / Rank2X2View)
ALLIES = {
    -1: "calibration",
    0: "bronze_one",
    1: "bronze_two",
    2: "bronze_three",
    3: "bronze_four",
    4: "silver_one",
    5: "silver_two",
    6: "silver_three",
    7: "silver_four",
    8: "gold_one_star",
    9: "gold_two_stars",
    10: "gold_three_stars",
    11: "gold_four_stars",
    12: "gold_elite_1",
    13: "gold_elite_2",
    14: "gold_elite_3",
    15: "platinum_global",
    16: "diamond_global",
}

COMPETITIVE = {
    -1: "calibration_2v2",
    0: "bronze_one_2v2",
    1: "bronze_two_2v2",
    2: "bronze_three_2v2",
    3: "bronze_four_2v2",
    4: "silver_one_2v2",
    5: "silver_two_2v2",
    6: "silver_three_2v2",
    7: "silver_four_2v2",
    8: "gold_one_star_2v2",
    9: "gold_two_stars_2v2",
    10: "gold_three_stars_2v2",
    11: "gold_four_stars_2v2",
    12: "gold_eagle_2v2",
    13: "platinum_cup_2v2",
    14: "platinum_global_2v2",
    15: "diamond_global_2v2",
    16: "diamond_global_2v2",
}


def ensure_unity():
    if UNITY.exists() and UNITY.stat().st_size > 1_000_000:
        return
    UNITY.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OBB) as z:
        with z.open("assets/bin/Data/data.unity3d") as src, open(UNITY, "wb") as dst:
            while True:
                chunk = src.read(8 * 1024 * 1024)
                if not chunk:
                    break
                dst.write(chunk)


def load_sprites():
    import UnityPy

    ensure_unity()
    env = UnityPy.load(str(UNITY))
    by_name = {}
    for obj in env.objects:
        try:
            if obj.type.name != "Sprite":
                continue
            data = obj.read()
            name = (getattr(data, "name", None) or getattr(data, "m_Name", "") or "").strip()
            if not name or name in by_name:
                continue
            img = data.image
            if img is None:
                continue
            by_name[name] = img.convert("RGBA")
            # Unity часто дублирует с разным регистром
            by_name[name.lower()] = by_name[name]
        except Exception:
            continue
    return by_name


def fit(im: Image.Image) -> Image.Image:
    w, h = im.size
    scale = min(SIZE / w, SIZE / h)
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    im = im.resize((nw, nh), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(im, ((SIZE - nw) // 2, (SIZE - nh) // 2), im)
    return out


def resolve(sprites, sprite_name: str) -> Image.Image:
    for key in (sprite_name, sprite_name.lower()):
        if key in sprites:
            return sprites[key]
    raise KeyError(sprite_name)


def write_mode(sprites, folder_name: str, mapping: dict):
    for root in OUT_ROOTS:
        mode_dir = root / folder_name
        mode_dir.mkdir(parents=True, exist_ok=True)

    for rid, sname in mapping.items():
        img = fit(resolve(sprites, sname))
        fname = "rank-cal.png" if rid < 0 else f"rank-{rid}.png"
        for root in OUT_ROOTS:
            img.save(root / folder_name / fname, optimize=True)
        print(f"  {folder_name} {rid} <- {sname}")

    # legacy flat paths = allies (профиль «Союзники» по умолчанию)
    for rid in range(-1, 17):
        sname = mapping[rid]
        img = fit(resolve(sprites, sname))
        fname = "rank-cal.png" if rid < 0 else f"rank-{rid}.png"
        if folder_name != "competitive":
            continue
        for root in OUT_ROOTS:
            img.save(root / fname, optimize=True)


def export_bp(sprites):
    img = fit(resolve(sprites, "battlepass_current_level_icon"))
    for root in OUT_ROOTS:
        root.mkdir(parents=True, exist_ok=True)
        img.save(root / "bp.png", optimize=True)
        img.save(root / "bp-level.png", optimize=True)
    print("  bp <- battlepass_current_level_icon")


def main():
    sprites = load_sprites()
    print("sprites", len(sprites))
    # В UI «Союзники» = спрайты *_2v2, «Соревновательный» = bronze_one / gold_* без суффикса.
    write_mode(sprites, "allies", COMPETITIVE)
    write_mode(sprites, "competitive", ALLIES)
    export_bp(sprites)
    print("done")


if __name__ == "__main__":
    main()
