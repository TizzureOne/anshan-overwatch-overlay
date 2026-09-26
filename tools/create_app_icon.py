"""Build the original Anshan monogram from the same vector polygons as app.svg."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1] / 'src/AnshanOverlay/Assets'
shapes = [
    ('#192B40', [(44, 8), (212, 8), (248, 44), (248, 212), (212, 248), (44, 248), (8, 212), (8, 44)]),
    ('#F4F6F9', [(46, 194), (109, 52), (142, 52), (205, 194), (166, 194), (125, 97), (84, 194)]),
    ('#FF9C27', [(75, 165), (174, 123), (164, 102), (218, 119), (196, 172), (187, 150), (88, 192)]),
    ('#39A9FF', [(48, 206), (85, 206), (79, 220), (42, 220)]),
]
svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">\n' + '\n'.join(
    f'<polygon fill="{color}" points="' + ' '.join(f'{x},{y}' for x, y in points) + '"/>' for color, points in shapes) + '\n</svg>\n'
(root / 'app.svg').write_text(svg, encoding='utf-8')
im = Image.new('RGBA', (1024, 1024))
draw = ImageDraw.Draw(im)
for color, points in shapes:
    draw.polygon([(x * 4, y * 4) for x, y in points], fill=color)
im = im.resize((256, 256), Image.Resampling.LANCZOS)
im.save(root / 'app.png')
im.save(root / 'app.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print('Created original SVG, PNG and seven-size ICO')
