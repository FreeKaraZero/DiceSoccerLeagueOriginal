import type { AbilityRecord, GameData, PlayerRecord } from "../data/types";
import { isTriggered, parseEffect, type Effect } from "./abilities";
import { describeFace, type Face } from "./dice";
import { createRng, type Rng } from "./rng";

export type Side = "home" | "away";

/** Lineup slots, in the order used by `MatchLineup.plist`. */
export const GOALKEEPER_SLOT = 0;
export const DEFENCE_SLOTS = [1, 2, 3] as const;
export const ATTACK_SLOTS = [4, 5, 6] as const;
/** The middle attacker is the one who takes the shot, as in the original. */
export const CENTRAL_ATTACK_SLOT = 5;

/** Ball positions: 0 is the home goal, 4 the away goal, 2 the kickoff spot. */
export const BALL_ZONES = 5;
export const CENTRE_ZONE = 2;

export const SECONDS_PER_ENCOUNTER = 60;
export const DEFAULT_MATCH_SECONDS = 90 * 60;

export interface MatchTeam {
  name: string;
  /** Seven players: [GK, DEF, DEF, DEF, FWD, FWD, FWD]. */
  lineup: PlayerRecord[];
}

export interface DieState {
  slot: number;
  player: PlayerRecord;
  /** Rolled side, before abilities. */
  rolled: Face;
  /** Points contributed after every ability of the encounter has been applied. */
  value: number;
}

export interface TriggeredAbility {
  side: Side;
  player: PlayerRecord;
  ability: AbilityRecord;
  effect: Effect;
  /** Slot of the die the effect landed on, or null for effects we only announce. */
  targetSlot: number | null;
}

export interface Encounter {
  attacker: Side;
  dice: Record<Side, DieState[]>;
  totals: Record<Side, number>;
  triggered: TriggeredAbility[];
  winner: Side | null;
  /** Set when the encounter ended with a shot on goal. */
  shot: { scorer: Side; goal: boolean; shooter: number; keeper: number } | null;
}

export interface MatchSnapshot {
  minute: number;
  secondsRemaining: number;
  score: Record<Side, number>;
  possession: Side;
  ballZone: number;
  finished: boolean;
}

const other = (side: Side): Side => (side === "home" ? "away" : "home");

export interface MatchOptions {
  seed?: number;
  matchSeconds?: number;
  kickoff?: Side;
}

export class Match {
  readonly teams: Record<Side, MatchTeam>;
  private readonly abilities: Map<number, AbilityRecord>;
  private readonly rng: Rng;
  private readonly matchSeconds: number;
  private score: Record<Side, number> = { home: 0, away: 0 };
  private secondsRemaining: number;
  private possession: Side;
  private ballZone = CENTRE_ZONE;

  constructor(home: MatchTeam, away: MatchTeam, abilities: Map<number, AbilityRecord>, options: MatchOptions = {}) {
    this.teams = { home, away };
    this.abilities = abilities;
    this.rng = createRng(options.seed ?? Date.now());
    this.matchSeconds = options.matchSeconds ?? DEFAULT_MATCH_SECONDS;
    this.secondsRemaining = this.matchSeconds;
    this.possession = options.kickoff ?? (this.rng.next() < 0.5 ? "home" : "away");
  }

  snapshot(): MatchSnapshot {
    return {
      minute: Math.ceil((this.matchSeconds - this.secondsRemaining) / 60),
      secondsRemaining: this.secondsRemaining,
      score: { ...this.score },
      possession: this.possession,
      ballZone: this.ballZone,
      finished: this.secondsRemaining <= 0,
    };
  }

  /** Plays one encounter: both trios roll, abilities resolve, the ball moves. */
  play(): Encounter {
    if (this.secondsRemaining <= 0) throw new Error("the match is over");
    const attacker = this.possession;
    const defender = other(attacker);

    const dice: Record<Side, DieState[]> = {
      home: this.rollTrio("home", attacker === "home" ? ATTACK_SLOTS : DEFENCE_SLOTS),
      away: this.rollTrio("away", attacker === "away" ? ATTACK_SLOTS : DEFENCE_SLOTS),
    };

    const triggered = [...this.resolveAbilities("home", dice), ...this.resolveAbilities("away", dice)];
    const totals = { home: total(dice.home), away: total(dice.away) };

    const instantWinner = triggered.find((entry) => entry.effect.kind === "instantWin")?.side ?? null;
    let winner: Side | null = instantWinner;
    if (winner === null) {
      if (totals[attacker] > totals[defender]) winner = attacker;
      else if (totals[defender] > totals[attacker]) winner = defender;
      else winner = triggered.find((entry) => entry.effect.kind === "winTies")?.side ?? null;
    }

    this.secondsRemaining = Math.max(0, this.secondsRemaining - SECONDS_PER_ENCOUNTER);

    const encounter: Encounter = { attacker, dice, totals, triggered, winner, shot: null };
    if (winner !== null) {
      this.possession = winner;
      this.advanceBall(winner);
      encounter.shot = this.resolveShot(winner);
    }
    return encounter;
  }

  private advanceBall(winner: Side): void {
    const direction = winner === "home" ? 1 : -1;
    this.ballZone = Math.min(BALL_ZONES - 1, Math.max(0, this.ballZone + direction));
  }

  private resolveShot(shooter: Side): Encounter["shot"] {
    const atGoal = shooter === "home" ? this.ballZone === BALL_ZONES - 1 : this.ballZone === 0;
    if (!atGoal) return null;

    const keeperSide = other(shooter);
    const shooterDie = this.rollDie(shooter, CENTRAL_ATTACK_SLOT);
    const keeperDie = this.rollDie(keeperSide, GOALKEEPER_SLOT);
    const goal = shooterDie.value > keeperDie.value;
    if (goal) this.score[shooter] += 1;

    this.ballZone = CENTRE_ZONE;
    this.possession = goal ? keeperSide : shooter;
    return { scorer: shooter, goal, shooter: shooterDie.value, keeper: keeperDie.value };
  }

  private rollTrio(side: Side, slots: readonly number[]): DieState[] {
    return slots.map((slot) => this.rollDie(side, slot));
  }

  private rollDie(side: Side, slot: number): DieState {
    const player = this.teams[side].lineup[slot];
    if (player === undefined) throw new Error(`${side} team has no player in slot ${slot}`);
    const rolled = describeFace(this.rng.pick(player.faces));
    return { slot, player, rolled, value: rolled.value };
  }

  private resolveAbilities(side: Side, dice: Record<Side, DieState[]>): TriggeredAbility[] {
    const own = dice[side];
    const enemy = dice[other(side)];
    const roll = own.map((die) => die.rolled);
    const results: TriggeredAbility[] = [];

    for (const die of own) {
      const ability = this.abilities.get(die.player.abilityId);
      if (ability === undefined || !isTriggered(ability.trigger, roll)) continue;
      const effect = parseEffect(ability);
      const target = this.applyEffect(effect, own, enemy);
      results.push({ side, player: die.player, ability, effect, targetSlot: target?.slot ?? null });
    }
    return results;
  }

  /** Picks the target that helps the owner most and applies the effect to it. */
  private applyEffect(effect: Effect, own: DieState[], enemy: DieState[]): DieState | null {
    const pool = effect.onEnemy ? enemy : own;
    switch (effect.kind) {
      case "double":
      case "add": {
        const target = highest(pool);
        if (target === null) return null;
        target.value = effect.kind === "double" ? target.value * 2 : target.value + effect.amount;
        return target;
      }
      case "halve":
      case "subtract": {
        const target = highest(pool);
        if (target === null) return null;
        target.value =
          effect.kind === "halve" ? Math.floor(target.value / 2) : Math.max(0, target.value - effect.amount);
        return target;
      }
      case "reroll": {
        const target = lowest(pool);
        if (target === null) return null;
        target.rolled = describeFace(this.rng.pick(target.player.faces));
        target.value = target.rolled.value;
        return target;
      }
      default:
        return null;
    }
  }
}

const total = (dice: readonly DieState[]): number => dice.reduce((sum, die) => sum + die.value, 0);

function highest(dice: DieState[]): DieState | null {
  return dice.reduce<DieState | null>((best, die) => (best === null || die.value > best.value ? die : best), null);
}

function lowest(dice: DieState[]): DieState | null {
  return dice.reduce<DieState | null>((worst, die) => (worst === null || die.value < worst.value ? die : worst), null);
}

/** Builds a `MatchTeam` from a lineup of player ids, as stored in the original data. */
export function teamFromLineup(data: GameData, name: string, lineup: readonly number[]): MatchTeam {
  return {
    name,
    lineup: lineup.map((id) => {
      const player = data.players.get(id);
      if (player === undefined) throw new Error(`unknown player id ${id} in lineup of ${name}`);
      return player;
    }),
  };
}
