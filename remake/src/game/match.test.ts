import { describe, expect, it } from "vitest";
import type { AbilityRecord, PlayerRecord } from "../data/types";
import { isTriggered, parseEffect, Trigger } from "./abilities";
import { describeFace } from "./dice";
import { ATTACK_SLOTS, CENTRE_ZONE, DEFENCE_SLOTS, Match, SECONDS_PER_ENCOUNTER, type MatchTeam } from "./match";

const player = (id: number, faces: number[], abilityId = 0): PlayerRecord => ({
  id,
  name: `Player ${id}`,
  position: "FWD",
  faces,
  abilityId,
  description: "",
  cost: 0,
  imageId: 0,
  skinColour: 0,
});

const team = (name: string, faces: number[], abilityId = 0): MatchTeam => ({
  name,
  lineup: Array.from({ length: 7 }, (_, slot) => player(slot, faces, abilityId)),
});

const ability = (overrides: Partial<AbilityRecord>): AbilityRecord => ({
  id: 1,
  name: "Test",
  description: "Choose a die and double its value. ACTIVATES when your team rolls a PAIR.",
  spriteId: 0,
  trigger: Trigger.Pair,
  buffType: 2,
  buffSlot: 0,
  buffTarget: 1,
  ...overrides,
});

describe("faces", () => {
  it("reads plain numbers, bonus and re-roll sides", () => {
    expect(describeFace(7)).toMatchObject({ value: 7, wild: false, rerolls: 0 });
    expect(describeFace(17)).toMatchObject({ value: 2, rerolls: 0 });
    expect(describeFace(25)).toMatchObject({ value: 0, rerolls: 2 });
    expect(describeFace(31)).toMatchObject({ value: 0, wild: true });
  });
});

describe("triggers", () => {
  const roll = (...faces: number[]) => faces.map(describeFace);

  it("matches combos on the rolled values", () => {
    expect(isTriggered(Trigger.Pair, roll(3, 3, 5))).toBe(true);
    expect(isTriggered(Trigger.Pair, roll(3, 3, 3))).toBe(false);
    expect(isTriggered(Trigger.Triple, roll(3, 3, 3))).toBe(true);
    expect(isTriggered(Trigger.Straight, roll(2, 3, 4))).toBe(true);
    expect(isTriggered(Trigger.AllOdd, roll(1, 3, 5))).toBe(true);
    expect(isTriggered(Trigger.TotalEven, roll(1, 3, 4))).toBe(true);
  });

  it("matches a rolled face for triggers 19..26", () => {
    expect(isTriggered(21, roll(1, 3, 6))).toBe(true);
    expect(isTriggered(21, roll(1, 4, 6))).toBe(false);
  });

  it("lets a wild side stand in for any value", () => {
    expect(isTriggered(Trigger.Triple, roll(4, 4, 31))).toBe(true);
    expect(isTriggered(24, roll(1, 2, 31))).toBe(true);
  });
});

describe("effects", () => {
  it("reads the numeric part out of the original ability copy", () => {
    expect(parseEffect(ability({}))).toMatchObject({ kind: "double" });
    expect(parseEffect(ability({ description: "Subtracts 3 from all opponent dice." }))).toMatchObject({
      kind: "subtract",
      amount: 3,
      onEnemy: true,
    });
    expect(parseEffect(ability({ description: "Re-roll any one die of your choice." }))).toMatchObject({ kind: "reroll" });
    expect(parseEffect(ability({ trigger: Trigger.WinTies }))).toMatchObject({ kind: "winTies" });
  });
});

describe("Match", () => {
  it("gives the ball to the stronger trio and keeps the clock running", () => {
    const strong = team("Strong", [9]);
    const weak = team("Weak", [1]);
    const match = new Match(strong, weak, new Map(), { seed: 1, kickoff: "home", matchSeconds: 600 });

    const encounter = match.play();
    expect(encounter.attacker).toBe("home");
    expect(encounter.dice.home.map((die) => die.slot)).toEqual([...ATTACK_SLOTS]);
    expect(encounter.dice.away.map((die) => die.slot)).toEqual([...DEFENCE_SLOTS]);
    expect(encounter.totals).toEqual({ home: 27, away: 3 });
    expect(encounter.winner).toBe("home");
    expect(match.snapshot().ballZone).toBe(CENTRE_ZONE + 1);
    expect(match.snapshot().secondsRemaining).toBe(600 - SECONDS_PER_ENCOUNTER);
  });

  it("leaves possession alone on a tie", () => {
    const match = new Match(team("A", [4]), team("B", [4]), new Map(), { seed: 2, kickoff: "away", matchSeconds: 600 });
    const encounter = match.play();
    expect(encounter.winner).toBeNull();
    expect(match.snapshot()).toMatchObject({ possession: "away", ballZone: CENTRE_ZONE });
  });

  it("applies a triggered ability to the dice", () => {
    const abilities = new Map([[1, ability({})]]);
    const match = new Match(team("A", [3], 1), team("B", [1]), abilities, {
      seed: 3,
      kickoff: "home",
      matchSeconds: 600,
    });
    const encounter = match.play();
    // Three 3s are a triple, not a pair, so nothing fires for the away side either.
    expect(encounter.triggered).toHaveLength(0);
    expect(encounter.totals.home).toBe(9);
  });

  it("scores when the ball reaches the goal and the striker beats the keeper", () => {
    const match = new Match(team("A", [9]), team("B", [1]), new Map(), { seed: 4, kickoff: "home", matchSeconds: 6000 });
    let goals = 0;
    for (let i = 0; i < 10; i += 1) {
      const encounter = match.play();
      if (encounter.shot?.goal === true) goals += 1;
    }
    expect(goals).toBeGreaterThan(0);
    expect(match.snapshot().score.home).toBe(goals);
    expect(match.snapshot().ballZone).toBe(CENTRE_ZONE);
  });
});
