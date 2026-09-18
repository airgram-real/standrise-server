"""GIF звёзд Telegram (из чата) -> PNG s/m/l."""
from pathlib import Path
from PIL import Image

BASE = Path(
    r"C:\Users\Administrator\.cursor\projects\c-Users-Administrator-Desktop-StandRise-FULL-20260910-2203\assets"
)
# от большой стоимости к маленькой (как прислал пользователь)
MAP = {
    "l": BASE
    / "c__Users_Administrator_AppData_Roaming_Cursor_User_workspaceStorage_e704a0f3d5f8ecd95218848293061c46_images_13698_1f15adcc0551a1babca5318d4972fe20-badd167f-7de2-4db8-84b6-77687a5ac5c2.gif",
    "m": BASE
    / "c__Users_Administrator_AppData_Roaming_Cursor_User_workspaceStorage_e704a0f3d5f8ecd95218848293061c46_images_13697_69eff8d2a8a8d195f841b71da8380246-978457eb-da66-4401-accc-7c77bdd4b07a.gif",
    "s": BASE
    / "c__Users_Administrator_AppData_Roaming_Cursor_User_workspaceStorage_e704a0f3d5f8ecd95218848293061c46_images_13696_fec91a61b23af9be1b305808ce0ad0fb-34c6fac0-f047-4ee5-97a6-61ef6dba5fe9.gif",
}
LOGO_SRC = BASE / (
    "c__Users_Administrator_AppData_Roaming_Cursor_User_workspaceStorage_e704a0f3d5f8ecd95218848293061c46_images_"
    "photo_4999059453004942545_y-2eda0d7d-eb4b-4722-8a5c-7167a526535c.png"
)
LOGO_OUT = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\Logo.png"),
    Path(r"C:\StandRise\Web\wwwroot\Logo.png"),
]
OUT = [
    Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\assets\icons"),
    Path(r"C:\StandRise\Web\wwwroot\assets\icons"),
]


def knock_black(im: Image.Image) -> Image.Image:
    im = im.convert("RGBA")
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if r < 24 and g < 24 and b < 24:
                px[x, y] = (r, g, b, 0)
    return im


def resolve_src(tier: str, default: Path) -> Path:
    user_dir = Path(r"C:\Users\Administrator\Downloads\ProjectRework\ProjectRework\public\assets\icons\source")
    alt = user_dir / f"star-{tier}.gif"
    if alt.is_file() and alt.stat().st_size > 500:
        return alt
    if default.is_file():
        return default
    raise FileNotFoundError(alt)


def main():
    for tier, src in MAP.items():
        src = resolve_src(tier, src)
        im = Image.open(src)
        try:
            im.seek(0)
        except Exception:
            pass
        im = knock_black(im)
        bbox = im.getbbox()
        if bbox:
            im = im.crop(bbox)
        for root in OUT:
            root.mkdir(parents=True, exist_ok=True)
            im.save(root / f"star-{tier}.png", optimize=True)
        print("ok", tier, src.name, im.size)


def copy_logo() -> None:
    import shutil

    alt = Path(r"C:\Users\Administrator\Downloads\photo_4999059453004942545_y.png")
    src = LOGO_SRC if LOGO_SRC.exists() else alt
    if not src.exists():
        raise FileNotFoundError(src)
    for dst in LOGO_OUT:
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
    print("logo ok", src, "->", LOGO_OUT[0])


if __name__ == "__main__":
    main()
    copy_logo()
