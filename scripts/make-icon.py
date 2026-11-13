"""Generate the application-owned icon, without third-party artwork or packages."""
import math
import pathlib
import struct
import zlib


def png(size):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            u, v = (x + .5) / size, (y + .5) / size
            radius = .18
            dx, dy = max(.08 + radius - u, 0, u - (.92 - radius)), max(.08 + radius - v, 0, v - (.92 - radius))
            inside = math.hypot(dx, dy) < radius
            color = (int(40 + 30 * v), int(110 + 45 * v), int(245 - 10 * u), 255 if inside else 0)
            # A white U and a small play cutout, designed on a normalized grid.
            stem = .29 < u < .39 and .27 < v < .64 or .61 < u < .71 and .27 < v < .64
            ring = .11 < math.hypot(u - .5, v - .6) < .21 and v >= .6
            if inside and (stem or ring):
                color = (245, 250, 255, 255)
            raw.extend(color)

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>2I5B', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')


sizes = [16, 32, 48, 64, 128, 256]
images = [png(s) for s in sizes]
offset = 6 + 16 * len(sizes)
entries = bytearray()
for size, data in zip(sizes, images):
    entries.extend(struct.pack('<4B2H2I', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
pathlib.Path(__file__).resolve().parents[1].joinpath('assets/UltraLightWallpaper.ico').write_bytes(struct.pack('<3H', 0, 1, len(sizes)) + entries + b''.join(images))
