from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "design" / "mockups" / "03-shared-wishlist-selected-concept.png"
IMPLEMENTATION = ROOT / "qa" / "implementation-desktop.png"
QA = ROOT / "qa"


def labeled_pair(left: Image.Image, right: Image.Image, labels: tuple[str, str]) -> Image.Image:
    label_height = 38
    height = max(left.height, right.height)
    canvas = Image.new("RGB", (left.width + right.width, height + label_height), "#f7f5ef")
    canvas.paste(left, (0, label_height))
    canvas.paste(right, (left.width, label_height))
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 12), labels[0], fill="#123f2b")
    draw.text((left.width + 12, 12), labels[1], fill="#123f2b")
    return canvas


def main() -> None:
    QA.mkdir(parents=True, exist_ok=True)
    with Image.open(SOURCE).convert("RGB") as source, Image.open(IMPLEMENTATION).convert("RGB") as implementation:
        source_normalized = source.crop(((source.width - 1424) // 2, 0, (source.width + 1424) // 2, 1024))
        implementation_normalized = implementation.crop((0, 0, 1424, 1024))
        labeled_pair(
            source_normalized,
            implementation_normalized,
            ("SOURCE — OPTION 3", "IMPLEMENTATION — DESKTOP"),
        ).save(QA / "comparison-full.png")

        source_profile = source.crop((0, 0, 500, 1024)).resize((500, 1024))
        implementation_profile = implementation.crop((0, 0, 464, 1024)).resize((500, 1024))
        profile_pair = labeled_pair(source_profile, implementation_profile, ("SOURCE — PROFILE", "IMPLEMENTATION — PROFILE"))

        source_cards = source.crop((500, 0, 1536, 720)).resize((740, 514))
        implementation_cards = implementation.crop((464, 0, 1424, 720)).resize((740, 555))
        cards_pair = labeled_pair(source_cards, implementation_cards, ("SOURCE — CARDS", "IMPLEMENTATION — CARDS"))

        focus = Image.new("RGB", (1480, profile_pair.height + cards_pair.height), "white")
        focus.paste(profile_pair, (240, 0))
        focus.paste(cards_pair, (0, profile_pair.height))
        focus.save(QA / "comparison-focus.png")


if __name__ == "__main__":
    main()
