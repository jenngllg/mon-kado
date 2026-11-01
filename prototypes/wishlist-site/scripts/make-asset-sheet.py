from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "public" / "assets"
FILES = [
    "lea-portrait.png",
    "gift-picnic-basket.png",
    "gift-botanical-print.png",
    "gift-cotton-perfume.png",
    "gift-pilea-plant.png",
    "gift-merino-blanket.png",
    "gift-cooking-class.png",
    "sage-leaf-pattern.png",
]


def main() -> None:
    tile = 320
    label = 42
    sheet = Image.new("RGB", (tile * 4, (tile + label) * 2), "white")
    draw = ImageDraw.Draw(sheet)
    for index, filename in enumerate(FILES):
        with Image.open(ASSETS / filename) as image:
            image = image.convert("RGB")
            image.thumbnail((tile, tile))
            x = (index % 4) * tile + (tile - image.width) // 2
            y = (index // 4) * (tile + label) + (tile - image.height) // 2
            sheet.paste(image, (x, y))
            draw.text(((index % 4) * tile + 8, (index // 4) * (tile + label) + tile + 10), filename, fill="#123f2b")
    sheet.save(ROOT / "asset-sheet.jpg", quality=88)


if __name__ == "__main__":
    main()
