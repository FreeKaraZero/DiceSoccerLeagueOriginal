# Dice Soccer - native remake (Godot 4)

A native Godot 4 remake of the 2012 iOS game whose `.ipa` sits in `../Payload`.
The bundle contains no source code, so the gameplay is a clean-room
reconstruction driven by the original data (player cards, abilities, five
seasons of fixtures, prices) and rendered with the original art, audio and font.

Targets Android and iOS as a real native app: no web view, no wrapper.

## Layout

| Path | Contents |
| --- | --- |
| `tools/extract_assets.py` | Bundle -> JSON + PNG extraction (run this first) |
| `tools/cgbi.py` | Decoder for Apple's `CgBI` PNG variant |
| `assets/` | Generated, git-ignored: `data/`, `sprites/`, `images/`, `audio/`, `fonts/` |
| `scripts/data/` | Records and loaders for the extracted JSON |
| `scripts/game/` | Dice sides, ability triggers/effects, match simulation |
| `scripts/career.gd` | Cash, squad, fixtures, five-season progression, save file |
| `scripts/ui/`, `scenes/` | Menu, match, squad, shop and season screens |
| `tests/run_tests.gd` | Headless test suite |

## Getting started

```bash
pip3 install --user "pillow>=11"
python3 tools/extract_assets.py         # writes assets/ from ../Payload
godot --headless --import               # imports the generated assets
godot                                   # play
godot --headless --script res://tests/run_tests.gd
```

Without `assets/` the project still opens, but every screen shows a reminder to
run the extractor: the original art cannot be redistributed by the repository's
generated folder.

## Gameplay model

Reconstructed from the shipped tutorial, handbook and data tables:

- A line-up is `[GK, DEF, DEF, DEF, FWD, FWD, FWD]`; each player is a die whose
  sides come from his card's `DiceSides` field.
- Every encounter the attacking trio rolls against the defending trio. The
  higher total takes the ball and pushes it one of five zones; a tie changes
  nothing. One encounter costs a minute of the clock.
- Sides 16-18 are the `+1/+2/+3` bonuses, 24-26 grant re-rolls, 31/32 are wild.
- Abilities fire on the roll pattern in `BuffTrigger` (pair, triple, straight,
  all odd/even, odd/even total, or a specific rolled value) and apply the effect
  described in the ability text.
- Reaching the opponent's goal zone triggers the central forward against the
  goalkeeper: higher value scores.
- Winning fixtures pays the `CashWin`/`CashDraw`/`CashLose` of the opponent and
  awards stars; collecting enough stars unlocks the next of the five seasons.

## Exports

`export_presets.cfg` ships an Android preset (`com.lambdamugames.dicesoccer`,
arm64 + armv7) and an iOS preset (same bundle id, minimum iOS 12). Building the
Android package needs the export templates plus the Android SDK; the iOS export
produces an Xcode project and therefore requires macOS with Xcode - it cannot be
built on Linux.
