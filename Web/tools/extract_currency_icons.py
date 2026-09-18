"""Extract gold/silver currency icons from Standoff 0.17 OBB."""
from pathlib import Path
import zipfile

obb = Path(r"C:\Users\Administrator\Desktop\0.17.0\main.961.com.axlebolt.standoff2.obb")
unity = Path(r"C:\StandRise\Web\tools\_unity3d\data.unity3d")
out = Path(r"C:\StandRise\Web\wwwroot\assets\icons")
pub = Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\assets\icons")

if not unity.exists() or unity.stat().st_size < 1000:
    unity.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(obb) as z:
        with z.open("assets/bin/Data/data.unity3d") as src, open(unity, "wb") as dst:
            while True:
                chunk = src.read(8 * 1024 * 1024)
                if not chunk:
                    break
                dst.write(chunk)

import UnityPy
from PIL import Image

env = UnityPy.load(str(unity))
hits = []
for obj in env.objects:
    try:
        if obj.type.name not in ("Texture2D", "Sprite"):
            continue
        data = obj.read()
        name = (getattr(data, "name", None) or getattr(data, "m_Name", "") or "")
        low = name.lower()
        if "currency" not in low and "inventory" not in low:
            if low not in ("gold_128x128", "gold_125x127") and "silver" not in low:
                continue
        if "gold" not in low and "silver" not in low and "currency" not in low:
            continue
        img = data.image
        if img is None:
            continue
        img = img.convert("RGBA")
        if img.width < 20 or img.height > 512:
            continue
        hits.append((name, img.width, img.height, img))
    except Exception:
        continue

print("hits", len(hits))
for name, w, h, _ in sorted(hits, key=lambda x: x[0])[:40]:
    print(f"  {name} {w}x{h}")

def pick(substr):
    for name, w, h, img in hits:
        if substr in name.lower():
            return img, name
    return None, None

gold, gn = pick("inventory_currency_gold")
if gold is None:
    gold, gn = pick("gold_128")
if gold is None:
    gold, gn = pick("currency_gold")

silver, sn = pick("inventory_currency_silver")
if silver is None:
    silver, sn = pick("inventory_currency_coin")
if silver is None:
    silver, sn = pick("currency_silver")

if not gold or not silver:
    raise SystemExit(f"missing gold={gn} silver={sn}")

for folder in (out, pub):
    folder.mkdir(parents=True, exist_ok=True)
    g = gold.resize((64, 64), Image.Resampling.LANCZOS)
    s = silver.resize((64, 64), Image.Resampling.LANCZOS)
    g.save(folder / "gold.png", optimize=True)
    s.save(folder / "silver.png", optimize=True)
    print("wrote", folder, gn, sn)
