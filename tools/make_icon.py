"""Generate src/SysMonitorBar/Assets/SysMonitorBar.ico (multi-size) with Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "SysMonitorBar" / "Assets" / "SysMonitorBar.ico"
SIZE = 256


def draw():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((8, 8, SIZE - 8, SIZE - 8), radius=48, fill=(32, 36, 44, 255))
    # three level bars: CPU / GPU / RAM
    bars = [(0.55, (86, 196, 120)), (0.80, (90, 160, 240)), (0.40, (240, 170, 70))]
    left, bottom, width, gap, height = 44, SIZE - 44, 44, 18, SIZE - 88
    for i, (level, color) in enumerate(bars):
        x = left + i * (width + gap)
        d.rounded_rectangle((x, bottom - height, x + width, bottom), radius=10, fill=(60, 66, 78, 255))
        d.rounded_rectangle((x, bottom - int(height * level), x + width, bottom), radius=10, fill=color + (255,))
    return img


def main():
    OUT.parent.mkdir(parents=True, exist_ok=True)
    draw().save(OUT, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
