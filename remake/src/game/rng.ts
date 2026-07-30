/** Deterministic RNG so matches can be replayed and unit tested. */
export interface Rng {
  next(): number;
  pick<T>(values: readonly T[]): T;
}

export function createRng(seed: number): Rng {
  let state = seed >>> 0 || 1;
  const next = (): number => {
    // xorshift32
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    state >>>= 0;
    return state / 0x100000000;
  };
  return {
    next,
    pick: (values) => {
      const value = values[Math.floor(next() * values.length)];
      if (value === undefined) throw new Error("cannot pick from an empty list");
      return value;
    },
  };
}
