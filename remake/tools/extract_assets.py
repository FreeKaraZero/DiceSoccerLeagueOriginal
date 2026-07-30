#!/usr/bin/env python3
"""Extract the original Dice Soccer iOS bundle into engine-agnostic assets.

Reads ``Payload/Dice Soccer.app`` (binary plists, cocos2d spritesheets, audio)
and writes plain JSON plus upright PNG atlases into ``remake/public``.

Usage: python3 tools/extract_assets.py [--src <app bundle>] [--out <dir>]
"""

from __future__ import annotations

import argparse
import json
import plistlib
import re
import shutil
from collections.abc import Iterable
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

from cgbi import open_image

RECT_RE = re.compile(r"-?\d+(?:\.\d+)?")

# Retina sheets/images are the highest fidelity source available.
HD_SUFFIX = "-hd"
IPAD_SUFFIX = "-ipad"


def parse_numbers(value: str) -> list[float]:
    return [float(n) for n in RECT_RE.findall(value)]


@dataclass(frozen=True)
class Frame:
    name: str
    source_w: int
    source_h: int
    tex_x: int
    tex_y: int
    tex_w: int
    tex_h: int
    rotated: bool
    offset_x: int
    offset_y: int


def parse_frame(name: str, info: dict) -> Frame:
    tex_x, tex_y, tex_w, tex_h = (int(n) for n in parse_numbers(info["textureRect"]))
    source_w, source_h = (int(n) for n in parse_numbers(info["spriteSourceSize"]))
    color_x, color_y = (int(n) for n in parse_numbers(info["spriteColorRect"])[:2])
    return Frame(
        name=name,
        source_w=source_w,
        source_h=source_h,
        tex_x=tex_x,
        tex_y=tex_y,
        tex_w=tex_w,
        tex_h=tex_h,
        rotated=bool(info.get("textureRotated")),
        offset_x=color_x,
        offset_y=color_y,
    )


def is_spritesheet(data: object) -> bool:
    return isinstance(data, dict) and isinstance(data.get("frames"), dict) and "metadata" in data


def extract_frame(sheet: Image.Image, frame: Frame) -> Image.Image:
    region = sheet.crop((frame.tex_x, frame.tex_y, frame.tex_x + frame.tex_w, frame.tex_y + frame.tex_h))
    if frame.rotated:
        region = region.transpose(Image.Transpose.ROTATE_90)
    out = Image.new("RGBA", (frame.source_w, frame.source_h))
    out.paste(region, (frame.offset_x, frame.offset_y))
    return out


def shelf_pack(sizes: list[tuple[str, int, int]], max_width: int = 2048) -> tuple[dict[str, tuple[int, int]], int, int]:
    """Pack rectangles into rows sorted by descending height."""
    positions: dict[str, tuple[int, int]] = {}
    x = y = row_height = width = 0
    for name, w, h in sorted(sizes, key=lambda s: -s[2]):
        if x + w > max_width:
            x = 0
            y += row_height
            row_height = 0
        positions[name] = (x, y)
        x += w
        width = max(width, x)
        row_height = max(row_height, h)
    return positions, width, y + row_height


def convert_spritesheet(plist_path: Path, data: dict, out_dir: Path) -> None:
    png_path = plist_path.with_suffix(".png")
    if not png_path.exists():
        return
    frames = [parse_frame(name, info) for name, info in data["frames"].items()]
    sheet = open_image(png_path)
    images = {f.name: extract_frame(sheet, f) for f in frames}

    positions, width, height = shelf_pack([(f.name, f.source_w, f.source_h) for f in frames])
    atlas = Image.new("RGBA", (max(width, 1), max(height, 1)))
    json_frames = {}
    for frame in frames:
        x, y = positions[frame.name]
        atlas.paste(images[frame.name], (x, y))
        json_frames[frame.name] = {
            "frame": {"x": x, "y": y, "w": frame.source_w, "h": frame.source_h},
            "rotated": False,
            "trimmed": False,
            "sourceSize": {"w": frame.source_w, "h": frame.source_h},
            "spriteSourceSize": {"x": 0, "y": 0, "w": frame.source_w, "h": frame.source_h},
        }

    name = atlas_name(plist_path.stem)
    (out_dir / "atlas").mkdir(parents=True, exist_ok=True)
    atlas.save(out_dir / "atlas" / f"{name}.png")
    (out_dir / "atlas" / f"{name}.json").write_text(
        json.dumps(
            {"frames": json_frames, "meta": {"image": f"{name}.png", "size": {"w": atlas.width, "h": atlas.height}, "scale": "1"}},
            indent=1,
        )
    )


def atlas_name(stem: str) -> str:
    for suffix in (HD_SUFFIX, IPAD_SUFFIX):
        if stem.endswith(suffix):
            return stem[: -len(suffix)]
    return stem


def preferred_plists(src: Path) -> list[Path]:
    return preferred_variants(src.glob("*.plist"))


def preferred_variants(paths: Iterable[Path]) -> list[Path]:
    """Keep one file per logical name, preferring the retina (``-hd``) variant."""
    by_name: dict[str, Path] = {}
    for path in sorted(paths):
        key = atlas_name(path.stem)
        current = by_name.get(key)
        if current is None or path.stem.endswith(HD_SUFFIX):
            by_name[key] = path
    return list(by_name.values())


SKIPPED_DATA = {"Info", "Entitlements", "ResourceRules"}


def main() -> None:
    parser = argparse.ArgumentParser()
    here = Path(__file__).resolve().parents[1]
    parser.add_argument("--src", type=Path, default=here.parent / "Payload" / "Dice Soccer.app")
    parser.add_argument("--out", type=Path, default=here / "public")
    args = parser.parse_args()

    src: Path = args.src
    out: Path = args.out
    if not src.is_dir():
        raise SystemExit(f"original app bundle not found: {src}")

    for sub in ("data", "atlas", "images", "audio", "fonts"):
        shutil.rmtree(out / sub, ignore_errors=True)
        (out / sub).mkdir(parents=True, exist_ok=True)

    sheet_names: set[str] = set()
    data_files = 0
    sheets = 0
    for plist_path in preferred_plists(src):
        with plist_path.open("rb") as handle:
            data = plistlib.load(handle)
        if is_spritesheet(data):
            sheet_names.add(atlas_name(plist_path.stem))
            convert_spritesheet(plist_path, data, out)
            sheets += 1
        elif plist_path.stem not in SKIPPED_DATA:
            (out / "data" / f"{atlas_name(plist_path.stem)}.json").write_text(json.dumps(data, indent=1, default=str))
            data_files += 1

    images = 0
    for png in preferred_variants(src.glob("*.png")):
        if atlas_name(png.stem) in sheet_names:
            continue
        open_image(png).save(out / "images" / f"{atlas_name(png.stem)}.png")
        images += 1

    for audio in sorted(src.glob("*.mp3")):
        shutil.copy2(audio, out / "audio" / audio.name)
    for font in sorted(src.glob("*.ttf")):
        shutil.copy2(font, out / "fonts" / font.name)

    print(f"data: {data_files} json, atlases: {sheets}, loose images: {images}")


if __name__ == "__main__":
    main()
