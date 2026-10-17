"""Read and write PNG images, including Apple's CgBI PNG variant."""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass
from pathlib import Path

PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


def _chunks(blob: bytes):
    offset = len(PNG_MAGIC)
    while offset < len(blob):
        if offset + 8 > len(blob):
            raise ValueError("Truncated PNG chunk header")
        length = struct.unpack(">I", blob[offset : offset + 4])[0]
        end = offset + 8 + length
        if end + 4 > len(blob):
            raise ValueError("Truncated PNG chunk data")
        kind = blob[offset + 4 : offset + 8]
        yield kind, blob[offset + 8 : end]
        offset = end + 4


def _paeth(left: int, above: int, upper_left: int) -> int:
    prediction = left + above - upper_left
    distances = (
        abs(prediction - left),
        abs(prediction - above),
        abs(prediction - upper_left),
    )
    return (left, above, upper_left)[distances.index(min(distances))]


def _unfilter(
    raw: bytes, width: int, height: int, bytes_per_pixel: int, row_stride: int | None = None
) -> bytearray:
    stride = row_stride if row_stride is not None else width * bytes_per_pixel
    result = bytearray(stride * height)
    offset = 0

    for row in range(height):
        if offset + stride + 1 > len(raw):
            raise ValueError("Truncated PNG image data")
        filter_type = raw[offset]
        offset += 1
        scanline = bytearray(raw[offset : offset + stride])
        offset += stride
        previous = result[(row - 1) * stride : row * stride] if row else bytes(stride)

        if filter_type == 1:
            for index in range(bytes_per_pixel, stride):
                scanline[index] = (scanline[index] + scanline[index - bytes_per_pixel]) & 0xFF
        elif filter_type == 2:
            for index in range(stride):
                scanline[index] = (scanline[index] + previous[index]) & 0xFF
        elif filter_type == 3:
            for index in range(stride):
                left = scanline[index - bytes_per_pixel] if index >= bytes_per_pixel else 0
                scanline[index] = (scanline[index] + ((left + previous[index]) >> 1)) & 0xFF
        elif filter_type == 4:
            for index in range(stride):
                left = scanline[index - bytes_per_pixel] if index >= bytes_per_pixel else 0
                upper_left = previous[index - bytes_per_pixel] if index >= bytes_per_pixel else 0
                scanline[index] = (scanline[index] + _paeth(left, previous[index], upper_left)) & 0xFF
        elif filter_type != 0:
            raise ValueError(f"Unsupported PNG filter: {filter_type}")

        result[row * stride : (row + 1) * stride] = scanline

    return result


def _unpremultiply_bgra(pixels: bytearray) -> None:
    for offset in range(0, len(pixels), 4):
        blue, green, red, alpha = pixels[offset : offset + 4]
        if alpha and alpha != 255:
            red = min(255, red * 255 // alpha)
            green = min(255, green * 255 // alpha)
            blue = min(255, blue * 255 // alpha)
        pixels[offset : offset + 4] = bytes((red, green, blue, alpha))


def _to_rgba(
    samples: bytearray,
    width: int,
    height: int,
    bit_depth: int,
    color_type: int,
    palette: bytes,
    transparency: bytes,
) -> bytearray:
    if color_type == 6:
        return samples

    pixels = bytearray(width * height * 4)
    if color_type == 3 and len(palette) % 3:
        raise ValueError("Invalid PNG palette")

    transparent_gray = struct.unpack(">H", transparency[:2])[0] if color_type == 0 and len(transparency) >= 2 else -1
    transparent_rgb = (
        struct.unpack(">HHH", transparency[:6])
        if color_type == 2 and len(transparency) >= 6
        else (-1, -1, -1)
    )

    for index in range(width * height):
        source = index * (3 if color_type == 2 else 2 if color_type == 4 else 1)
        target = index * 4

        if color_type == 0:
            gray = samples[source]
            alpha = 0 if gray == transparent_gray else 255
            pixels[target : target + 4] = bytes((gray, gray, gray, alpha))
        elif color_type == 2:
            red, green, blue = samples[source : source + 3]
            alpha = 0 if (red, green, blue) == transparent_rgb else 255
            pixels[target : target + 4] = bytes(
                (red, green, blue, alpha)
            )
        elif color_type == 3:
            if bit_depth == 8:
                palette_index = samples[source]
            else:
                row, column = divmod(index, width)
                bit_offset = column * bit_depth
                packed = samples[row * ((width * bit_depth + 7) // 8) + bit_offset // 8]
                palette_index = (packed >> (8 - bit_depth - bit_offset % 8)) & ((1 << bit_depth) - 1)
            palette_offset = palette_index * 3
            if palette_offset + 3 > len(palette):
                raise ValueError("PNG palette index is out of range")
            alpha = transparency[palette_index] if palette_index < len(transparency) else 255
            pixels[target : target + 4] = bytes(
                (*palette[palette_offset : palette_offset + 3], alpha)
            )
        elif color_type == 4:
            gray, alpha = samples[source : source + 2]
            pixels[target : target + 4] = bytes((gray, gray, gray, alpha))
        else:
            raise ValueError(f"Unsupported PNG color type: {color_type}")

    return pixels


@dataclass
class RgbaImage:
    width: int
    height: int
    pixels: bytearray

    def crop(self, left: int, top: int, right: int, bottom: int) -> RgbaImage:
        width = right - left
        height = bottom - top
        if left < 0 or top < 0 or width < 0 or height < 0:
            raise ValueError("Invalid image crop rectangle")
        result = bytearray(width * height * 4)
        for row in range(height):
            start = ((top + row) * self.width + left) * 4
            end = start + width * 4
            result[row * width * 4 : (row + 1) * width * 4] = self.pixels[start:end]
        return RgbaImage(width, height, result)

    def rotate_90(self) -> RgbaImage:
        result = bytearray(self.width * self.height * 4)
        for y in range(self.height):
            for x in range(self.width):
                source = (y * self.width + x) * 4
                target_x = y
                target_y = self.width - x - 1
                target = (target_y * self.height + target_x) * 4
                result[target : target + 4] = self.pixels[source : source + 4]
        return RgbaImage(self.height, self.width, result)

    def paste(self, source: RgbaImage, left: int, top: int) -> None:
        if left < 0 or top < 0 or left + source.width > self.width or top + source.height > self.height:
            raise ValueError("Image paste rectangle is out of bounds")
        for row in range(source.height):
            source_start = row * source.width * 4
            target_start = ((top + row) * self.width + left) * 4
            count = source.width * 4
            self.pixels[target_start : target_start + count] = source.pixels[
                source_start : source_start + count
            ]

    def save(self, path: Path) -> None:
        raw = bytearray()
        stride = self.width * 4
        for row in range(self.height):
            raw.append(0)
            start = row * stride
            raw.extend(self.pixels[start : start + stride])

        path.write_bytes(
            PNG_MAGIC
            + _png_chunk(b"IHDR", struct.pack(">IIBBBBB", self.width, self.height, 8, 6, 0, 0, 0))
            + _png_chunk(b"IDAT", zlib.compress(bytes(raw)))
            + _png_chunk(b"IEND", b"")
        )


def _png_chunk(kind: bytes, data: bytes) -> bytes:
    body = kind + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)


def open_image(path: Path) -> RgbaImage:
    """Decode a non-interlaced 8-bit PNG into RGBA pixels."""
    blob = path.read_bytes()
    if not blob.startswith(PNG_MAGIC):
        raise ValueError(f"Not a PNG image: {path}")

    width = height = bit_depth = color_type = interlace = 0
    palette = transparency = b""
    compressed = bytearray()
    is_cgbi = blob[12:16] == b"CgBI"

    for kind, data in _chunks(blob):
        if kind == b"IHDR":
            width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", data
            )
            if compression != 0 or filtering != 0:
                raise ValueError(f"Unsupported PNG compression or filter method: {path.name}")
        elif kind == b"PLTE":
            palette = data
        elif kind == b"tRNS":
            transparency = data
        elif kind == b"IDAT":
            compressed.extend(data)
        elif kind == b"IEND":
            break

    if interlace != 0 or bit_depth not in (1, 4, 8):
        raise ValueError(f"Unsupported PNG bit depth or interlacing: {path.name}")

    if is_cgbi:
        if bit_depth != 8 or color_type != 6:
            raise ValueError(f"Unsupported CgBI PNG color type: {path.name}")
        raw = zlib.decompressobj(-zlib.MAX_WBITS).decompress(bytes(compressed))
        pixels = _unfilter(raw, width, height, 4)
        _unpremultiply_bgra(pixels)
        return RgbaImage(width, height, pixels)

    if bit_depth < 8 and color_type != 3:
        raise ValueError(f"Unsupported packed PNG color type: {path.name}")
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color_type)
    bytes_per_pixel = None if channels is None else max(1, (channels * bit_depth + 7) // 8)
    if bytes_per_pixel is None:
        raise ValueError(f"Unsupported PNG color type: {path.name}")
    raw = zlib.decompress(bytes(compressed))
    row_stride = (width * channels * bit_depth + 7) // 8
    samples = _unfilter(raw, width, height, bytes_per_pixel, row_stride)
    return RgbaImage(
        width,
        height,
        _to_rgba(samples, width, height, bit_depth, color_type, palette, transparency),
    )
