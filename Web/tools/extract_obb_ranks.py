from pathlib import Path
import re
import zipfile
from PIL import Image

obb = Path(r"C:\Users\Administrator\Desktop\0.17.0\main.961.com.axlebolt.standoff2.obb")
out = Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\ranks")
www = Path(r"C:\StandRise\Web\wwwroot\ranks")
tmp = Path(r"C:\StandRise\Web\tools\_unity3d")
tmp.mkdir(parents=True, exist_ok=True)
unity = tmp / "data.unity3d"

if not unity.exists() or unity.stat().st_size < 1000:
    print("extracting data.unity3d from obb...")
    with zipfile.ZipFile(obb) as z:
        with z.open("assets/bin/Data/data.unity3d") as src, open(unity, "wb") as dst:
            while True:
                chunk = src.read(8 * 1024 * 1024)
                if not chunk:
                    break
                dst.write(chunk)
print("unity3d", unity.stat().st_size)

import UnityPy
print("loading UnityPy...")
env = UnityPy.load(str(unity))
print("objects", len(env.objects))

rank_map = {}
bp = None
saved = 0
dump = out / "_obb"
dump.mkdir(parents=True, exist_ok=True)

for obj in env.objects:
    try:
        if obj.type.name not in ("Texture2D", "Sprite"):
            continue
        data = obj.read()
        name = (getattr(data, "name", None) or getattr(data, "m_Name", "") or "")
        low = name.lower()
        if not low:
            continue
        interesting = any(k in low for k in (
            "rank", "ranked", "league", "badge", "bronze", "silver", "gold",
            "phantom", "chimera", "master", "elite", "legend", "battlepass",
            "goldpass", "medal"
        ))
        if not interesting:
            continue
        img = data.image
        if img is None:
            continue
        img = img.convert("RGBA")
        if img.width < 24 or img.height < 24:
            continue
        safe = re.sub(r"[^a-z0-9_-]+", "_", low)[:80]
        p = dump / f"{safe}_{img.width}x{img.height}.png"
        img.save(p)
        saved += 1
        m = re.search(r"(?:rank|ranked|league|badge)[_-]?(\d{1,2})$", low)
        if m:
            n = int(m.group(1))
            if 0 <= n <= 16:
                rank_map[n] = p
        if "battlepass_icon" in low or (bp is None and "battlepass" in low and "icon" in low):
            bp = p
    except Exception:
        continue

print("saved interesting", saved, "rank_map", sorted(rank_map))
if bp:
    print("bp", bp)

# print dumped names
for f in sorted(dump.glob("*.png"))[:80]:
    print(f.name, f.stat().st_size)
