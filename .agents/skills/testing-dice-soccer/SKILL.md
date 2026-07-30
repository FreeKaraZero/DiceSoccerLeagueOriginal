---
name: testing-dice-soccer
description: How to build, run and UI-test the native Godot 4 Dice Soccer remake in game/ on a headless Linux box with an X display.
---

# Testing the Dice Soccer Godot remake

The playable game lives in `game/` (Godot 4.3, GDScript). Art and data are **git-ignored** and generated out of
the original iOS bundle in `Payload/`, so a fresh clone cannot run the game until they are extracted.

## Prepare

```bash
cd <repo>/game
python3 tools/extract_assets.py        # writes game/assets/{data,sprites,images,audio,fonts}; needs pillow>=11
/home/ubuntu/bin/godot --headless --import   # imports the new assets, otherwise textures are missing
```

If the screens show "Assets missing. Run: python3 tools/extract_assets.py", the extraction or the import step
was skipped.

## Automated checks

```bash
cd <repo>/game
/home/ubuntu/bin/godot --headless --script res://tests/run_tests.gd   # prints "<n> checks, <m> failed"
```
The script exits 0 even conceptually — always read the printed "N failed" count rather than trusting the exit
status.

## Run the game on the desktop

```bash
cd /tmp && DISPLAY=:0 setsid nohup <godot> --path <repo>/game > /tmp/godot_run.log 2>&1 < /dev/null &
```

- **Use `setsid nohup … &` and `cd /tmp`.** Launching it any other way makes the process die as soon as the
  shell call returns, and the window never appears. Verify with
  `DISPLAY=:0 wmctrl -lG | grep "Dice Soccer"`; retry the launch if nothing is listed.
- ALSA "cannot find card '0'" errors in the log are harmless (audio falls back to the dummy driver).
- The window is a fixed 640x960 portrait window. Position it deterministically before recording:
  `DISPLAY=:0 wmctrl -i -r <winid> -e 0,480,60,640,960` and minimise Chrome
  (`DISPLAY=:0 xdotool windowminimize <chrome-winid>`) so the game is unobstructed.
- On a 1600x1200 display the screenshot tool works in a 1024x768 space: screen ≈ `(307 + 0.64*logical_x,
  58 + 0.64*logical_y)`. Use `zoom` on the window rectangle to read the UI text reliably.

## Career save / test state

- Save file: `~/.local/share/godot/app_userdata/Dice Soccer/career.json`.
- Delete it before a run for a deterministic fresh career (cash 0, Season 0 League, fixture 1). The in-game
  **NEW CAREER** button does the same thing.
- Useful fixed values from the extracted data for assertions: Season 0 League has 3 fixtures
  (The Neighbours, Var City, DS High), each paying 1000 / 500 / 200 for win / draw / loss; stars needed =
  fixtures × 2 = 6; a win is 3 stars, a draw 1. Initial roster ids `[1,2,3,4,7,6,8]`; the cheapest buyable
  player is Randy Johnson (MID, $100), which makes him the natural subject for shop + squad-assignment tests.
- Line-up slots are `[GK, DEF, DEF, DEF, FWD, FWD, FWD]`; slot 0 requires a GK, MIDs fit any outfield slot. In
  SQUAD, only bench players expose an `IN` button, so a squad swap test needs a bought/benched player first.

## Gotchas seen while testing (may or may not be fixed)

- The match screen's ABANDON and PLAY ON textures can overlap (TextureButton ignores `size`); if PLAY ON looks
  half-hidden, click its visible right-hand edge.
- Keyboard `Space`/`Enter` on the match screen may fire the encounter twice (both the focused button and
  `_unhandled_input` react to `ui_accept`). Prefer mouse clicks when you need an exact one-minute step, and
  check the clock delta rather than assuming one press = one encounter.
- Dice are blank until the first encounter is played — that is not a missing-texture bug.

## Devin Secrets Needed

None. Everything runs locally from the repo and the bundled `Payload/` dump.
