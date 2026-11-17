from PIL import Image, ImageDraw, ImageFont
import math
from pathlib import Path

root = Path(__file__).parent
size = 256
im = Image.new("RGBA", (size, size))
pix = im.load()
for y in range(size):
    t = y / (size - 1)
    c = (23 - int(12*t), 43 - int(21*t), 56 - int(25*t), 255)
    for x in range(size): pix[x, y] = c
d = ImageDraw.Draw(im)
d.rounded_rectangle((7, 7, 248, 248), 58, outline="#34505D", width=4)
d.ellipse((44, 31, 212, 199), fill="#10212C", outline="#5BE3C0", width=8)
cx, cy = 128, 115
for turn in range(3):
    angle = math.radians(turn * 120)
    coords = []
    for x, y in [(0,-3), (4,-55), (19,-64), (32,-43), (28,-20), (12,-7)]:
        coords.append((cx + math.cos(angle)*x - math.sin(angle)*y,
                       cy + math.sin(angle)*x + math.cos(angle)*y))
    d.polygon(coords, fill="#5BE3C0")
d.ellipse((117, 104, 139, 126), fill="#EDF5FA")
font_path = r"C:\Windows\Fonts\segoeuib.ttf"
font = ImageFont.truetype(font_path, 36)
box = d.textbbox((0, 0), "X1", font=font)
d.text(((size-(box[2]-box[0]))/2, 204), "X1", font=font, fill="#EDF5FA", stroke_width=0)
im.save(root / "X1Control.png")
im.save(root / "X1Control.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
