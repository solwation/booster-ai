"""Draws assets/boosterstart.png and .ico (rounded gradient square with a lightning bolt).
Pure standard library, so the icon can be regenerated anywhere: python3 tools/make_icon.py"""
import struct, zlib, pathlib

N = 256
BOLT = [(150, 22), (62, 142), (122, 142), (100, 234), (196, 104), (134, 104), (164, 22)]

def inside(poly, x, y):
    c = False
    for (x1, y1), (x2, y2) in zip(poly, poly[1:] + poly[:1]):
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c

def coverage(x, y):
    """4x4 supersampled: (square alpha, bolt alpha)."""
    sq = bo = 0
    r = 56
    for i in range(4):
        for j in range(4):
            px, py = x + (i + .5) / 4, y + (j + .5) / 4
            cx = min(max(px, r + 8), N - r - 8); cy = min(max(py, r + 8), N - r - 8)
            if (px - cx) ** 2 + (py - cy) ** 2 <= r * r: sq += 1
            if inside(BOLT, px, py): bo += 1
    return sq / 16, bo / 16

def render(n):
    rows = []
    s = N / n
    for y in range(n):
        row = bytearray([0])
        for x in range(n):
            a, b = coverage(x * s, y * s)
            t = (x + y) / (2 * n)
            base = (int(30 + (136 - 30) * t), int(102 + (57 - 102) * t), int(245 + (239 - 245) * t))
            col = [int(base[k] * (1 - b) + 255 * b) for k in range(3)]
            row += bytes(col + [int(255 * max(a, b))])
        rows.append(bytes(row))
    return rows

def png(n):
    raw = b"".join(render(n))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

out = pathlib.Path(__file__).resolve().parent.parent / "assets"
out.mkdir(exist_ok=True)
images = {n: png(n) for n in (256, 64, 48, 32, 16)}
(out / "boosterstart.png").write_bytes(images[256])
head = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries = data = b""
for n, img in images.items():
    entries += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(img), offset)
    offset += len(img); data += img
(out / "boosterstart.ico").write_bytes(head + entries + data)
print("wrote", out)
