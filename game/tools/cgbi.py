"""Decoder for Apple's "CgBI" PNG variant used by iOS app bundles.

iOS-optimized PNGs carry a private ``CgBI`` chunk, store raw deflate streams
(no zlib header) and keep pixels as premultiplied BGRA. Pillow cannot read
them, so this module reverses those transformations and returns a plain image.
"""

from __future__ import annotations

import struct
import zlib
from pathlib import Path

from PIL import Image

PNG_MAGIC = b"\x89PNG\r\n\x1a\n"
CGBI = b"CgBI"


def _chunks(blob: bytes):
    offset = len(PNG_MAGIC)
    while offset < len(blob):
        (length,) = struct.unpack(">I", blob[offset : offset + 4])
        kind = blob[offset + 4 : offset + 8]
        data = blob[offset + 8 : offset + 8 + length]
        yield kind, data
        offset += 12 + length


def _paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    return b if pb <= pc else c


def _unfilter(raw: bytes, width: int, height: int, bpp: int) -> bytearray:
    stride = width * bpp
    out = bytearray(stride * height)
    pos = 0
    for row in range(height):
        filter_type = raw[pos]
        pos += 1
        line = bytearray(raw[pos : pos + stride])
        pos += stride
        base = row * stride
        prev = out[base - stride : base] if row else bytes(stride)
        if filter_type == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 0xFF
        elif filter_type == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif filter_type == 3:
            for i in range(stride):
                left = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif filter_type == 4:
            for i in range(stride):
                left = line[i - bpp] if i >= bpp else 0
                upper_left = prev[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + _paeth(left, prev[i], upper_left)) & 0xFF
        elif filter_type != 0:
            raise ValueError(f"unsupported PNG filter {filter_type}")
        out[base : base + stride] = line
    return out


def _unpremultiply_bgra(pixels: bytearray) -> None:
    for i in range(0, len(pixels), 4):
        blue, green, red, alpha = pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]
        if alpha and alpha != 0xFF:
            red = min(255, red * 255 // alpha)
            green = min(255, green * 255 // alpha)
            blue = min(255, blue * 255 // alpha)
        pixels[i], pixels[i + 1], pixels[i + 2] = red, green, blue


def open_image(path: Path) -> Image.Image:
    """Open a PNG, transparently decoding the iOS ``CgBI`` variant."""
    blob = path.read_bytes()
    if not blob.startswith(PNG_MAGIC) or blob[12:16] != CGBI:
        return Image.open(path).convert("RGBA")

    idat = bytearray()
    width = height = 0
    for kind, data in _chunks(blob):
        if kind == b"IHDR":
            width, height, depth, color_type, _, _, interlace = struct.unpack(">IIBBBBB", data)
            if (depth, color_type, interlace) != (8, 6, 0):
                raise ValueError(f"unsupported CgBI PNG format in {path.name}")
        elif kind == b"IDAT":
            idat += data
        elif kind == b"IEND":
            break

    pixels = _unfilter(zlib.decompressobj(-zlib.MAX_WBITS).decompress(bytes(idat)), width, height, 4)
    _unpremultiply_bgra(pixels)
    return Image.frombytes("RGBA", (width, height), bytes(pixels))
