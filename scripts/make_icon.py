from PIL import Image
import sys, os
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'PixelEditor.App', 'Assets') + os.sep   # python scripts/make_icon.py: rebuilds the icon
os.makedirs(OUT, exist_ok=True)

N = 16
img = Image.new('RGBA', (N, N), (0, 0, 0, 0))
px = img.load()

BG, BG2, EDGE = (31, 36, 52, 255), (43, 50, 72, 255), (76, 194, 255, 255)
# rounded square
for y in range(N):
    for x in range(N):
        corner = (x in (0, N - 1) and y in (0, N - 1)) or (x in (0, N - 1) and y in (1, N - 2) and False)
        if (x, y) in [(0, 0), (1, 0), (0, 1), (N - 1, 0), (N - 2, 0), (N - 1, 1), (0, N - 1), (1, N - 1), (0, N - 2), (N - 1, N - 1), (N - 2, N - 1), (N - 1, N - 2)]:
            continue
        px[x, y] = BG2 if y < 3 else BG
# accent color frame
for i in range(2, N - 2):
    for (x, y) in [(i, 0), (i, N - 1), (0, i), (N - 1, i)]:
        px[x, y] = EDGE
for (x, y) in [(1, 1), (N - 2, 1), (1, N - 2), (N - 2, N - 2)]:
    px[x, y] = EDGE

def put(x, y, c): px[x, y] = c
YEL, YEL2, ORG = (255, 214, 64, 255), (255, 180, 40, 255), (200, 120, 30, 255)
PINK, METAL, WOOD, LEAD = (255, 120, 150, 255), (190, 196, 210, 255), (240, 200, 150, 255), (40, 40, 50, 255)
# diagonal pencil
for t in range(5, 11):
    put(t, 15 - t - 1, YEL); put(t, 15 - t, YEL2)
    put(t, 15 - t - 2, ORG); put(t, 15 - t + 1, ORG)
for t in (11,):
    put(t, 15 - t - 1, METAL); put(t, 15 - t, METAL); put(t, 15 - t - 2, (150, 156, 172, 255)); put(t, 15 - t + 1, (150, 156, 172, 255))
for t in (12,):
    put(t, 15 - t - 1, PINK); put(t, 15 - t, PINK); put(t, 15 - t - 2, (210, 80, 110, 255)); put(t, 15 - t + 1, (210, 80, 110, 255))
# sharpened tip
put(4, 10, WOOD); put(4, 11, WOOD); put(3, 11, WOOD); put(4, 12, ORG); put(3, 12, LEAD)
put(2, 12, LEAD)
# pixel trail and palette
for (x, y, c) in [(2, 13, (76, 194, 255, 255)), (3, 13, (76, 194, 255, 255)), (4, 13, (76, 194, 255, 255))]:
    put(x, y, c)
sw = [((9, 11), (255, 77, 109, 255)), ((11, 11), (112, 224, 0, 255)), ((9, 13), (181, 107, 255, 255)), ((11, 13), (255, 190, 11, 255))]
for (x, y), c in sw:
    for dx in (0, 1):
        for dy in (0, 1):
            put(x + dx, y + dy, c)

sizes = [256, 128, 64, 48, 32, 24, 16]
frames = []
for s in sizes:
    frames.append(img.resize((s, s), Image.NEAREST))
frames[0].save(OUT + 'app.ico', format='ICO', sizes=[(s, s) for s in sizes], append_images=frames[1:])
img.resize((256, 256), Image.NEAREST).save(OUT + 'app-256.png')
img.resize((64, 64), Image.NEAREST).save(OUT + 'logo-64.png')
print('ok')
