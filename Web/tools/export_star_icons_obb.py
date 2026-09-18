"""Telegram-style star tiers from OBB (gold_one/two/three_stars)."""
from pathlib import Path
import UnityPy
from PIL import Image

UNITY = Path(r"C:\StandRise\Web\tools\_unity3d\data.unity3d")
MAP = {
    "s": "gold_one_star",
    "m": "gold_two_stars",
    "l": "gold_three_stars",
}
OUT = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\assets\icons"),
    Path(r"C:\StandRise\Web\wwwroot\assets\icons"),
]


def get_sprite(env, name):
    for obj in env.objects:
        try:
            if obj.type.name != "Sprite":
                continue
            d = obj.read()
            n = (getattr(d, "name", None) or getattr(d, "m_Name", "") or "")
            if n.lower() == name.lower():
                return d.image.convert("RGBA")
        except Exception:
            pass
    return None


def main():
    env = UnityPy.load(str(UNITY))
    for tier, sprite_name in MAP.items():
        im = get_sprite(env, sprite_name)
        if im is None:
            raise SystemExit(f"missing {sprite_name}")
        for root in OUT:
            root.mkdir(parents=True, exist_ok=True)
            im.save(root / f"star-{tier}.png", optimize=True)
        print("ok", tier, sprite_name, im.size)


if __name__ == "__main__":
    main()
