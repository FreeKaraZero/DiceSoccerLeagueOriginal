/**
 * Die faces as stored in `DiceSides` of the original player data. Faces 1..14
 * are plain numbers; the rest are the special sides drawn in the `Buff6` sheet.
 */
export const BONUS_FACES = { plusOne: 16, plusTwo: 17, plusThree: 18 } as const;
export const REROLL_FACES = { one: 24, two: 25, three: 26 } as const;
export const WILD_FACES = [31, 32] as const;
export const MAX_NUMBER_FACE = 14;

export interface Face {
  /** Raw face as printed on the card. */
  face: number;
  /** Points this face contributes to the team total. */
  value: number;
  /** A wild side scores nothing but satisfies any ability trigger. */
  wild: boolean;
  /** Re-rolls this face grants to its team. */
  rerolls: number;
}

export function describeFace(face: number): Face {
  if (face <= MAX_NUMBER_FACE) return { face, value: face, wild: false, rerolls: 0 };
  if (face >= BONUS_FACES.plusOne && face <= BONUS_FACES.plusThree) {
    return { face, value: face - (BONUS_FACES.plusOne - 1), wild: false, rerolls: 0 };
  }
  if (face >= REROLL_FACES.one && face <= REROLL_FACES.three) {
    return { face, value: 0, wild: false, rerolls: face - (REROLL_FACES.one - 1) };
  }
  return { face, value: 0, wild: (WILD_FACES as readonly number[]).includes(face), rerolls: 0 };
}

/** Name of the sprite in the `Buff6` atlas that shows this face. */
export function faceFrame(face: number): string {
  return `DiceSide${String(face).padStart(2, "0")}.png`;
}
