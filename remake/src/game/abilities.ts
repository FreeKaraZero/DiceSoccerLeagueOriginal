import type { AbilityRecord } from "../data/types";
import type { Face } from "./dice";

/**
 * `BuffTrigger` values used by `Abilities.plist`. Values 19..26 mean
 * "the team rolled the face N", where N is `trigger - FACE_TRIGGER_OFFSET`.
 */
export const Trigger = {
  None: 0,
  WinTies: 1,
  Pair: 2,
  AllOdd: 3,
  AllEven: 4,
  Straight: 5,
  Triple: 6,
  TotalOdd: 7,
  TotalEven: 8,
} as const;

export const FACE_TRIGGER_OFFSET = 18;
const FIRST_FACE_TRIGGER = 19;
const LAST_FACE_TRIGGER = 26;

export function triggerLabel(trigger: number): string {
  if (trigger >= FIRST_FACE_TRIGGER && trigger <= LAST_FACE_TRIGGER) return `${trigger - FACE_TRIGGER_OFFSET}`;
  switch (trigger) {
    case Trigger.Pair:
      return "PAIR";
    case Trigger.AllOdd:
      return "ALL ODD";
    case Trigger.AllEven:
      return "ALL EVEN";
    case Trigger.Straight:
      return "STRAIGHT";
    case Trigger.Triple:
      return "TRIPLE";
    case Trigger.TotalOdd:
      return "ODD TOTAL";
    case Trigger.TotalEven:
      return "EVEN TOTAL";
    case Trigger.WinTies:
      return "TIES";
    default:
      return "";
  }
}

/**
 * Evaluates a trigger against a roll. Wild sides stand in for any value, so
 * every substitution of the wilds is tried before giving up.
 */
export function isTriggered(trigger: number, roll: readonly Face[]): boolean {
  if (trigger === Trigger.None || trigger === Trigger.WinTies) return trigger === Trigger.WinTies;
  const wildIndex = roll.findIndex((face) => face.wild);
  if (wildIndex !== -1) {
    const candidates = new Set<number>([...roll.map((face) => face.value), ...FACE_SUBSTITUTIONS]);
    return [...candidates].some((value) =>
      isTriggered(
        trigger,
        roll.map((face, index) => (index === wildIndex ? { ...face, value, wild: false } : face)),
      ),
    );
  }
  return matchesPattern(trigger, roll.map((face) => face.value));
}

const FACE_SUBSTITUTIONS = [1, 2, 3, 4, 5, 6, 7, 8];

function matchesPattern(trigger: number, roll: readonly number[]): boolean {
  const total = roll.reduce((sum, value) => sum + value, 0);
  const counts = new Map<number, number>();
  for (const value of roll) counts.set(value, (counts.get(value) ?? 0) + 1);

  if (trigger >= FIRST_FACE_TRIGGER && trigger <= LAST_FACE_TRIGGER) {
    return roll.includes(trigger - FACE_TRIGGER_OFFSET);
  }
  switch (trigger) {
    case Trigger.Pair:
      return [...counts.values()].some((count) => count === 2);
    case Trigger.Triple:
      return [...counts.values()].some((count) => count >= 3);
    case Trigger.AllOdd:
      return roll.every((value) => value % 2 === 1);
    case Trigger.AllEven:
      return roll.every((value) => value % 2 === 0);
    case Trigger.TotalOdd:
      return total % 2 === 1;
    case Trigger.TotalEven:
      return total % 2 === 0;
    case Trigger.Straight: {
      const unique = [...new Set(roll)].sort((a, b) => a - b);
      return unique.length === roll.length && unique.every((value, index) => index === 0 || value === unique[index - 1] + 1);
    }
    default:
      return false;
  }
}

export type EffectKind = "double" | "halve" | "add" | "subtract" | "reroll" | "instantWin" | "winTies" | "flavour";

export interface Effect {
  kind: EffectKind;
  /** Amount for `add` / `subtract`. */
  amount: number;
  /** Whether the effect lands on an enemy die instead of one of ours. */
  onEnemy: boolean;
}

/**
 * The original stores effects as an opaque `BuffType` plus English copy, and the
 * numeric part only lives in the copy, so the copy is what we read. Anything we
 * cannot map yet stays as `flavour`: it is announced but changes no dice.
 */
export function parseEffect(ability: AbilityRecord): Effect {
  const text = ability.description.split("ACTIVATES")[0]?.toLowerCase() ?? "";
  const onEnemy = /opponent|enemy|other team|his |their /.test(text) && !/your own/.test(text);
  if (ability.trigger === Trigger.WinTies) return { kind: "winTies", amount: 0, onEnemy: false };
  if (/instantly (move forward|win)/.test(text)) return { kind: "instantWin", amount: 0, onEnemy: false };
  if (/double/.test(text)) return { kind: "double", amount: 2, onEnemy };
  if (/(divide|halve|half)/.test(text)) return { kind: "halve", amount: 2, onEnemy };
  if (/re-?roll/.test(text)) return { kind: "reroll", amount: 0, onEnemy };

  const subtract = /subtracts? (\d+)/.exec(text) ?? /removes? (\d+)/.exec(text);
  if (subtract) return { kind: "subtract", amount: Number(subtract[1]), onEnemy: true };
  const add = /adds? (\d+)/.exec(text);
  if (add) return { kind: "add", amount: Number(add[1]), onEnemy: false };

  return { kind: "flavour", amount: 0, onEnemy: false };
}
