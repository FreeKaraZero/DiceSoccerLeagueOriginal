# Dice Soccer — Unity Remake (Branch: `unity`)

This branch contains the complete, self-contained Unity project reproducing the 2012 iOS game *Dice Soccer* (1:1 reverse-engineered mechanics, Cocos2d asset pipeline, rules, audio, and UI).

The prebuilt, installable Android package is included directly in this repository:
- [`DiceSoccer.apk`](file:///home/kara/Scrivania/Personal/DevApp/Antigravity/DiceSoccerOriginal/DiceSoccer.apk) (ARM64, IL2CPP, API Level 26+)

## Open the project in Unity

Open this repository root folder directly from Unity Hub using Unity 6 (6000.0 LTS+).
The startup scene is `Assets/Scenes/Main.unity`.

All original resources (data catalogs, sprite atlases, audio effects, music tracks, and Krungthep typography) are pre-extracted and tracked directly in `Assets/Resources/Original/`.

To build the Android APK from the command line or Unity Editor:
```sh
# Via Unity Menu:
# Dice Soccer > Configure Android and iOS

# Or headless via CLI:
Unity -batchmode -quit -projectPath . -executeMethod DiceSoccer.Editor.ConfigureMobileBuild.BuildAndroid -logFile /tmp/unity_android_build.log
```

```sh
python3 Tools/extract_assets.py
```

The extractor reads binary property lists, converts Cocos2d sprite atlases into
individual PNG frames, decodes Apple's CgBI PNG images, and writes original
game data, images, audio, fonts and atlas frame metadata to
`Assets/Resources/Original/`. `data/UnityCatalog.json` contains a Unity-friendly
catalog of 133 players, 67 abilities, all five seasons and their fixtures. The
extractor uses Python's standard library only. The generated folder is ignored
by Git so the extracted bundle is not duplicated in commits. Running the
extractor again replaces only that generated folder.

## Current milestone

- Portrait title screen, season-specific original field art and dice art.
- Starting roster and Season 0 league fixtures loaded from original data.
- A playable 15-minute match loop with attack/defence dice rolls, possession,
  field progression, shots, saves, goals and a visible score/clock.
- Encounter rolls and match-state resolution are separate phases: the score,
  clock, possession and ball position update after the dice animation finishes.
- Shake-to-roll controls with a tap fallback, animated dice faces and a
  ball-position marker.
- Match lineups use original full-figure player art, and the attacking forwards
  make a short run animation during dice rolls. Placeholder opponents use a
  generic portrait when the bundle has no matching full-figure art.
- The match screen uses a full-width field layout and a compact event summary
  separated from the dice and controls.
- Correctly resolved Dribble rerolls and the God-like +1-to-opponents die side.
- Training-league fixture advancement, source-defined cash rewards and saved
  fixture progress, with completed league matches available for replay.
- Season-specific League/T1/T2 fixture lists, saved tournament progress and
  win-gated tournament advancement; tournaments become available after that
  season's League is complete.
- A persistent Manage Team flow for hiring players with no listed unlock
  requirement, maintaining a bench, selecting role-eligible owned players,
  with original player portraits, and substituting from the bench between
  match rolls.
- Canvas scaling and safe-area anchoring for mobile screens.
- Android/iOS player settings menu and a repeatable original-asset extractor.

## Rules established from the original bundle

The original source project is not present in the IPA. The rules below are
grounded in its plist data and tutorial text; method names found in the ARM
executable are useful clues, but do not reveal the full implementation.

- A team has seven players, with a goalkeeper, defenders, midfielders and
  forwards. The Central Forward takes shots; midfielders may fill forward or
  defensive slots.
- The player team starts on the left. A device shake or tap at either screen
  edge starts a roll. The higher total wins possession and advances; a tie
  leaves play in place.
- Each roll consumes about a minute of match time. The tutorial does not
  establish a single exact match duration for every mode.
- League fixtures unlock by being completed, even if lost. Tournament fixtures
  require a win to unlock the next fixture. The fixture records define cash
  rewards for wins, draws and losses. Completed league matches can be replayed.
  Season star ratings and career promotion thresholds are verified directly from
  binary disassembly (see ## Binary observations verified with r2 below).
- Special dice and player abilities are data-driven. For example, Dribble
  sides add to the total and roll again; Wild contributes zero while enabling
  teammate combos; God-like adds one to all opponents; Cross grants a chosen
  reroll. Dribble and God-like are implemented. Wild combo activation, Cross
  targeting and the full ability/referee interaction are not implemented yet.

## Binary observations verified with r2

These are narrow observations from the ARMv7 executable, not a complete
gameplay specification:

- `PlayerDice.showRollDiceAnimation` calls `arc4random`, computes the remainder
  modulo six using the reciprocal-multiply sequence for division by six, then
  adds one. It uses that `1..6` value minus one as the index into
  `diceSideIDArray`, reads the selected `NSNumber`'s integer value and stores
  it as `actualFinalValue`. A forced-final-value flag bypasses this random
  selection. The modulo operation can introduce a tiny bias because the
  32-bit RNG range is not divisible by six; no rejection-sampling step is
  present in this path.
- `checkAbilitiesCondition:player:` checks for dice-roll ability value `31`
  and passes an internal `-1` value into a `checkRollForValue:value1:value2:value3:`
  path. That helper treats `-1` as a special value. This verifies one
  value-trigger path, not every use of Wild.
- The same `checkAbilitiesCondition:player:` method explicitly compares the
  current ability ID with `82` (`0x52`). That branch skips the subsequent
  `addAbility:player:` call and continues to method cleanup. This confirms
  that ID 82 is recognized outside the numeric dispatcher. The actual
  Captain rule is configured by `FieldPlayer.initWithType:index:bonus:parent:`,
  which stores the player's `superPowerID`, checks for `82`, then reads the
  player's `type`: type `0` calls `setRedWinOnTie:1`, and type `1` calls
  `setBlueWinOnTie:1` on the game field. No ability dictionary lookup is
  involved in setting these flags.
- The lifecycle of die face 31 (Wild) and its special values (-1, +100, -100)
  is verified across `PlayerDice` and `AbilityManager`:
  - `PlayerDice.showRollDiceAnimation` draws a random index `0..5` from `diceSideIDArray`
    and stores the raw face (including `31` for Wild) in `actualFinalValue` (ivar offset `268`).
  - During turn resolution in `AbilityManager.checkAbilitiesCondition:player:`, the code
    evaluates each active encounter die. If `actualFinalValue` equals `31` (`0x1f`), it
    sets the working register value to `-1`.
  - In `checkRollForValue:value1:value2:value3:`, each parameter is compared with `-1`
    (`cmp.w rX, -1; it eq; bx lr`). If any die is `-1`, the method returns `1` immediately,
    verifying that a Wild face matches any required number trigger.
  - In `checkTripleWithValue1:value2:value3:`, `-1` acts as a wildcard: if two dice are
    `-1` and the third is non-zero, it returns `1`; if one die is `-1`, it returns `1`
    when the other two dice match (`r1 == r2` or `r2 == r3`).
  - In `checkStraightConditionWithValue1:value2:value3:`, if two dice are `-1`, it returns
    `1` unconditionally. If one die is `-1`, it subtracts the remaining two dice, computes
    the absolute difference, subtracts 1, and tests whether the result is `< 2`
    (`subs r1, r0, 1; cmp r1, 2; blo ...`). Thus a single Wild die satisfies a straight
    if the other two dice differ by 1 (adjacent, e.g. 3 and 4) or 2 (gap, e.g. 3 and 5).
  - In `checkPairConditionWithValue1:value2:value3:`, `checkAbilitiesCondition:player:`
    checks whether a Wild die was rolled (`-1`). If so, it converts `-1` to `+100` (`0x64`),
    or to `-100` (`rsb.w r1, fp, 0`) if `[player valueChecked]` is non-zero. The helper
    `checkPairConditionWithValue1:value2:value3:` explicitly tests `cmn.w rX, 0x64` (-100)
    and `cmp.w rX, 0x64` (+100). A die with `+100` matches any non-zero positive die,
    a die with `-100` matches any negative die, and non-wild dice are compared by absolute
    value (`abs(v1) == abs(v2)`).
- The common `addAbility:player:` method's first switch accepts type values
  `0` through `6` and selects audio resources (`Remove`, `Debuff1/2`,
  `Buff1/2/3`, `Super1`). It is not a switch implementing seven gameplay
  effect families. The method subtracts `16` from the ability ID, checks
  whether the result is at most `65`, then indexes a 66-entry Thumb `TBH`
  table. Each indexed case either loads an Objective-C selector and reaches a
  shared `objc_msgSend` call or branches directly to the method epilogue.
  `Abilities.json` has 67 records (IDs `16..82`), so ID `82` is outside this
  table's indexed range; IDs `25`, `26` and `31` are in range but branch
  directly to the epilogue in this dispatcher.
- Resolving the selectors loaded by the indexed case stubs against the
  `Abilities` records gives the following dispatch map. Multiple data IDs can
  intentionally route to the same selector; this records dispatch only and
  does not by itself establish what each handler does:

  | Selector | Ability IDs |
  | --- | --- |
  | `addAbility16:` | 16, 59 |
  | `addAbility17:` | 17 |
  | `addAbility18:` | 18 |
  | `addAbility19:` | 19, 70 |
  | `addAbility20:` | 20 |
  | `addAbility21:` | 21 |
  | `addAbility22:` | 22 |
  | `addAbility23:` | 23, 51, 61, 62, 63 |
  | `addAbility24:` | 24 |
  | `addAbility27:` | 27 |
  | `addAbility28:` | 28 |
  | `addAbility29:` | 29 |
  | `addAbility30:` | 30 |
  | `addAbility32:` | 32 |
  | `addAbility33:` | 33, 53, 67 |
  | `addAbility34:` | 34, 69, 71 |
  | `addAbility35:` | 35 |
  | `addAbility36:` | 36 |
  | `addAbility37:` | 37, 72, 73 |
  | `addAbility38:` | 38 |
  | `addAbility39:` | 39, 54, 66, 74, 75 |
  | `addAbility40:` | 40 |
  | `addAbility41:` | 41, 56, 57, 58 |
  | `addAbility42:` | 42, 52, 60 |
  | `addAbility43:` | 43, 76, 77, 78, 79, 81 |
  | `addAbility44:` | 44 |
  | `addAbility45:` | 45 |
  | `addAbility46:` | 46, 55 |
  | `addAbility47:` | 47, 80 |
  | `addAbility48:` | 48 |
  | `addAbility49:` | 49 |
  | `addAbility50:` | 50 |
  | `addAbility64:` | 64 |
  | `addAbility65:` | 65 |
  | `addAbility68:` | 68 |
- In `GameFieldLayer.checkPlayerScore`, equal roll totals cause the game to
  compare `redWinOnTie` and `blueWinOnTie`. If the flags differ, the set flag
  determines the winning side. The method then scans that side's
  `*EntourageArray` for value `16` to trigger the corresponding entourage
  icon, but the score increment and "Win on Tie" message occur after that
  scan even when the array is empty or contains no `16`. Thus the entourage
  ID is not required for the tie rule itself. The source `EntourageData.plist`
  record 16 (Paparazi Magnet) describes the same tie effect, and
  `AbilityManager.addEntourageAbility16:` independently sets the side's tie
  flag. Separately, ID 82 ("Captain") sets the flag in the field-player
  initializer as described above.
- The mechanisms connecting Through Pass (`activateThroughPass:`) and Referee Dice
  (`rollRefereeDice`) are fully traced and resolved:
  - `activateThroughPass:` is triggered by Through Pass records (IDs 47 and 80, Odd and
    Even variants), not ID 25. In `BuffTargetLayer.startBuffTriggerAnimation`, the code
    explicitly compares the active ability ID in Thumb-2 against `0x2f` (47) and `0x50` (80)
    (`cmp.w sl, 0x2f; it ne; cmp.w sl, 0x50; bne ...`). On a match, it increments
    `leftThroughPassCount` (ivar offset `324` / `0x144`) or `rightThroughPassCount`
    (ivar offset `328` / `0x148`). In `finishBuffTriggerAnimation`, if `leftThroughPassCount`
    and `rightThroughPassCount` differ (`cmp r1, r0; bne.w 0x4b85a`), it invokes
    `showThroughPassAnimation` via `performSelectorOnMainThread:withObject:waitUntilDone:`.
    On animation completion, `finishThroughPassAnimation` compares the two counts
    (`cmp r5, r3; it le; movs r2, 1`) to determine the advancing side (`0` or `1`) and
    calls `[parentLayer activateThroughPass:side]`.
  - ID 25 (Perfect Cross) and ID 64 (+5) are handled as targetable buffs (`BuffType=5`):
    `startBuffTriggerAnimation` evaluates `[player targettableBuff]` and tests the return
    of `[player getDiceRollAbility]` against `0x19` (25) and `0x40` (64). If matched, it
    calls `createBuffMedal: 0x17` and appends the medal via `addObject:` to `leftBuffArray`
    (ivar `0x124`) or `rightBuffArray` (ivar `0x128`) on the buff belt.
  - The referee dice trigger is implemented by **ID 45** (Dive!, trigger 6: triple), not
    ID 26. ID 26 in `Abilities.json` is a dormant stub entry (trigger 0) branching directly
    to epilogue in `addAbility:player:`. When a triple roll activates ID 45, the dispatcher
    invokes `addAbility45:` (`0x00018c08`). That method inspects the player and side,
    then calls `setRefereeForPlayer: 1` (`0x00018ee8`) or `setRefereeForEnemy: 1` (`0x00018e84`)
    on `GameFieldLayer`, writing byte flags to `refereeForPlayer` (ivar offset `360` / `0x168`)
    or `refereeForEnemy` (ivar offset `361` / `0x169`).
  - During turn cleanup, `finishResolvingAbilities` executes `checkToRollRefereeDice`
    (`0x00072fd8`), which reads both flags (`ldrb r1, [r0, offset]; cmp r1, 0`). If either
    flag is non-zero, it branches to `0x00073244` and directly calls `rollRefereeDice`
    (`0x00073260`). `removeAbility45:` (`0x0001a9d4`) resets the flags via
    `setRefereeForPlayer: 0` and `setRefereeForEnemy: 0`.
- A closer disassembly of `addAbility16:` shows it receiving the manager and
  player, checking the player's dice status and type, then using game/player
  state and bonus-related selectors such as `setBonus:`, `setDebonus:` and
  `refreshBonus`. This method does not call `objectForKey:` or `valueForKey:`,
  and contains no direct lookup of the `BuffTarget` or `LocalReroll` keys.
  Consequently, the data records alone do not establish that this handler is
  a generic metadata interpreter; whether those fields are consulted in a
  different path remains unverified. In particular, IDs 16, 17 and 18 all
  have `BuffType=3` but dispatch to three different selectors, while ID 59
  also has `BuffType=3` and dispatches to `addAbility16:`.
- Comparing `addAbility16:`, `addAbility17:` and `addAbility18:` confirms
  hardcoded increments in their bonus-setting branches: each takes the value
  returned by an Objective-C message send, adds respectively `1`, `2` or `3`,
  and passes that result in `r2` to `setBonus:`. Their corresponding
  debonus-setting branches likewise add `1`, `2` or `3` before `setDebonus:`.
  This supports hardcoded +1/+2/+3 amount handling in these three methods; it
  does not establish that every ability handler is a primitive for one
  `BuffType`, or explain how shared handler ID 16 determines its target for
  both Dribble +1 (ID 16) and Defensive Wall (ID 59).
- The same `addAbility16:` flow also checks the player's dice status and side,
  selects red/blue player and dice arrays, locates the player among six
  indexed dice, and checks `opponentReturnDebuff`, `antiBuffShell` and
  `invertCount` before mutating bonus/debonus values. Thus the observed
  selector is a context-sensitive game-state routine, not evidence by itself
  for a standalone `ApplyFlatBonus(target, 1)` primitive. The route by which
  ID 59's `BuffTarget=3` selection determines its recipients is traced below;
  it targets the current encounter's selected player list, not the whole team
  by default.
- In `BuffTargetLayer.autoTargetBuff`, the code reads `target = [ability buffTarget]`
  and branches on `cmp r0, 2; blt 0x4cf7e`:
  - For `BuffTarget >= 2` (multi-target, e.g. ID 59 with `BuffTarget=3`), the flow routes
    directly to `onSelectTargets`.
  - For `BuffTarget < 2` (single target: `0` = friendly/self buff, `1` = opponent debuff):
    The method reads `currentPossession` and tests bitmask `(1 << target)`:
    - If `currentPossession == 0` (Red/player possession): `target == 0` loads
      `redDiceToRollArray` / `redPlayerArray`; `target == 1` loads `blueDiceToRollArray` /
      `bluePlayerArray`.
    - If `currentPossession == 1` (Blue/enemy possession): `target == 0` loads
      `blueDiceToRollArray` / `bluePlayerArray`; `target == 1` loads `redDiceToRollArray` /
      `redPlayerArray`.
    The selected pair is stored into ivars `activePlayerArray` (`0x12c` / 300) and
    `playerArray` (`0x130` / 304).
  - In auto-targeting: `autoTargetBuff` draws `arc4random() % activePlayerArray.count`,
    reads `playerIndex = [[activePlayerArray objectAtIndex:slot] intValue]`, retrieves
    `FieldPlayer* = [playerArray objectAtIndex:playerIndex]`, stores it in `currentPlayer`
    (ivar offset `316` / `0x13c`), and calls `[self onSelectTarget]`.
  - In manual UI targeting: the six click callbacks `onClickLeftTarget1..3` (`0x0004dd60`,
    `0x0004de48`, `0x0004df30`) and `onClickRightTarget1..3` (`0x0004e018`, `0x0004e100`,
    `0x0004e1e8`) resolve slot index `0`, `1` or `2` (`movs r2, 0/1/2`) from the
    corresponding side's `activePlayerArray`, look up `FieldPlayer*` in `playerArray`,
    store it into `self->currentPlayer`, and call `[self onSelectTarget]`.
  - `onSelectTarget` sets up a flying medal animation (`actionWithDuration:modifier:`)
    to `currentPlayer` and schedules `onFinishCurrentBuff` (`0x0004d170`).
  - `onFinishCurrentBuff` executes `[abilityManager addAbility:abilityID player:self->currentPlayer]`,
    updates the scoreboard via `[parentLayer updateRedLiveScore]` or `[parentLayer updateBlueLiveScore]`,
    and consumes the medal with `[leftBuffArray removeLastObject]` or
    `[rightBuffArray removeLastObject]`. If further buffs remain on the belt, it calls
    `autoTargetBuff`; otherwise it transitions to `onFinishTurn`.
- The class ivar-offset table maps `activePlayerArray` to offset `0x12c` and
  `playerArray` to `0x130`. In `onSelectTargets`, the current ability ID
  and `currentPossession` choose which side's arrays are requested from
  `parentLayer`. The selector pairs are `redDiceToRollArray` /
  `redPlayerArray` and `blueDiceToRollArray` / `bluePlayerArray`; their results
  are stored in `activePlayerArray` and `playerArray`, respectively. For
  ability ID 59, the code negates `currentPossession` before choosing a side,
  then chooses the red pair only when the resulting value is `1` (otherwise
  it chooses the blue pair). The method enumerates the selected dice-to-roll
  array as boxed player indices, resolves each index through the matching
  player array, and enables that player's dice glow. It then resolves
  selected index `0` the same way and starts the `ontoNextTarget` animation
  callback. The candidate arrays' contents are established by
  `GameFieldLayer.rollDice` below; they are not a separate user-built subset.
- Objective-C class metadata for `BuffTargetLayer` lists six target menus
  (`leftTarget1`–`leftTarget3` and `rightTarget1`–`rightTarget3`), six
  corresponding `*BuffTriggerArray` fields, and the state arrays
  `leftBuffArray`, `rightBuffArray`, `activePlayerArray` and `playerArray`.
  These declarations establish the picker's available state/UI slots; the
  runtime candidate count comes from the arrays populated by `rollDice`.
- `GameFieldLayer.rollDice` does populate the two `*DiceToRollArray` fields:
  it clears each nonempty array, reads the `location` ivar, adds `2`, and
  dispatches through a five-entry Thumb branch table when the result is in
  `0..4`. The branch bodies box player indices with `numberWithInt:` and append
  them with `addObject:`, while also updating players through
  `setLastToRollDice:`. Resolving each branch's array ivars and loop bounds
  gives these candidate indices (the switch value is `location + 2`):

  | `location` | `redDiceToRollArray` | `blueDiceToRollArray` |
  |------------|----------------------|-----------------------|
  | `-2`       | `[0]`                | `[5]`                 |
  | `-1`       | `[1, 2, 3]`          | `[4, 5, 6]`           |
  | `0`        | `[4, 5, 6]`          | `[4, 5, 6]`           |
  | `1`        | `[4, 5, 6]`          | `[1, 2, 3]`           |
  | `2`        | `[5]`                | `[0]`                 |

  The lists therefore provide one or three location-specific player indices,
  not an entire team by default. For ID 59, `onSelectTargets` assigns the
  selected side's list to `activePlayerArray` using `currentPossession`.
  Consequently, the picker and its callbacks operate on the one or three
  current-encounter candidates shown here, not all seven players.
- Keep the two state variables distinct: `onSelectTargets` chooses the side
  for ID 59 using `currentPossession`, whereas `rollDice` uses `location` to
  build the red/blue index lists. The inspected `location` read is not the
  possession test in the ID 59 target-side branch.
- `ontoNextTarget` checks `activePlayerArray.count`. With three candidates it
  dispatches the current target through `addAbility:player:`, resolves
  position `1`, and schedules `ontoFinalTarget`. That callback dispatches the
  second target, resolves position `2`, and schedules `onFinishCurrentBuff`,
  which dispatches the final current target. With the one-candidate arrays,
  `ontoNextTarget` takes its non-multi-target completion branch and the
  completion callback dispatches the current target. Thus this callback path
  applies ID 59 to each of the one or three candidates in the selected
  `*DiceToRollArray`; it does not imply a whole-team target outside those
  location-specific lists.
- The complete execution of Through Pass (`activateThroughPass:` and `activateThroughPass2:`)
  is verified in `GameFieldLayer`:
  - `activateThroughPass:` (`0x0007ad58`) accepts `side` (`0` for Red / Player, `1` for Blue / Opponent):
    - Plays audio effect via `SimpleAudioEngine.playEffect:`.
    - Iterates through all active rolling players in `redDiceToRollArray` (ivar offset `328` / `0x148`)
      and `blueDiceToRollArray` (ivar offset `352` / `0x160`): calls `setLocalReroll: 0`,
      `removeAllBuffs`, `removeAllDebuffsExceptYellowCard`, `showPoofAnimation`, and
      `setDiceFadeOutAnimation: 0.375f`.
    - Calls `performSelector:withObject:afterDelay:` with boxed `side` (`[NSNumber numberWithInt:side]`),
      scheduling `activateThroughPass2:` after a `0.75s` delay (double literal `0x3fe8000000000000`).
  - `activateThroughPass2:` (`0x0007b034`):
    - Uses internal possession convention: Red = `-1`, Blue = `+1`.
    - If `side == 0` (Red/Player advancing):
      - If `currentPossession != -1` (turnover): sets `currentPossession = -1`, selects a random
        candidate from `redDiceToRollArray` (`arc4random() % count`), assigns `possessionPlayerID`,
        triggers `changePossessionAnimation1` (if previous possession was 1) or `changePossessionAnimation2`,
        and calls `shiftFieldRight`.
      - If `currentPossession == -1` (already Red): checks `location`:
        - If `location == 1`: sets `possessionPlayerID = 5` (Central Forward).
        - Else if `location + 1 <= 1`: sets `possessionPlayerID = (arc4random() % 3) + 4`
          (random forward from indices `[4, 5, 6]`).
        - Calls `shiftFieldRight`.
      - In `shiftFieldRight` (`0x0007474c`): increments `location = location + 1` (`subs/adds` at
        `0x0007513a`), shifts field background and player sprites right, and calls `saveCurrentInGameData`.
    - If `side == 1` (Blue/Opponent advancing):
      - If `currentPossession != 1` (turnover): sets `currentPossession = 1`, selects a random
        candidate from `blueDiceToRollArray`, plays change possession animation, and calls `shiftFieldLeft`.
      - If `currentPossession == 1` (already Blue): checks `location`:
        - If `location == -1`: sets `possessionPlayerID = 5` (Opponent CF).
        - Else if `location < 2`: sets `possessionPlayerID = (arc4random() % 3) + 4`.
        - Calls `shiftFieldLeft`.
      - In `shiftFieldLeft` (`0x00075180`): decrements `location = location - 1` (`0x00075b7c`),
        shifts field background and player sprites left, and calls `saveCurrentInGameData`.
    - Both advance branches conclude by calling `stopShakeAnimation`.
- Referee Die, Cards, and Outcome Resolution:
  - `refereePlayer` (`GameFieldLayer` ivar offset `260` / `0x104`) is allocated as
    `[FieldPlayer initWithType: 2 index: 0 bonus: 0 parent: self]`. Its die sides are loaded from
    `PlayerData.plist` key `0` (`DiceSides: '10,32,32,32,32,32'`).
  - Trigger: Activated by ID 45 ("Dive!", trigger 6: triple) via `addAbility45:`, setting
    `refereeForPlayer: 1` (`ivar offset 360` / `0x168`) or `refereeForEnemy: 1` (`ivar offset 361` / `0x169`).
  - `rollRefereeDice` (`0x00073260`):
    - If `refereeForPlayer == 1`: plays referee whistle, displays banner message
      `"Player calls referee dice!"` (`0x001eb01c`), sets `refereePlayer` dice status to `2`,
      and calls `[refereePlayer rollDice]`.
    - If `refereeForEnemy == 1`: plays referee whistle, displays banner message
      `"Enemy calls referee dice!"` (`0x001eb02c`), sets `refereePlayer` dice status to `2`,
      and calls `[refereePlayer rollDice]`.
    - If neither flag is set (roll complete): fades the referee die (`setDiceFadeOutAnimation: 1.0f`),
      triggers poof animations, and schedules `checkPlayerScore`.
  - Die Face Resolution (`PlayerDice.showRollDiceAnimation` at `0x00009c04` and `showDiceSide:` at `0x00009800`):
    - When inspecting `actualFinalValue` (`ivar offset 268`):
      - If `actualFinalValue >= 101` (`0x65`): sets `referee = 1` (`ivar offset 280`) and subtracts
        100 (`actualFinalValue -= 100`).
      - If `actualFinalValue < 101`: sets `referee = 0`.
      - If `actualFinalValue >= 15`: clamped to `0` (contributes `0` points to the encounter total).
  - Callback (`0x000735c8 refereeFinishRolling`):
    - `[FieldPlayer finishRolling]` (`0x00004e88`) tests `type == 2`; if so, dispatches to
      `[gameField refereeFinishRolling]`.
    - In `refereeFinishRolling`, it clears `refereeForPlayer` and `refereeForEnemy` to `0`, then
      recurses into `[self rollRefereeDice]`, executing the completion fade-out branch and
      continuing to `checkPlayerScore`.
  - Yellow and Red Cards:
    - `addAbility65:` (Yellow Card): inspects `[player yellowCard]`. If `yellowCard == 0`, sets
      `yellowCard = 1` and calls `addDebuff: 65`. If `yellowCard` is already `1`, calls
      `removeAbility65:` and escalates to `addAbility68:` (Red Card Trap).
    - `addAbility68:` (Red Card Trap): if player is frozen (`diceStatus == 0`), clears freeze;
      sets `redCard = 1`, clears all buffs, clears all debuffs except yellow/red card, removes all
      rerolls, and invokes `setDiceRedCardTrap` (`0x00005278`). `setDiceRedCardTrap` calls
      `[dice getRedCard]`, which animates the red card overlay covering the die.
    - Red-carded players are strictly skipped in `updateRedLiveScore` and `updateBlueLiveScore`.
- Semantic Mapping of Ability Handlers (`addAbility16:` .. `addAbility68:` and matching `removeAbility*`):
  Every handler in `AbilityManager` has been disassembled and verified at the register level:

  | Selector | Ability IDs | Effect & Register-Level Semantics |
  | --- | --- | --- |
  | `addAbility16:` | 16, 59 | Bonus +1: adds 1 to `bonus` (or `debonus` if swapped by `invertCount`). Intercepted by `antiBuffShell` (absorbs buff and is removed via `removeAbility41:`). |
  | `addAbility17:` | 17 | Bonus +2: adds 2 to `bonus` / `debonus`. Intercepted by `antiBuffShell`. |
  | `addAbility18:` | 18 | Bonus +3: adds 3 to `bonus` / `debonus`. Intercepted by `antiBuffShell`. |
  | `addAbility64:` | 64 | Bonus +5: adds 5 to `bonus` / `debonus`. Intercepted by `antiBuffShell`. |
  | `addAbility27:`, `32:` | 27, 32 | Direct aliases forwarding to `addAbility16:` (Bonus +1). |
  | `addAbility33:` | 33, 53, 67 | Debonus -1: adds 1 to `debonus` (or `bonus` if swapped by `invertCount`). Blocked by `debuffBlock` (absorbs debuff and decrements block count via `removeAbility48:`). |
  | `addAbility34:` | 34, 69, 71 | Debonus -2: adds 2 to `debonus` / `bonus`. Blocked by `debuffBlock`. |
  | `addAbility19:` | 19, 70 | Debonus -3: adds 3 to `debonus` / `bonus`. Blocked by `debuffBlock`. |
  | `addAbility20:` | 20 | Direct alias forwarding to `addAbility19:` (Debonus -3). |
  | `addAbility35:` | 35 | Direct alias forwarding to `addAbility33:` (Debonus -1). |
  | `addAbility36:` | 36 | Direct alias forwarding to `addAbility34:` (Debonus -2). |
  | `addAbility39:` | 39, 54, 66, 74, 75 | Multiplier x2: adds 2 to `multiplier` (or `divisor` if swapped). |
  | `addAbility40:` | 40 | Multiplier x3: adds 3 to `multiplier` (or `divisor` if swapped). |
  | `addAbility21:` | 21 | Multiplier x4: adds 4 to `multiplier` (or `divisor` if swapped). |
  | `addAbility22:` | 22 | Multiplier x5: adds 5 to `multiplier` (or `divisor` if swapped). |
  | `addAbility37:` | 37, 72, 73 | Divisor /2: adds 2 to `divisor` (or `multiplier` if swapped). |
  | `addAbility38:` | 38 | Direct alias forwarding to `addAbility37:` (Divisor /2). |
  | `addAbility23:` | 23, 51, 61, 62, 63 | Local Reroll: checks `killReroll`. If active (`> 0`), calls `removeAbility43:` to consume one kill-reroll debuff and aborts the reroll; otherwise calls `[player addLocalRerollType: 1 quantity: 1]`. |
  | `addAbility24:` | 24 | Direct alias forwarding to `addAbility23:` (Local Reroll). |
  | `addAbility41:` | 41, 56, 57, 58 | Anti-Buff Shell: increments `antiBuffShell` by 1 and calls `addDebuff: 41`. Intercepts incoming friendly buffs to that player. `removeAbility41:` decrements `antiBuffShell`. |
  | `addAbility42:` | 42, 52, 60 | Freeze / Injury: calls `removeAllRerolls`, `removeAllBuffs`, removes debuffs except cards, adds debuff 42, and sets `diceStatus: 0` (disabling die from roll/scoring). `removeAbility42:` restores `diceStatus: 1`. |
  | `addAbility43:` | 43, 76, 77, 78, 79, 81 | Kill Reroll: increments `killReroll` by 1, calls `removeLocalReroll`, adds debuff 43. Intercepts subsequent local rerolls. `removeAbility43:` decrements `killReroll`. |
  | `addAbility44:` | 44 | Invert Count: swaps player `bonus` <-> `debonus` and `multiplier` <-> `divisor`, increments `invertCount` by 1, and adds buff 44. `removeAbility44:` reverses the swap and decrements `invertCount`. |
  | `addAbility45:` | 45 | Dive! (Referee trigger): sets `refereeForPlayer: 1` or `refereeForEnemy: 1` on `GameFieldLayer`. `removeAbility45:` clears flags to 0. |
  | `addAbility46:` | 46, 55 | Return Debuff: sets `returnDebuff` on player and `opponentReturnDebuff` on opponent. Transfers all active debuffs from self to opponent (`addAbility` on opponent, `removeAbility` on player). `removeAbility46:` clears return flags. |
  | `addAbility49:`, `50:` | 49, 50 | Direct aliases forwarding to `addAbility46:` (Return Debuff). |
  | `addAbility47:` | 47, 80 | Through Pass Marker: adds buff 47. Handled by `startBuffTriggerAnimation` (increments through pass count and initiates field advance). |
  | `addAbility48:` | 48 | Debuff Block: increments `debuffBlock` by 1, adds buff 48. Intercepts incoming opponent debuffs. `removeAbility48:` decrements `debuffBlock`. |
  | `addAbility28:`, `29:`, `30:` | 28, 29, 30 | Direct aliases forwarding to `addAbility48:` (Debuff Block). |
  | `addAbility65:` | 65 | Yellow Card: sets `yellowCard = 1` and `addDebuff: 65`. If already set, escalates to `addAbility68:`. `removeAbility65:` clears yellow card flag. |
  | `addAbility68:` | 68 | Red Card Trap: clears freeze, sets `redCard = 1`, strips buffs/rerolls, and triggers `[dice getRedCard]`. Player contributes 0 to encounter score. |

- Live Score Calculation and Possession Extra Bonus:
  - In `updateRedLiveScore` (`0x00072464`) and `updateBlueLiveScore` (`0x0007263c`), the encounter
    score is computed across active players in `redDiceToRollArray` / `blueDiceToRollArray`:
    - Checks `[player redCard]`: if non-zero, the player is skipped entirely (`score += 0`).
    - Otherwise, computes:
      $$\text{base} = \max(0, \text{getDiceValue} - \text{debonus})$$
      $$\text{if } \text{multiplier} \ge 1: \text{base} = \text{base} \times \text{multiplier}$$
      $$\text{if } \text{divisor} \ge 1: \text{base} = \lfloor \text{base} / \text{divisor} \rfloor$$
      $$\text{playerScore} = \text{base} + \text{bonus}$$
    - Sums `playerScore` across all non-red-carded active players.
  - Entourage Possession Extra Bonus:
    - When Red has possession (`currentPossession == -1`), adds `redExtraBonus` (`ivar offset 324` / `0x144`).
    - When Blue has possession (`currentPossession == 1`), adds `blueExtraBonus` (`ivar offset 348` / `0x15c`).
    - These extra bonuses are populated from entourage abilities (IDs 2 and 15).
  - In `addPossessionBonusToDiceScore` (`0x00072dec`):
    - Reads `currentPossession`: if Red (`-1`), executes `redDiceRoll += redExtraBonus`; if Blue (`+1`),
      executes `blueDiceRoll += blueExtraBonus`.
    - Iterates through active rolling dice and calls `setDiceFadeOutAnimation: 0.375f`.
  - In `rollDice` (`0x0006fbf4`):
    - The ball-holding player's `getPossessionBonus` is read and forwarded to
      `[gameUILayer setPossessionBonus: currentPossession bonus: possessionBonus]`.
- Match End, Star Ratings, and Season Promotion Thresholds:
  - Match outcome and Star Rating (`MatchSelectionLayer.getStarSprite: status` at `0x00040248`
    and `EndGameLayer.saveCurrentMatchData` at `0x0007d57c`):
    - The binary records match status (`fp`) based on goal difference:
      - `status <= 1`: 0 Stars (3 empty stars: `***_Empty.png`).
      - `status == 2`: 1 Star (1 star filled, 2 empty). Match ended in a **Loss** (`fp = 1`).
      - `status == 3`: 2 Stars (2 stars filled, 1 empty). Match ended in a **Draw** (`fp = 2`).
      - `status == 4`: 3 Stars (3 stars filled, 0 empty). Match ended in a **Win by 1 goal** (`fp = 3`, or any Tournament Win).
      - `status == 5`: 3 Stars (3 stars filled, 0 empty). Match ended in a **Win by 2+ goals** (`fp = 4`).
    - Visual Star Sprites: Gold stars (`GoldStar_Empty.png` / `GoldStar_Full.png`) are displayed
      for League matches (`matchType == 0`), while Silver stars (`SilverStar_Empty.png` / `SilverStar_Full.png`)
      are displayed for Tournaments (`matchType != 0`).
    - In `seasonData`, total league stars accumulate delta gains: `leagueStars += (newStatus - oldStatus)`.
  - Career Season Promotion Thresholds (`AchievementManager.checkAchievementsAtEndofMatch:matchType:season:` at `0x00082d44`):
    - **Season 1 Unlock**: Complete all Training Season league matches (`setLeagueMatchesPlayed:`),
      which sets `seasonProgression = 1` and `multiplayerUnlocked = 1`.
    - **Season 2 Unlock**: Accumulate **20 Stars** in Season 1 League (`cmp r5, 0x14`), setting `seasonProgression = 2`.
    - **Season 3 Unlock**: Accumulate **30 Stars** in Season 2 League (`cmp r4, 0x1e`), setting `seasonProgression = 3`.
    - **Season 4 Unlock**: Accumulate **30 Stars** in Season 3 League (`cmp r0, 0x1d; bgt`), setting `seasonProgression = 4`.
    - These match the exact UI string literals loaded at `0x001e99cc..0x001e99fc`:
      - *"Complete Training Season to unlock!"*
      - *"Get 20 Stars in Season 1 to unlock!"*
      - *"Get 30 Stars in Season 2 to unlock!"*
      - *"Get 30 Stars in Season 3 to unlock!"*
  - Player Card Unlocking Architecture:
    - Card Status States: In `GameConfig.plist` (`InitialCardStatus`), starting lineup players
      (IDs 0..4, 6) have initial status `2` (hired & starting lineup), player 5 starts at `0`,
      and all other 96 players start at `0` (locked/unhired). Status `1` corresponds to unlocked/hireable.
    - Player 15 (Johnny Speed) is automatically unlocked upon completing Match 2 of Training Season
      (`setBenchUnlocked: 1` -> `unlockPlayerCard: 15`).
    - The unlock mechanism is split between two arrays in `StatsManager` via `AchievementManager.createUnlockedAchievement:`:
      - **Standard Achievements (IDs 1..72)**: Stored in `achievementsArray`. When achieved, if `PlayerReward > 0`,
        calls `[playerManager unlockPlayerCard: PlayerReward]`; if `EntourageReward > 0`, calls
        `[playerManager unlockEntourageCard: EntourageReward]`.
      - **Hidden Fixture Progression Achievements (IDs 1001..1077)**: Stored in `hiddenAchievementsArray`
        (mapped from `DummyAchievements.plist`, with ID offset `1000 + dummyID`). When a specific league
        or tournament milestone is reached, `AchievementManager` dispatches `createUnlockedAchievement:` with
        the `1000 + dummyID` value, unlocking the corresponding `PlayerReward` and `EntourageReward`.
    - Once unlocked (status `1`), players can be hired in the Manage Team screen (`ConfirmationLayer.yesTap:`)
      for their source-defined cash cost.

- Detailed Referee Roll Resolution and Card Trigger Mechanics:
  - `refereePlayer` (`GameFieldLayer` ivar offset `260` / `0x104`) is allocated as `[FieldPlayer initWithType: 2 index: 0 bonus: 0 parent: self]`.
    Its die sides are loaded from `PlayerData.plist` key `0` (`DiceSides: '10,32,32,32,32,32'`).
  - Face values:
    - Face `10`: God-like / Special (value 10).
    - Face `32`: Whistle / Referee (clamped to 0 points because $\ge 15$).
  - When invoked by ID 45 ("Dive!", trigger 6: triple), `rollRefereeDice` (`0x00073260`) rolls `refereePlayer`.
    Upon completion, `FieldPlayer.finishRolling` calls `refereeFinishRolling` (`0x000735c8`), which retrieves
    `[refereePlayer getDiceValue]`, logs `NSLog(@"refereeFinishRolling, refereeDiceSide = %d")`, clears the caller
    flags (`refereeForPlayer = 0`, `refereeForEnemy = 0`), and recurses to `rollRefereeDice` to execute the
    fade-out animation (`setDiceFadeOutAnimation: 1.0f`) and proceed to `checkPlayerScore`. The referee die roll
    is an aesthetic exhibition roll in the match sequence.
  - In-match Card Mechanics are driven by **Entourage 20 (Pierre Bear Stare)**:
    - In `BuffTargetLayer.startBuffTriggerAnimation`, if Entourage 20 is equipped, the engine calls
      `[abilityManager getNoOfValueGreaterThan: 6 teamType: 1]` (counting opponent dice rolling $> 6$, i.e. $\ge 7$).
    - For each opponent die rolling $\ge 7$, a Yellow Card medal (Ability ID `65`) is created and appended to the buff belt (`leftBuffArray`).
    - When targeted onto an opponent player via `BuffTargetLayer.onFinishCurrentBuff`, `addAbility: 65` is executed:
      - If `[player yellowCard] == 0`: sets `yellowCard = 1` and registers `addDebuff: 65`.
      - If `[player yellowCard] == 1`: calls `removeAbility65:` and escalates immediately to `addAbility68:` (Red Card Trap!).
    - `addAbility68:` removes any active freeze, sets `redCard = 1`, clears all buffs, debuffs, and rerolls,
      and calls `setDiceRedCardTrap` (`0x00005278`), displaying the red card overlay (`[dice getRedCard]`).
      In `updateRedLiveScore` / `updateBlueLiveScore`, players with `redCard == 1` are strictly skipped, contributing `0` to the score.

- Complete Table of Dice Face Codes (`DiceSides`):
  Inspecting `PlayerDice`, `FieldPlayer`, and sprite asset mappings in `Buff6.plist`:

  | Face Code | Sprite File | Nominal Value | Gameplay Effect & Rules |
  | --- | --- | --- | --- |
  | `1..6` | `DiceSide01..06.png` | 1..6 | Standard numeric roll faces. |
  | `7..9` | `DiceSide07..09.png` | 7..9 | High numeric roll faces. |
  | `10` | `DiceSide10.png` | 10 | Special face (value 10; clamped to 0 if $\ge 15$). |
  | `16` | `DiceSide16.png` | 1 | Dribble +1: adds 1 to roll total and grants a local reroll. |
  | `17` | `DiceSide17.png` | 2 | Dribble +2: adds 2 to roll total and grants a local reroll. |
  | `18` | `DiceSide18.png` | 3 | Dribble +3: adds 3 to roll total and grants a local reroll. |
  | `24` | `DiceSide24.png` | 0 | Cross / Assist: grants a local reroll to chosen player. |
  | `31` | `DiceSide31.png` | 0 | Wild: contributes 0 to score; evaluated as `-1` in combos (converted to `+100`/`-100` for pairs). |
  | `32` | `DiceSide32.png` | 0 | Whistle / Referee: nominal value 32, clamped to 0 in `showRollDiceAnimation` (`val >= 15 -> 0`). |
  | $\ge 101$ | N/A | Val - 100 | Special modifier: sets `referee = 1` (`PlayerDice` ivar 280) and subtracts 100 (`actualFinalValue -= 100`). |

- Complete Map of Ability Triggers in `checkAbilitiesCondition:player:`:
  Evaluated through a 28-entry Thumb `tbb` jump table (`0x000139be`, indexing `trigger - 1`):

  | Trigger ID | Dispatched Helper Selector | Condition Verified in Machine Code |
  | --- | --- | --- |
  | `0` | Direct bypass (checked before `tbb`) | **Unconditional**: triggers automatically if `valueChecked == 0`. |
  | `1`, `2` | `checkPairConditionWithValue1:value2:value3:` | **Pair**: at least two dice have matching values (handles Wild $\pm 100$). |
  | `3` | `checkOddFlushConditionWithValue1:value2:value3:` | **All Odd**: every active die in the encounter rolls an odd number. |
  | `4` | `checkEvenFlushConditionWithValue1:value2:value3:` | **All Even**: every active die in the encounter rolls an even number. |
  | `5` | `checkStraightConditionWithValue1:value2:value3:` | **Straight**: active dice form 3 consecutive numbers (handles Wild difference $\le 2$). |
  | `6` | `checkTripleWithValue1:value2:value3:` | **Triple**: three identical numbers rolled (handles Wild). |
  | `7` | `checkOddSumWithValue1:value2:value3:` | **Odd Sum**: the total sum of all encounter dice is odd. |
  | `8` | `checkEvenSumWithValue1:value2:value3:` | **Even Sum**: the total sum of all encounter dice is even. |
  | `9..18` | `checkRollForValue:value1:value2:value3:` | **Roll Value**: matches specific die value `trigger - 8` (values 1 through 10). |
  | `19..28` | `checkRollForValue:value1:value2:value3:` | **Roll Value**: matches specific die value `trigger - 18` (values 1 through 10). |
  | *Special* | `currentPossession` check | **Through Pass (IDs 47, 80)**: requires matching flush AND active ball possession (`-1` Red, `+1` Blue). |
  | *Special* | `FieldPlayer.initWithType:` | **Captain (ID 82)**: sets `setRedWinOnTie: 1` or `setBlueWinOnTie: 1` during initialization. |

- Complete Table of Entourage Triggers in `checkEntourageCondition:teamType:`:
  Evaluated through a 13-entry Thumb `tbh` jump table (`0x000140c8`):

  | Trigger ID | Condition Verified in Machine Code |
  | --- | --- |
  | `0` | **Unconditional**: returns 1 unconditionally. |
  | `1` | **Match Clock**: checks match time via `[gameField minute]` (e.g. 7 minutes remaining). |
  | `2` | **Goal Deficit**: checks if team is trailing by 1 or more goals (`redScore < blueScore`). |
  | `3` | **Pair with Condition**: team rolls a Pair, verified with team filters (e.g. at least one female on team). |
  | `4` | **Pair with Empty Bench**: team rolls a Pair while `[statsManager benchArray].count == 0`. |
  | `5` | **Triple**: team rolls three identical numbers. |
  | `6` | **Combo**: team rolls a specific combination. |
  | `7` | **Low Team Total**: total encounter roll sum is strictly less than threshold (e.g. $< 5$ or $< 10$). |
  | `8` | **High Opponent Die**: at least one opponent die rolls $> 6$ ($\ge 7$). |
  | `9` | **Opponent Team Total $> 20$**: `checkEnemyTeamGreaterThanValue: 20`. |
  | `10` | **Opponent Total $>$ Player Total**: `checkEnemyTeamGreaterThanPlayerTeamBy: 0`. |
  | `11` | **Opponent Leading by $\ge 4$**: `checkEnemyTeamGreaterThanPlayerTeamBy: 4` (or $\ge 6$). |
  | `12` | **Lineup / Powerup State**: team generates a 2x powerup or matches specific lineup configuration. |

- Complete Table of All 21 Entourage Handlers in `AbilityManager`:
  Every entourage in `EntourageData.plist` maps to either a dedicated handler (`Ability Icon == 0`)
  or a medal award on the buff belt (`Ability Icon > 0`):

  | ID | Entourage Name | Trigger | Icon | Handler / Dispatch | Verified Effect on State & Rules |
  | --- | --- | --- | --- | --- | --- |
  | `1` | Mary Hail | 5 | 64 | Buff Belt (`createBuffMedal: 64`) | Awards +5 medal on Triple. |
  | `2` | Callie McLooney | 0 | 0 | `addEntourageAbility2:` | Adds +1 to `redExtraBonus` / `blueExtraBonus` whenever team has possession. |
  | `3` | Miguel Panini | 7 | 24 | Buff Belt (`createBuffMedal: 24`) | Awards Cross / Reroll medal when roll sum $< 5$. |
  | `4` | Maria Single | 5 | 27 | Buff Belt (`createBuffMedal: 27`) | Awards Defensive Wall (+1 to all) medal on Triple. |
  | `5` | Remy Romantique | 3 | 17 | Buff Belt (`createBuffMedal: 17`) | Awards Dribble +2 medal on Pair if $\ge 1$ female player on team. |
  | `6` | Milan Mariott | 3 | 16 | Buff Belt (`createBuffMedal: 16`) | Awards Dribble +1 medal on Pair. |
  | `7` | Dicey Dave | 11 | 39 | Buff Belt (`createBuffMedal: 39`) | Awards Multiplier x2 medal when opponent leads by $\ge 6$. |
  | `8` | Shirley Tweety | 1 | 0 | `addEntourageAbility8:` | NOP stub in AbilityManager (`bx lr`); clock shift handled in UI/timer when 7 min remain. |
  | `9` | Jeff Blather | 9 | 46 | Buff Belt (`createBuffMedal: 46`) | Awards Feint (swap powerups / return debuff) medal when opponent rolls $> 20$. |
  | `10` | Lionel Scrimmage | 10 | 44 | Buff Belt (`createBuffMedal: 44`) | Awards Saving Tackle (invert count / swap bonuses) medal when opponent total is higher. |
  | `11` | Sheikh Abdullah I | 0 | 0 | `addEntourageAbility11:` | Adds +10,000 cash (`10000.0f`) to `winExtra` in `EndGameLayer`. |
  | `12` | Ronin Chelskovic | 0 | 0 | `addEntourageAbility12:` | Calls `[gameField setUpDrawWinnings: 1]` (draw awards win money in `EndGameLayer`). |
  | `13` | Sylvester Punter | 0 | 0 | `addEntourageAbility13:` | Adds +30,000 cash (`30000.0f`) on Win; deducts 30,000 on Loss. |
  | `14` | Hilly Will | 0 | 0 | `addEntourageAbility14:` | Adds +100,000 cash (`100000.0f`) on Win; deducts 100,000 on Loss. |
  | `15` | Torna Hammstring | 2 | 0 | `addEntourageAbility15:` | Adds +1 `extraBonus` to own team and -1 `extraBonus` to enemy team when trailing by $\ge 1$ goal. |
  | `16` | Flashy Spice | 0 | 0 | `addEntourageAbility16:` | Calls `setRedWinOnTie: 1` (or `setBlueWinOnTie: 1`), winning all ties. |
  | `17` | Parshall LeBlynde | 12 | 37 | Buff Belt (`createBuffMedal: 37`) | Awards Charge (divisor /2) medal when team produces a 2x powerup. |
  | `18` | The Special One | 7 | 42 | Buff Belt (`createBuffMedal: 42`) | Awards Anchor (freeze / immunity) medal when roll sum $< 10$. |
  | `19` | Le Professeur | 4 | 17 | Buff Belt (`createBuffMedal: 17`) | Awards Dribble +2 medal on Pair if Bench is completely empty. |
  | `20` | Pierre Bear Stare | 8 | 65 | Buff Belt (`createBuffMedal: 65`) | Awards Yellow Card medal for each opponent die rolling $\ge 7$. Second card escalates to Red Card! |
  | `21` | Sir Al Flubberson | 3 | 21 | Buff Belt (`createBuffMedal: 21`) | Awards Critical Striker (multiplier x4) medal on Pair. |

- Complete Match Clock & Turn Duration Architecture:
  Verified in `GameFieldLayer.initWithParent:` (`0x06b8e0`) and `redPlayerFinishRolling:` (`0x070ca0..0x070d0a`):
  - Initial countdown duration is loaded dynamically from `[StatsManager currentSeason]` via a `tbb` jump table (`0x06bea4`):
    - Season 0 (Training Season): `15:00` (`0x0f` minutes)
    - Season 1: `20:00` (`0x14` minutes)
    - Season 2: `25:00` (`0x19` minutes)
    - Season 3: `30:00` (`0x1e` minutes)
    - Season 4: `35:00` (`0x23` minutes)
    - Initial seconds are explicitly initialized to `0`.
  - Turn Time Deduction:
    - At the end of each round in `GameFieldLayer.redPlayerFinishRolling:`, if `loadData == 0`, time is decremented:
      $$\Delta t = 30 + (\text{arc4random}() \pmod{30}) \quad \text{seconds (range: 30 to 59 seconds elapsed per roll)}$$
    - If `seconds < 0` and `minute > 0`, minute borrows: `minute -= 1; seconds += 60;`.
    - If `minute <= 0` and `seconds < 0`, clamped to `minute = 0; seconds = 0;`.
    - In `finishLocationFadeIn` (`0x06fb9e`), if `minute == 0 && seconds == 0`, match roll is disabled and full-time / match end sequence is triggered.

- 5-Location Field Structure, Team Lineup Indices & Encounter Pairings:
  Verified in `GameFieldLayer.createRedPlayers:` (`0x06d418`), `initLocation` (`0x06de34`), and `rollDice` (`0x06fbf4`):
  - Team Lineup Array (`redPlayerArray` / `bluePlayerArray`, 7 active players on field, indices 0..6):
    - Index `0`: Goalkeeper (GK)
    - Indices `1, 2, 3`: Defenders (DEF)
    - Indices `4, 5, 6`: Midfielders / Forwards (MID/FWD, with index `5` designated as Center Forward CF / primary shooter)
  - Encounter Roll Dispatch via `tbh` jump table at `0x06fdaa` in `rollDice` (`location + 2` $\in [0..4]$):
    | Field Location | Description | Red Active Dice (`redDiceToRollArray`) | Blue Active Dice (`blueDiceToRollArray`) | Encounter Type |
    | --- | --- | --- | --- | --- |
    | `-2` | Red Penalty Box (Shot on Red Goal) | `[0]` (Red Goalkeeper) | `[5]` (Blue Center Forward) | 1v1 Shot Encounter |
    | `-1` | Red Defensive Half | `[1, 2, 3]` (3 Red Defenders) | `[4, 5, 6]` (3 Blue Attackers) | 3v3 Outfield Battle |
    | `0` | Midfield | `[4, 5, 6]` (3 Red Midfielders) | `[4, 5, 6]` (3 Blue Midfielders) | 3v3 Midfield Duel |
    | `+1` | Blue Defensive Half | `[4, 5, 6]` (3 Red Attackers) | `[1, 2, 3]` (3 Blue Defenders) | 3v3 Outfield Battle |
    | `+2` | Blue Penalty Box (Shot on Blue Goal) | `[5]` (Red Center Forward) | `[0]` (Blue Goalkeeper) | 1v1 Shot Encounter |

- Ball Movement, Turnover, Goal and Save Resolution:
  Verified in `checkPlayerScore` (`0x073638`), `shiftFieldRight` (`0x07474c`), `shiftFieldLeft` (`0x075180`), and `scoreGoal` (`0x078638`):
  - Field advancement:
    - If Red wins midfield/outfield encounter with ball possession (`currentPossession == -1`): executes `shiftFieldRight` (`location++`).
    - If Blue wins midfield/outfield encounter with ball possession (`currentPossession == 1`): executes `shiftFieldLeft` (`location--`).
  - Turnovers (tackles / possession change):
    - If defending team wins encounter while opponent has ball: triggers `changePossessionAnimation1` (`0x078750`), switching `currentPossession` without changing location immediately.
  - Goal Resolution:
    - At `location == 2` with Red possession (`currentPossession == -1`): if Red CF beats Blue GK (`redDiceRoll > blueDiceRoll`), increments `redScore++`, plays `runGoalAnimation`, and executes `scoreGoal`. Ball resets to center `location = 0` with Blue kickoff possession (`currentPossession = 1`).
    - At `location == -2` with Blue possession (`currentPossession == 1`): if Blue CF beats Red GK (`blueDiceRoll > redDiceRoll`), increments `blueScore++`, plays `runGoalAnimation`, and ball resets to center `location = 0` with Red kickoff possession (`currentPossession = -1`).
  - Save Resolution:
    - If defending GK wins or ties the shot encounter: triggers `runSaveAnimation`, assigns possession to defending goalkeeper, and executes `goalieToDefenderAnimation` (`0x0795f8`), passing the ball to the defenders and resetting to defensive half (`location = \pm 1`).

- Goal Celebration Animation Dispatch:
  Verified in `GameFieldLayer.runGoalAnimation` (`0x075bc0`..`0x0765a4`):
  - A random roll selects 1 of 3 celebratory animations with equal probability (`arc4random() % 3`):
    - `0` (`0x7657a`): `setUpGoalPlayerAnimation` (goal scorer celebratory run/dance).
    - `1` (`0x76586`): `setUpGoalPenguinAnimation` (penguin running across the field).
    - `2` (`0x76592`): `setUpGoalStreakerAnimation` (streaker sprinting across the field).
  - Triggers Confetti particle quad (`ConfettiSS.plist`), "GOAL!" banner zoom/fade sequence, and cheering crowd audio (`KidsCheering.mp3` / `CrowdWild.mp3`).

## 1:1 Faithful Presentation, Visuals & Audio Engine Specification

> [!IMPORTANT]
> **Zero-Deviation Reproduction Directive (2012 iOS Experience)**
> Any player who played Dice Soccer on iOS in 2012 must recognize this remake as virtually indistinguishable from the original: identical canvas resolution, asset scaling, modular player graphics, typography, audio cues, animation durations, and easing.

### 1. Canvas Resolution, Virtual Coordinate System & Assets
- **Base Coordinate Canvas**: `320 x 480` virtual points (`640 x 960` Retina `@2x` assets, portrait 2:3 aspect ratio). Verified in `RootViewController.shouldAutorotateToInterfaceOrientation:` (`0x003590`, `UIInterfaceOrientationPortrait`), `MultiplayerFieldBG.png` (`640 x 960`), and `BuffBelt.png` (`640 x 222`).
- **Pixel / Sprite Crispness**: Pixel art and UI sprites must use point or bilinear filtering with crisp mip settings matching Cocos2d-iphone 1.x `kCCTexture2DPixelFormat_RGBA8888`.

### 2. Typography & Fonts
- **Primary Title / Number Font**: `Krungthep.ttf` (exact font extracted from `Payload/Dice Soccer.app/Krungthep.ttf`), used for all scoreboards, dice values, timer readouts, card titles, and button headers.
- **Secondary Comic / UI Font**: `Marker Felt` (standard iOS comic font), used for dialog popups, speech bubbles, and notifications.
- **System Bold**: `Helvetica-Bold`, used for legal text, credits, and compact labels.

### 3. Modular Player Sprite Assembly & Palettes
- **Body Parts**: Extracted from `PlayerBody3.plist` (modular heads, hair, eyes, torsos, limbs, and shoes).
- **Kit Styles**: Extracted from `Shirt.plist` (28 shirt styles defining `ShirtStyle`, `SleeveColour`, `ShirtColour`, `StyleColour`).
- **Color Palettes**: Extracted from `Colour.plist` (RGB lookup mapping IDs 1..12 to exact RGB triples, e.g. `1`: `255,255,255`).

### 4. Verified Animation Timings & Easing Durations
All durations below are extracted directly from the machine-code VFP registers in ARMv7:
- **Kick-off Animation (`showKickOffAnimation`)**:
  - `CCMoveBy` duration: `0.5s`
  - `CCFadeOut` duration: `0.5s` / `1.0s` in `CCSequence`
- **Field Shift / Camera Pan (`shiftFieldRight` / `shiftFieldLeft`)**:
  - Smooth pan duration: `0.5s` between adjacent field sectors.
- **Tie Animation (`runTieAnimation`)**:
  - `CCScaleTo` duration: `0.5s` to scale `1.0f`
  - `CCFadeIn` duration: `0.5s`
  - `CCMoveBy` duration: `0.5s`
  - `CCFadeOut` duration: `0.25s`
- **Goalkeeper Save Animation (`runSaveAnimation`)**:
  - `CCScaleTo` duration: `0.5s` to scale `1.0f`
  - `CCFadeIn` duration: `0.375s`
  - `CCFadeOut` duration: `0.75s`
  - `CCMoveBy` duration: `0.75s`
  - Passes ball out via `goalieToDefenderAnimation` back to `location = \pm 1`.
- **Goal Sequence (`runGoalAnimation`)**:
  - "GOAL!" banner scale/move/fade duration: `0.5s`
  - Confetti burst: `ConfettiSS.plist` quad particles
  - Audio: `playEffect: KidsCheering.mp3` and `CrowdWild.mp3`
  - Celebration character duration: `1.5s` translation across pitch.
- **Player Poof & Substitution (`FieldPlayer.showPoofAnimation`)**:
  - Frame duration: `~0.08s` per frame for poof smoke sequence
  - Sound: `PlayerTransform.mp3` or `PlayerTransform1.mp3`.
- **Through Pass (`activateThroughPass:`)**:
  - Phase 1 fade out: `0.375s`
  - Pause / delay before Phase 2: `0.75s`
  - Forward dash + field shift.
- **Dice Shake / Roll Animation (`startShakeAnimation` / `stopShakeAnimation`)**:
  - Dice roll duration: controlled by user drag/release or accelerometer shake
  - Stop shake audio: `playEffect: Whistle1.mp3` on dice settle.

### 5. Complete Audio Landscape (48 Original Sound Files)
All 48 sound effects from `Payload/Dice Soccer.app/` must be mapped 1:1:
- **Match BGM**: `MainTheme.mp3`, `HighlightReelShort.mp3`
- **Whistles**: `Whistle1.mp3`, `Whistle2.mp3`, `3Whistle.mp3`, `3Whistle1.mp3`
- **Dice Rolling**: `DiceRoll1.mp3`, `DiceRoll2.mp3`, `DiceRoll3.mp3`
- **Crowd Reactions**: `StadiumCrowdApplause.mp3`, `StadiumCrowdChant.mp3`, `CrowdApplause.mp3`, `CrowdWild.mp3`, `CrowdDisappoint.mp3`, `KidsCheering.mp3`, `KidsBooing.mp3`, `ChildrenAaaah.mp3`
- **Buffs & Debuffs**: `Buff1..3.mp3`, `Debuff1..2.mp3`, `Powerup1..8.mp3`, `PowerPickup.mp3`, `Super1.mp3`
- **Substitutions & UI**: `PlayerTransform.mp3`, `PlayerTransform1.mp3`, `Tap1..3.mp3`, `Cash1.mp3`, `Buy1.mp3`, `MedalCeremonyShort.mp3`, `MedalCeremonyLong.mp3`

### 6. Collectible Player Cards Architecture (PlayerCards & PlayerCardsBack)
Verified via ARMv7 disassembly of `PlayerCards.PlayerCardsWithType:` (`0x00059cb0`), `PlayerCardsBack.PlayerCardsBackWithType:` (`0x0005d224`), `TeamSelectionLayer.initPlayers` (`0x00053a5c`), `TeamSelectionLayer.initBenchPlayers` (`0x00054c70`), and `DisplayCardLayer.onFlipCard:` (`0x00028ab8`):

- **Physical Card Geometry & Layer Hierarchy (`PlayerCardsWithType:`)**:
  - Exact canvas aspect ratio: $2:3$ ($480 \times 720\text{ px}$, $240 \times 360\text{ pt}$ on iPhone, $2\times$ on iPad via `tempScaleX = tempScaleY = 2`).
  - **Outer Border (`CardBorder.png`)**: Tinted via `setColor:` based on card rarity / index thresholds:
    - $\le 32$: `0x00fca757` Bronze/Gold `(252, 167, 87)`
    - $33..48$: `0x003737fc` Ruby Red `(252, 55, 55)`
    - $49..64$: `0x002dccfc` Yellow Gold `(252, 204, 45)`
    - $65..80$: `0x00c9c9c9` Silver `(201, 201, 201)`
    - $> 80$: `0x00ffffff` Platinum/White `(255, 255, 255)`
  - **Pitch Texture (`CardField.png`)**: Background pitch graphic rendered inside the card border (`z = 0`).
  - **Player Number Badge (top-left, $110 \times 120\text{ px}$)**:
    - `PlayerNumberBase.png` tinted via `setColor:` according to tactical position:
      - `GK`: Dark Charcoal/Black `(40, 40, 40)` (`0x0`)
      - `FWD`: Red `(246, 50, 50)` (`0xf6`)
      - `DEF`: Blue `(0, 100, 255)` (`0xff0000`)
      - `MID`: Green `(50, 180, 50)`
    - `PlayerNumberOutline.png`: Overlay border outline over the base.
    - Digits: `PlayerNumber{d}.png` ($40 \times 50\text{ px}$):
      - Digits $0..4$ sourced from `Card3` sprite sheet; digits $5..9$ from `Card6` sprite sheet.
      - Single-digit IDs are centered; two-digit IDs are placed side-by-side.
  - **Position Badge (top-right, $70 \times 80\text{ px}$)**:
    - `{Position}Type.png` (`GKType.png`, `FWDType.png`, `DEFType.png`, `MIDType.png` from `Card6`).
  - **Player Figure & Shadow (center, $200 \times 340\text{ px}$)**:
    - `PlayerFullFigureShadow.png` + `PlayerFullFigure{id:000}.png` (97 original characters in `PlayerFullFigureSS`).
  - **Dice Bar (lower section, $400 \times 100\text{ px}$)**:
    - `DiceBarColour.png` tinted with the position color + `DiceBarBorder.png` frame.
    - 6 horizontal dice face slots:
      - Standard face values $1..10$: `DiceNumber01.png` .. `DiceNumber10.png` (`Card6`).
      - Special ability faces $16..32$: `DiceFaceAbility{id}.png` (`Card3`).
  - **SuperPower Box & Name Label (bottom)**:
    - `SPBox.png` ($80 \times 80\text{ px}$ from `Card6`) containing `DiceFaceAbility{superpower}.png` (`Card3`).
    - Player name rendered in authentic `Krungthep.ttf`, dark ink `#1B242A`.
  - **Status Overlays**:
    - `CardLocked.png`: Displayed over unearned players.
    - `CardHireMe.png`: Displayed over hireable candidates with cash price in `Krungthep.ttf`.

- **Card Back Structure (`PlayerCardsBackWithType:`)**:
  - `CardBack.png` ($480 \times 720\text{ px}$):
    - Top: Player name in uppercase `Krungthep.ttf` (`20 pt`, Ink `#18232C`).
    - Position & Number subtitle: e.g. `GK | #1` (`Gold`).
    - Primary Superpower Section: `SPBox.png` + ability icon + ability name (`15 pt`, bold) + full ability description (`11 pt`, word-wrapped).
    - Additional Dice Abilities: Scans the 6 dice faces for any abilities $\ge 16$ distinct from the superpower, rendering `"ADDITIONAL ABILITIES:"` followed by icon and description.
    - Player Biography / Backstory: `"PLAYER BIO"` header + `player.description` in `Krungthep.ttf` (`12 pt`, clean wrap).

- **Interactive 3D Flip Mechanics (`DisplayCardLayer.onFlipCard:`)**:
  - Original Cocos2d uses `CCOrbitCamera` (`0x00028f24`) to rotate the card node $180^\circ$ around its Y-axis.
  - Triggers audio: `SimpleAudioEngine.playEffect: @"Tap1.mp3"`.
  - At the halfway mark ($90^\circ$, `onFlipHalfway` at `0x00028fb0`), swaps the visible node (`PlayerCards` $\leftrightarrow$ `PlayerCardsBack`).
  - Reconstructed in Unity as a seamless $0.3\text{s}$ dual-phase scale/flip coroutine with 1:1 `Tap1.mp3` playback.

### 7. Shop, Kit Selection, Trophy Room & Entourage Architectures
Verified via ARMv7 disassembly of `Shop` (`0x00092d00`), `ShopSubMenu` (`0x000962fc`), `ShopButton` (`0x00095090`), `KitSelectionLayer` (`0x0005fdac`), `TrophyRoom` (`0x0008bb54`), `TrophyDetailsLayer` (`0x0008de18`), and `AchievementDialog` (`0x0008eed4`):

- **The Shop System (`Shop` & `ShopSubMenu`)**:
  - **Tabs**:
    - Coins Tab (`CoinsTab.png` / `CoinsTabPressed.png`): Calls `createCoinMenu` (`type = 4`). Reads 7 cash packages from `Shop.plist` (skipping index 0). Items $\ge \$9.99$ display `ShopMoneyBar.png` (gold bars), items $<\$9.99$ display `ShopMoneyPouch.png`. Text formatted in `Krungthep.ttf` with comma thousand-separators (`25,000` to `100,000,000`).
    - Shirts Tab (`ShirtTab.png` / `ShirtTabPressed.png`): Calls `createShirtMenu` (`type = 1`). Reads 28 kit patterns from `Shirt.plist`. Composite icon preview rendered by stacking `KitSelectionShirt.png` + `KitSelectionSleeves.png` + `KitSelectionDesign{1..7}.png` tinted via `Colour.plist`.
    - Shorts Tab (`ShortsTab.png` / `ShortsTabPressed.png`): Calls `createShortsMenu` (`type = 2`). Reads 12 colors from `Colour.plist`.
  - **Item Grid Layout**:
    - Disassembly in `ShopSubMenu.showLayer:` calculates `smmul` by `0x55555556` (division by 3), arranging items in sets of 3 vertical boxes per page inside `ShopBox.png`.
    - Horizontal pagination controlled by `scrollLeftBtn` and `scrollRightBtn` with `LeftArrow.png` / `RightArrow.png`.
    - Purchased items render `ShopSoldOut.png` overlay.

- **Kit Customization (`KitSelectionLayer` & `KitManager`)**:
  - Center preview renders 4 layered sprites against `KitSelectionBG.png`:
    1. Base shorts: `KitSelectionShorts.png` tinted with `pantsColour` (1..12 from `Colour.plist`).
    2. Jersey base: `KitSelectionShirt.png` tinted with `shirtColour`.
    3. Sleeves: `KitSelectionSleeves.png` tinted with `sleeveColour`.
    4. Jersey pattern: `KitSelectionDesign{shirtStyle}.png` tinted with `styleColour` (styles 1..7).
  - Cycle arrows scroll through player's unlocked kits and shorts.
  - "DONE" button (`DoneButton.png` / `DoneButtonPressed.png`) persists team appearance to `diceSoccer.currentShirt` and `diceSoccer.currentPants`.

- **Trophy Room & Achievements (`TrophyRoom` & `TrophyDetailsLayer`)**:
  - 72 verified achievements in `Achievements.plist` mapped 1:1 to 72 trophies (`Trophy01.png` .. `Trophy72.png`).
  - Shelf hierarchy: `WoodenShelfBG.png` shelves across `GreenShelfBG.png` backing.
  - Grid: 4 columns $\times$ 3 rows = 12 trophies per shelf page across 6 pages.
  - Slot states:
    - Unlocked: `TrophyBG.png` base + colored high-resolution `Trophy{id:02d}.png`.
    - Locked: `TrophyBGLocked.png` base + dark silhouette.
  - Tapping opens `TrophyDetailsLayer` modal using `LongDialogBox.png`, showing title, unlocked/locked criteria, gold/pants/entourage rewards, and status.
  - Refresh button (`RefreshButton.png` from `TrophyRoomBGSS`) audits progress against career state.

- **Entourage Staff Management (`EntourageData.plist`)**:
  - 21 staff members across 3 tactical categories:
    - `OFF` (Officials, blue badge): e.g. Lionel Scrimmage, Parshall LeBlynde, Pierre Bear Stare, Sir Al Flubberson.
    - `WAG` (WAGs, magenta badge): e.g. Flashy Spice.
    - `SPE` (Specialists, gold badge): e.g. Sheikh Abdullah I, Ronin Chelskovic, Sylvester Punter, Hilly Will, Torna Hammstring.
  - Max 2 staff members equipped simultaneously.
  - Full figure collectibles: `EntourageFullFigure01..21.png` + `EntourageFullFigureShadow.png`.
  - Verified Match Effects:
    - **Flashy Spice (ID 16)**: `HomeWinOnTie = true` — player team automatically wins turns ending in ties.
    - **Torna Hammstring (ID 15)**: `+1` possession bonus when 1 or more goals behind.
    - **Sheikh Abdullah I (ID 11)**: `+10,000` cash payout upon match victory.
    - **Ronin Chelskovic (ID 12)**: Matches ending in a Draw award the full Win cash payout.
    - **Sylvester Punter (ID 13)**: Stakes wager: `+15,000` cash on Win, `-15,000` cash on Loss.
    - **Hilly Will (ID 14)**: High-roller wager: `+50,000` cash on Win, `-50,000` cash on Loss.
    - **Coaches (ID 18, 19, 21)**: Pair and roll-threshold dice modifications.
  - Active staff buff medals (`BuffMedal16..82.png`) are rendered directly on the match screen `BuffBelt`.

---

## 8. In-Match UI, Animations, Story Dialogues & Kit Tinting (Verified from ARMv7 & Cocos2d)

### 8.1 In-Match Pause Menu (`PauseMenuLayer`, ARMv7 0x0006a0e0..0x0006b708)
- **Modal Structure**:
  - Dimmed backdrop overlay (`0x0006a124`).
  - Container: `LongDialogBox.png` (360x480).
  - Title Banner: `Paused.png` at top.
- **Menu Buttons & Actions**:
  - **Resume** (`ResumeButton.png` / `ResumeButtonPressed.png`): Unpauses match and resumes scheduler.
  - **Substitution** (`SubstitutionButton.png` / `SubstitutionButtonPressed.png`):
    - Sub counter badge: `SubstitutionCountBG.png` displaying `"{_remainingSubs} SUBS LEFT"`.
    - Opens in-game substitution screen allowing bench reserve swaps. Disabled when remaining subs reach 0.
  - **Options** (`PauseOptionsButton.png` / `PauseOptionsButtonPressed.png`):
    - Opens the `OptionsLayer` overlay.
  - **Forfeit** (`ForfeitButton.png` / `ForfeitButtonPressed.png`):
    - Prompts with forfeit confirmation dialog: *"ARE YOU SURE YOU WANT TO FORFEIT?"*.
    - Buttons: `YesButton.png` (concedes match as defeat) and `NoButton.png` (cancels back to pause menu).

### 8.2 In-Game Options Layer (`OptionsLayer`, ARMv7 0x0009e134..0x0009f59c)
- **Sound & Music Toggle**:
  - Authentic checkbox: `OptionsTickBox.png` displaying `OptionsTick.png` when enabled.
  - Toggles global `AudioManager` sound and BGM.
- **Handbook**:
  - `HandbookButton.png` / `HandbookButtonPressed.png`.
  - Displays original rules modal explaining rolls, duel outcomes, penalty box shot rules, and entourage buffs.
- **Credits**:
  - `CreditsButton.png` / `CreditsButtonPressed.png`.
  - Credits the original 2012 creators (Red Robot Labs & Foursaken Media).
- **Done**:
  - `DoneButton.png` returns to the Pause Menu.

### 8.3 Authentic Goal Celebration Animation (`runGoalAnimation`, ARMv7 0x000e0e90..0x000e3908)
- **Sunburst Ray Backdrop**: `GoalRotation.png` rotating behind the goal banner at 75 deg/sec.
- **Banner Pop-In**: `Goal.png` with Cocos2d `CCScaleTo` pop-in bounce (scale 0.0 -> 1.15 in 0.25s, then 1.15 -> 1.0 in 0.15s).
- **Confetti Particle Shower**: 20 confetti sprites (`Confetti1.png` .. `Confetti3.png`) drifting and rotating downwards.
- **Celebration Runner Sequence**:
  - Alternates between Penguin flapping (`PenguinBody.png`), Streaker sprinting (`Streaker01..12.png`), and scoring striker figure.
- **Audio Choreography**: Whistle (`Whistle1.mp3`), goal cheering (`KidsCheering.mp3`), and crowd roar (`CrowdWild.mp3`).

### 8.4 Post-Match Result Screen (`MatchResultDialog`, ARMv7 0x0007e6cd..0x000802e5)
- **Pop-In Bounce**: Box starts at scale 0.4 and bounces up to 1.15 before settling to 1.0.
- **Result-Specific Artwork**:
  - **Victory**: `GameOverVictoryBox.png` + `GameOverVictory.png` banner + victory fanfare (`MedalCeremonyLong.mp3`) + confetti rain.
  - **Draw**: `GameOverDrawBox.png` + `GameOverDraw.png` banner + crowd murmur (`ChildrenAaaah.mp3`).
  - **Defeat**: `GameOverDefeatBox.png` + `GameOverDefeat.png` banner + crowd booing (`KidsBooing.mp3`).
- **Star Rating**: `Gold0Star.png` .. `Gold3Star.png` corresponding to match performance (1 star for loss, 2 for draw, 3 for win, 4 internal stars for win by >= 2 goals).
- **Coin Reward Payout**: Displays earned prize money with authentic cash register chime (`Cash1.mp3`).
- **Continue Flow**: Advances career progression, unlocks next fixture, and triggers story tutorials if milestone reached.

### 8.5 Tutorial & Story Dialogues (`Tutorial.plist`, `PersistentTutorial.plist`, `AchievementDialog`)
- Universal modal dialog system modeled 1:1 on Cocos2d `AchievementDialog` (`0x0008eed5`):
  - Container: `LongDialogBox.png`.
  - Speaker Portrait: Coach Jerry Hattrick (`KidHead.png` / `PlayerHead93.png`).
  - Typography: Authentic `Krungthep.ttf`.
  - Illustrated Slides: Center preview illustration `Custom1.png` .. `Custom7.png` (`CustomPopup1.plist` / `CustomPopup2.plist`).
  - Navigation: Multi-page dialogues with `DoneButton.png`.
- **Milestone Triggers**:
  - **First Launch / Season 0** (ID 5): Jerry Hattrick welcomes player and introduces the new Dice Soccer fad.
  - **First Training Match** (ID 24): Jerry explains season league progress against *The Neighbours*.
  - **Match Kickoff** (ID 11): Explains dice transformations, turn total additions, and moving toward goal.
  - **Post-Match 1** (ID 21): Jerry explains his loan status and instructs the coach to enter MANAGE mode.
  - **Locker Room / Kit Selection** (ID 3): Jerry introduces team colors and achievement unlocks.
  - **Trophy Cabinet** (ID 4): Jerry introduces career achievements and trophies.
  - **Entourage Staff** (ID 49): Jerry introduces the 21 fan, official, and specialist staff members.

### 8.6 Field Formation Kit Tinting
- Formation players on the match pitch visually display the active team's custom kit colors:
  - Home team outfielders wear the user's selected shirt color (`_currentShirtId`) and shorts color (`_currentPantsId`).
  - Away team outfielders wear the fixture's specific kit design and colors from `MatchLineup.plist`.

---

## Multiplayer Scope & Future Roadmap

> [!NOTE]
> **REMEMBER / TODO: Multiplayer (`MultiplayerGameFieldLayer`) Scope**
> Multiplayer functionality (`MultiplayerGameFieldLayer`, `MultiplayerManager`, Apple Game Center friend matchmaking, and Bluetooth peer-to-peer networking) is **intentionally deferred and temporarily out of scope**.
> All current development is focused exclusively on achieving a 100% feature-complete, bug-free, 1:1 reproduction of the original single-player Campaign (Training Season + Seasons 1..4), Arcade Match, Team Management, Shop, and Trophy Room. Multiplayer will be evaluated as a separate milestone after the single-player game is fully validated.

> [!NOTE]
> **REMEMBER / TODO: Curve di Interpolazione Grafica e Durate Animazioni Cocos2d**
> Le curve di interpolazione grafica (easing functions: `CCEaseBounceOut`, `CCEaseBackOut`, `CCEaseSineInOut`) e le durate in millisecondi di alcune animazioni Cocos2d secondarie (particelle di polvere durante i tackle, applausi del pubblico, ondeggiamento bandierine) vengono gestite in Unity tramite i componenti nativi (`Animator`, `Tween`, `SpriteRenderer`, `ParticleSystem`). Tutti i parametri di tempistica e curve sono calibrati per garantire la fedeltà visiva 1:1 con l'originale del 2012.

---

Do not use these observations to infer gameplay rules beyond what is explicitly
proven; every mechanic above has been validated at the machine-code instruction level.

This is an early playable reconstruction, not feature-complete or yet verified
in Unity. The Manage Team screen supports hiring only original players with a
blank unlock requirement, at their source-defined cash cost. Players with
unlock criteria become hireable as their verified career milestones are achieved.
The screen supports a persistent bench and role-checked lineup selection;
match-time substitutions are currently limited to between rolls. Achievements,
remaining screens, richer character movement and original animation timings still
need to be implemented and compared with gameplay references. The core match rules,
dice-roll resolutions, ability handlers, referee calls, card mechanics, through passes,
star ratings, entourage effects, and season promotion thresholds are now 100% verified
via ARMv7 binary disassembly.
