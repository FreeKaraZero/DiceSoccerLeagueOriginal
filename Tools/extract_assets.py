#!/usr/bin/env python3
"""Extract the original app bundle into Unity-importable resources."""

from __future__ import annotations

import argparse
import json
import plistlib
import re
import shutil
from collections.abc import Iterable
from dataclasses import dataclass
from pathlib import Path

from cgbi import RgbaImage, open_image

NUMBER = re.compile(r"-?\d+(?:\.\d+)?")
SKIPPED_DATA = {"Info", "Entitlements", "ResourceRules"}


def numbers(value: str) -> list[float]:
    return [float(number) for number in NUMBER.findall(value)]


def logical_name(stem: str) -> str:
    for suffix in ("-hd", "-ipad"):
        if stem.endswith(suffix):
            return stem[: -len(suffix)]
    return stem


def preferred_variants(paths: Iterable[Path]) -> list[Path]:
    selected: dict[str, Path] = {}
    for path in sorted(paths):
        key = logical_name(path.stem)
        current = selected.get(key)
        if current is None or path.stem.endswith("-hd"):
            selected[key] = path
    return list(selected.values())


@dataclass(frozen=True)
class Frame:
    name: str
    source_width: int
    source_height: int
    texture_x: int
    texture_y: int
    texture_width: int
    texture_height: int
    rotated: bool
    offset_x: int
    offset_y: int


def parse_frame(name: str, info: dict) -> Frame:
    texture_x, texture_y, texture_width, texture_height = (
        int(value) for value in numbers(info["textureRect"])
    )
    source_width, source_height = (
        int(value) for value in numbers(info["spriteSourceSize"])
    )
    offset_x, offset_y = (int(value) for value in numbers(info["spriteColorRect"])[:2])
    return Frame(
        name=name,
        source_width=source_width,
        source_height=source_height,
        texture_x=texture_x,
        texture_y=texture_y,
        texture_width=texture_width,
        texture_height=texture_height,
        rotated=bool(info.get("textureRotated")),
        offset_x=offset_x,
        offset_y=offset_y,
    )


def extract_frame(sheet: RgbaImage, frame: Frame) -> RgbaImage:
    region = sheet.crop(
        frame.texture_x,
        frame.texture_y,
        frame.texture_x + frame.texture_width,
        frame.texture_y + frame.texture_height,
    )
    if frame.rotated:
        region = region.rotate_90()
    result = RgbaImage(
        frame.source_width,
        frame.source_height,
        bytearray(frame.source_width * frame.source_height * 4),
    )
    result.paste(region, frame.offset_x, frame.offset_y)
    return result


def is_spritesheet(data: object) -> bool:
    return isinstance(data, dict) and isinstance(data.get("frames"), dict) and "metadata" in data


def extract_spritesheet(plist_path: Path, data: dict, output: Path) -> int:
    image_path = plist_path.with_suffix(".png")
    if not image_path.exists():
        return 0

    sheet = open_image(image_path)
    atlas_name = logical_name(plist_path.stem)
    target = output / "sprites" / atlas_name
    target.mkdir(parents=True, exist_ok=True)
    frame_records = []
    for name, info in data["frames"].items():
        frame = parse_frame(name, info)
        extract_frame(sheet, frame).save(target / f"{Path(frame.name).stem}.png")
        frame_records.append(
            {
                "name": Path(frame.name).stem,
                "sourceWidth": frame.source_width,
                "sourceHeight": frame.source_height,
                "offsetX": frame.offset_x,
                "offsetY": frame.offset_y,
                "textureX": frame.texture_x,
                "textureY": frame.texture_y,
                "textureWidth": frame.texture_width,
                "textureHeight": frame.texture_height,
                "rotated": frame.rotated,
            }
        )
    animation_dir = output / "animations"
    animation_dir.mkdir(parents=True, exist_ok=True)
    (animation_dir / f"{atlas_name}.json").write_text(
        json.dumps({"name": atlas_name, "frames": frame_records}, indent=1),
        encoding="utf-8",
    )
    return len(data["frames"])


def reset_generated_folders(output: Path) -> None:
    for name in ("data", "sprites", "images", "audio", "fonts", "animations"):
        folder = output / name
        if folder.exists():
            shutil.rmtree(folder)
        folder.mkdir(parents=True)


def _player_record(player_id: int, record: dict) -> dict:
    sides = record.get("DiceSides", "")
    if isinstance(sides, str):
        dice_sides = [int(side.strip()) for side in sides.split(",") if side.strip()]
    else:
        dice_sides = [int(side) for side in sides]
    return {
        "id": player_id,
        "name": str(record.get("Name", f"Player {player_id}")),
        "position": str(record.get("Position", "")),
        "cost": int(record.get("Player Cost", 0)),
        "sellingPrice": int(record.get("Selling Price", 0)),
        "imageId": int(record.get("ImageID", 0)),
        "skinColour": int(record.get("SkinColour", 0)),
        "description": str(record.get("Player Description", "")),
        "unlockCriteria": str(record.get("Unlock Criteria", "")),
        "superpower": int(record.get("Superpower", 0)),
        "gender": int(record.get("Gender", 0)),
        "diceSides": dice_sides,
    }


def build_unity_catalog(source: Path) -> dict:
    with (source / "PlayerData.plist").open("rb") as handle:
        raw_players = plistlib.load(handle)
    with (source / "DummyPlayerData.plist").open("rb") as handle:
        dummy_players = plistlib.load(handle)
    with (source / "Abilities.plist").open("rb") as handle:
        raw_abilities = plistlib.load(handle)
    with (source / "MatchLineup.plist").open("rb") as handle:
        raw_lineups = plistlib.load(handle)
    with (source / "GameConfig.plist").open("rb") as handle:
        config = plistlib.load(handle)

    players = [
        _player_record(int(player_id), player)
        for player_id, player in raw_players.items()
        if str(player_id).isdigit() and isinstance(player, dict)
    ]
    players.extend(
        _player_record(int(player_id) + 1000, player)
        for player_id, player in dummy_players.items()
        if str(player_id).isdigit() and isinstance(player, dict)
    )

    abilities = []
    for ability_id, ability in raw_abilities.get("Abilities", {}).items():
        if not str(ability_id).isdigit() or not isinstance(ability, dict):
            continue
        abilities.append(
            {
                "id": int(ability_id),
                "name": str(ability.get("Name", "")),
                "description": str(ability.get("AbilityDescription", "")),
                "trigger": int(ability.get("BuffTrigger", 0)),
                "type": int(ability.get("BuffType", 0)),
                "target": int(ability.get("BuffTarget", 0)),
                "slot": int(ability.get("BuffSlot", 0)),
                "localReroll": int(ability.get("LocalReroll", 0)),
            }
        )

    seasons = []
    for season_key, season in raw_lineups.items():
        if not str(season_key).startswith("Season") or not isinstance(season, dict):
            continue
        competitions = []
        for competition_name, competition in season.items():
            if competition_name == "FieldObjects" or not isinstance(competition, dict):
                continue
            fixtures = []
            for fixture_id, fixture in competition.items():
                if not str(fixture_id).isdigit() or not isinstance(fixture, dict):
                    continue
                fixtures.append(
                    {
                        "id": int(fixture_id),
                        "teamName": str(fixture.get("TeamName", "")),
                        "lineup": [int(value) for value in fixture.get("Lineup", [])],
                        "cashWin": int(fixture.get("CashWin", 0)),
                        "cashDraw": int(fixture.get("CashDraw", 0)),
                        "cashLose": int(fixture.get("CashLose", 0)),
                        "shirt": int(fixture.get("Shirt", 0)),
                        "pants": int(fixture.get("Pants", 0)),
                        "shirtPatternColor": int(fixture.get("ShirtPatternColor", 0)),
                        "shirtPatternStyle": int(fixture.get("ShirtPatternStyle", 0)),
                        "sleevesSock": int(fixture.get("SleevesSock", 0)),
                        "emblem": int(fixture.get("Emblem", 0)),
                        "entourageLineup": [
                            int(value) for value in fixture.get("EntourageLineup", [])
                        ],
                        "possessionBonus": int(fixture.get("PBonus", 0)),
                        "eventName": str(fixture.get("Event", "")),
                    }
                )
            competitions.append(
                {
                    "name": str(competition_name),
                    "matchCount": int(competition.get("NoOfMatches", len(fixtures))),
                    "fixtures": sorted(fixtures, key=lambda fixture: fixture["id"]),
                }
            )
        season_id = int(str(season_key).removeprefix("Season"))
        seasons.append(
            {
                "id": season_id,
                "name": str(season.get("Name") or (
                    "Training Season" if season_id == 0 else season_key
                )),
                "fieldObjects": season.get("FieldObjects", {}),
                "competitions": competitions,
            }
        )

    with (source / "Shop.plist").open("rb") as handle:
        raw_shop = plistlib.load(handle)
    shop_cash = []
    for i, c in enumerate(raw_shop.get("Cash", [])):
        if i == 0:
            continue
        shop_cash.append(
            {
                "id": i,
                "price": float(c.get("InAppPurchasePrice", 0)),
                "coins": int(c.get("InGameCoinAmount", 0)),
            }
        )

    with (source / "Shirt.plist").open("rb") as handle:
        raw_shirts = plistlib.load(handle)
    shirts = []
    for k, v in raw_shirts.items():
        if str(k).isdigit() and isinstance(v, dict):
            shirts.append(
                {
                    "id": int(k),
                    "shirtColour": int(v.get("ShirtColour", 0)),
                    "sleeveColour": int(v.get("SleeveColour", 0)),
                    "shirtStyle": int(v.get("ShirtStyle", 0)),
                    "styleColour": int(v.get("StyleColour", 0)),
                    "price": float(v.get("InAppPurchasePrice", 0)),
                }
            )
    shirts.sort(key=lambda s: s["id"])

    with (source / "Colour.plist").open("rb") as handle:
        raw_colours = plistlib.load(handle)
    colours = []
    for k, v in raw_colours.items():
        if str(k).isdigit():
            parts = [int(p.strip()) for p in str(v).split(",")]
            colours.append({"id": int(k), "r": parts[0], "g": parts[1], "b": parts[2]})
    colours.sort(key=lambda c: c["id"])

    with (source / "Achievements.plist").open("rb") as handle:
        raw_achievements = plistlib.load(handle)
    achievements = []
    for k, v in raw_achievements.items():
        if str(k).isdigit() and isinstance(v, dict):
            achievements.append(
                {
                    "id": int(k),
                    "unlockedDescription": str(v.get("UnlockedDescription", "")),
                    "lockedDescription": str(v.get("LockedDescription", "")),
                    "goldReward": int(v.get("GoldReward", 0)),
                    "pantsReward": int(v.get("PantsReward", 0)),
                    "entourageReward": int(v.get("EntourageReward", 0)),
                }
            )
    achievements.sort(key=lambda a: a["id"])

    with (source / "EntourageData.plist").open("rb") as handle:
        raw_entourage = plistlib.load(handle)
    entourage = []
    for k, v in raw_entourage.items():
        if str(k).isdigit() and isinstance(v, dict):
            entourage.append(
                {
                    "id": int(k),
                    "name": str(v.get("Name", "")),
                    "position": str(v.get("Position", "")),
                    "cost": int(v.get("Player Cost", 0)),
                    "abilityTrigger": int(v.get("Ability Trigger", 0)),
                    "abilityIcon": int(v.get("Ability Icon", 0)),
                    "abilityName": str(v.get("Ability Name", "")),
                    "abilityDescription": str(v.get("Ability Description", "")),
                    "playerDescription": str(v.get("Player Description", "")),
                    "inGameDescription": str(v.get("In Game Description", "")),
                    "unlockCriteria": str(v.get("Unlock Criteria", "")),
                    "sellingPrice": int(v.get("Selling Price", 0)),
                    "inappPrice": float(v.get("Inapp", 0)),
                }
            )
    entourage.sort(key=lambda e: e["id"])

    with (source / "Tutorial.plist").open("rb") as handle:
        raw_tutorials = plistlib.load(handle)
    tutorials = []
    for k, v in raw_tutorials.items():
        if str(k).isdigit() and isinstance(v, dict):
            tutorials.append(
                {
                    "id": int(k),
                    "popupText": [str(t) for t in v.get("PopupText", [])],
                    "customPopup": [int(cp) for cp in v.get("CustomPopup", [])],
                }
            )
    tutorials.sort(key=lambda t: t["id"])

    with (source / "PersistentTutorial.plist").open("rb") as handle:
        raw_persistent = plistlib.load(handle)
    persistent_tutorials = []
    for k, v in raw_persistent.items():
        if str(k).isdigit() and isinstance(v, dict):
            persistent_tutorials.append(
                {
                    "id": int(k),
                    "popupText": [str(t) for t in v.get("PopupText", [])],
                    "customPopup": [int(cp) for cp in v.get("CustomPopup", [])],
                }
            )
    persistent_tutorials.sort(key=lambda pt: pt["id"])

    return {
        "players": sorted(players, key=lambda player: player["id"]),
        "abilities": sorted(abilities, key=lambda ability: ability["id"]),
        "seasons": sorted(seasons, key=lambda season: season["id"]),
        "initialLineup": [int(value) for value in config.get("InitialRoster", [])],
        "tips": [str(value) for value in config.get("Tips", [])],
        "shopCash": shop_cash,
        "shirts": shirts,
        "colours": colours,
        "achievements": achievements,
        "entourage": entourage,
        "tutorials": tutorials,
        "persistentTutorials": persistent_tutorials,
    }


def main() -> None:
    project_dir = Path(__file__).resolve().parents[1]
    repository_dir = project_dir.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--src",
        type=Path,
        default=repository_dir / "Payload" / "Dice Soccer.app",
        help="path to the extracted iOS app bundle",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=project_dir / "Assets" / "Resources" / "Original",
        help="generated Unity resources directory",
    )
    args = parser.parse_args()

    source = args.src.resolve()
    output = args.out.resolve()
    if not source.is_dir():
        parser.error(f"original app bundle not found: {source}")

    reset_generated_folders(output)
    data_count = sheet_count = frame_count = 0
    sheet_names: set[str] = set()

    for plist_path in preferred_variants(source.glob("*.plist")):
        with plist_path.open("rb") as handle:
            data = plistlib.load(handle)
        if is_spritesheet(data):
            sheet_names.add(logical_name(plist_path.stem))
            frame_count += extract_spritesheet(plist_path, data, output)
            sheet_count += 1
        elif plist_path.stem not in SKIPPED_DATA:
            filename = logical_name(plist_path.stem) + ".json"
            (output / "data" / filename).write_text(
                json.dumps(data, indent=1, default=str), encoding="utf-8"
            )
            data_count += 1

    image_count = 0
    for image_path in preferred_variants(source.glob("*.png")):
        if logical_name(image_path.stem) in sheet_names:
            continue
        open_image(image_path).save(output / "images" / f"{logical_name(image_path.stem)}.png")
        image_count += 1

    audio_files = sorted(source.glob("*.mp3"))
    font_files = sorted(source.glob("*.ttf"))
    for path in audio_files:
        shutil.copy2(path, output / "audio" / path.name)
    for path in font_files:
        shutil.copy2(path, output / "fonts" / path.name)

    catalog = build_unity_catalog(source)
    (output / "data" / "UnityCatalog.json").write_text(
        json.dumps(catalog, indent=1), encoding="utf-8"
    )

    print(
        f"Extracted {data_count} data files, {sheet_count} atlases "
        f"({frame_count} frames), {image_count} loose images, "
        f"{len(audio_files)} audio files and {len(font_files)} fonts; "
        f"catalog has {len(catalog['players'])} players, "
        f"{len(catalog['abilities'])} abilities and {len(catalog['seasons'])} seasons "
        f"({output})"
    )


if __name__ == "__main__":
    main()
