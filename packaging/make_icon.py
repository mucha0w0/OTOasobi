from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


def make_icon(path: Path) -> None:
    size = 256
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle((8, 8, size - 8, size - 8), radius=48, fill=(22, 22, 22, 255))
    # waveform bars
    bars = [
        (0.18, 0.28),
        (0.30, 0.55),
        (0.42, 0.38),
        (0.54, 0.72),
        (0.66, 0.44),
        (0.78, 0.22),
    ]
    colors = [(154, 160, 166), (217, 119, 6), (15, 157, 142), (217, 119, 6), (15, 157, 142), (154, 160, 166)]
    width = 22
    for (x_frac, h_frac), color in zip(bars, colors):
        x = int(size * x_frac)
        h = int(size * h_frac)
        y0 = size // 2 - h
        y1 = size // 2 + h
        draw.rounded_rectangle((x, y0, x + width, y1), radius=6, fill=color + (255,))
    img.save(
        path,
        sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)],
    )


if __name__ == "__main__":
    out = Path(__file__).with_name("app.ico")
    make_icon(out)
    print(out)
