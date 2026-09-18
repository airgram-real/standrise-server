"""Battle Pass UI assets from OBB."""
from pathlib import Path
import UnityPy
from PIL import Image

UNITY = Path(r"C:\StandRise\Web\tools\_unity3d\data.unity3d")
OUT = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\ranks"),
    Path(r"C:\StandRise\Web\wwwroot\ranks"),
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


def fit_icon(im, size=128):
    w, h = im.size
    scale = min(size / w, size / h)
    nw, nh = max(1, int(w * scale)), max(1, int(h * scale))
    im = im.resize((nw, nh), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(im, ((size - nw) // 2, (size - nh) // 2), im)
    return out


def main():
    env = UnityPy.load(str(UNITY))
    free = get_sprite(env, "battlepass_current_level_icon")
    gold = get_sprite(env, "battle_pass_xp_gold")
    prog = get_sprite(env, "battlepass_progress")
    prog_bg = get_sprite(env, "battlepass_progress_background")
    if not all([free, gold, prog, prog_bg]):
        raise SystemExit("missing BP sprites")

    for root in OUT:
        root.mkdir(parents=True, exist_ok=True)
        fit_icon(free, 56).save(root / "bp-free.png", optimize=True)
        # Gold pass frame — сохраняем пропорции оригинала (не в квадрат).
        gw, gh = gold.size
        pad = 4
        gout = Image.new("RGBA", (gw + pad * 2, gh + pad * 2), (0, 0, 0, 0))
        gout.paste(gold, (pad, pad), gold)
        gout.save(root / "bp-gold.png", optimize=True)
        prog.save(root / "bp-progress.png", optimize=True)
        prog_bg.save(root / "bp-progress-bg.png", optimize=True)
        free.save(root / "bp.png", optimize=True)
    print("bp ui ok", free.size, gold.size, prog.size)


if __name__ == "__main__":
    main()
