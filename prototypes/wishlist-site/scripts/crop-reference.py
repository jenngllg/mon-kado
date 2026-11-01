from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "design" / "mockups" / "03-shared-wishlist-selected-concept.png"
OUTPUT = ROOT / "reference-assets"

CROPS = {
    "portrait.png": (62, 63, 402, 348),
    "basket.png": (525, 68, 802, 379),
    "print.png": (1018, 68, 1265, 372),
    "perfume.png": (525, 404, 804, 698),
    "plant.png": (1018, 404, 1265, 699),
    "blanket.png": (525, 727, 804, 1012),
    "cooking.png": (1018, 727, 1265, 1012),
}


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with Image.open(SOURCE) as source:
        for filename, bounds in CROPS.items():
            source.crop(bounds).save(OUTPUT / filename)


if __name__ == "__main__":
    main()
