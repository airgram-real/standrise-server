"""Extract rank / battle-pass icons from APK+OBB, with a painted fallback."""
from pathlib import Path
import os
import re
import zipfile

from PIL import Image, ImageDraw, ImageFont

OUT = Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\ranks")
WWW = Path(r"C:\StandRise\Web\wwwroot\ranks")
ROOT = Path(r"C:\Users\Administrator\Desktop\0.17.0")
OUT.mkdir(parents=True, exist_ok=True)
WWW.mkdir(parents=True, exist_ok=True)

RANK_HINTS = (
    "rank", "ranked", "league", "badge", "medal", "elo",
    "bronze", "silver", "gold", "phantom", "chimera", "master", "elite", "legend",
)
BP_HINTS = ("battlepass", "battle_pass", "goldpass", "gold_pass", "cursed", "season_pass", "bp_icon")


def collect_archives():
    files = []
    roots = [ROOT, Path(r"C:\Users\Administrator\Desktop"), Path(r"C:\Users\Administrator\Downloads")]
    for root in roots:
        if not root.exists():
            continue
        for p in root.rglob("*"):
            if not p.is_file():
                continue
            if p.suffix.lower() in {".apk", ".obb", ".zip"}:
                files.append(p)
            if p.name.lower() in {"unity.data", "data.unity3d"}:
                files.append(p)
    return files


def try_unitypy(files, dest):
    try:
        import UnityPy
    except Exception as e:
        print("UnityPy missing", e)
        return []
    saved = []
    dest.mkdir(parents=True, exist_ok=True)
    for f in files:
        try:
            env = UnityPy.load(str(f))
        except Exception as e:
            print("skip", f, e)
            continue
        print("scan", f, "objects", len(getattr(env, "objects", [])))
        for obj in env.objects:
            try:
                if obj.type.name not in ("Texture2D", "Sprite"):
                    continue
                data = obj.read()
                name = (getattr(data, "name", None) or getattr(data, "m_Name", "") or "").lower()
                if not name:
                    continue
                img = data.image
                if img is None:
                    continue
                img = img.convert("RGBA")
                if img.width < 24 or img.height < 24:
                    continue
                kind = None
                if any(h in name for h in BP_HINTS):
                    kind = "bp"
                elif any(h in name for h in RANK_HINTS):
                    kind = "rank"
                else:
                    continue
                safe = re.sub(r"[^a-z0-9_-]+", "_", name)[:80]
                out = dest / f"src_{kind}_{safe}.png"
                img.save(out)
                saved.append((kind, name, out, img.size))
            except Exception:
                continue
    print("extracted", len(saved))
    for row in saved[:40]:
        print(" ", row[0], row[1], row[3])
    return saved


def paint_rank(i):
    colors = [
        (70, 70, 74),
        (176, 112, 64), (176, 112, 64), (176, 112, 64), (176, 112, 64),
        (168, 176, 188), (168, 176, 188), (168, 176, 188), (168, 176, 188),
        (212, 176, 72), (212, 176, 72), (212, 176, 72), (212, 176, 72),
        (160, 120, 200), (80, 180, 200), (200, 80, 90), (90, 160, 230), (230, 210, 120),
    ]
    c = colors[i] if i < len(colors) else (180, 180, 180)
    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((8, 8, 120, 120), radius=22, fill=c + (255,))
    d.rounded_rectangle((18, 18, 110, 110), radius=16, fill=(20, 20, 22, 230))
    label = ["?", "I", "II", "III", "IV", "I", "II", "III", "IV", "I", "II", "III", "IV", "Ph", "Ch", "Ma", "El", "Lg"][i]
    try:
        font = ImageFont.truetype("arial.ttf", 42)
    except Exception:
        font = ImageFont.load_default()
    bbox = d.textbbox((0, 0), label, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((128 - tw) / 2, (128 - th) / 2 - 4), label, fill=c + (255,), font=font)
    return im


def paint_bp():
    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((8, 8, 120, 120), radius=18, fill=(212, 176, 72, 255))
    d.polygon([(64, 18), (96, 50), (80, 50), (80, 110), (48, 110), (48, 50), (32, 50)], fill=(40, 28, 8, 255))
    return im


def pick_extracted(saved, dest):
    ranks = [None] * 17
    bp = None
    for kind, name, path, size in saved:
        if kind == "bp" and bp is None:
            bp = path
        if kind != "rank":
            continue
        m = re.search(r"(?:rank|ranked|league)[_-]?(\d{1,2})", name)
        if m:
            n = int(m.group(1))
            if 0 <= n <= 16 and ranks[n] is None:
                ranks[n] = path
    return ranks, bp


def main():
    files = collect_archives()
    print("archives", len(files))
    for f in files[:30]:
        print(" ", f, f.stat().st_size)
    saved = try_unitypy(files, OUT / "_src") if files else []
    ranks, bp = pick_extracted(saved, OUT)

    for i in range(17):
        img = None
        if ranks[i] and Path(ranks[i]).exists():
            img = Image.open(ranks[i]).convert("RGBA")
        if img is None:
            img = paint_rank(i)
        img = img.resize((128, 128), Image.Resampling.LANCZOS)
        for folder in (OUT, WWW):
            folder.mkdir(parents=True, exist_ok=True)
            img.save(folder / f"rank-{i}.png")

    bp_img = Image.open(bp).convert("RGBA") if bp else paint_bp()
    bp_img = bp_img.resize((128, 128), Image.Resampling.LANCZOS)
    for folder in (OUT, WWW):
        bp_img.save(folder / "bp.png")
    print("ranks ready", OUT)


if __name__ == "__main__":
    main()
