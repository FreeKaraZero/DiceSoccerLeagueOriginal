export type Position = "GK" | "DEF" | "MID" | "FWD";

/** A row of `PlayerData.plist` / `DummyPlayerData.plist`. */
export interface PlayerRecord {
  id: number;
  name: string;
  position: Position;
  /** The six faces of the player's die, as printed on his card. */
  faces: number[];
  abilityId: number;
  description: string;
  cost: number;
  imageId: number;
  skinColour: number;
}

/** A row of `Abilities.plist`. */
export interface AbilityRecord {
  id: number;
  name: string;
  description: string;
  spriteId: number;
  trigger: number;
  buffType: number;
  buffSlot: number;
  buffTarget: number;
}

/** A team of `MatchLineup.plist`. Lineup slots are [GK, DEF, DEF, DEF, FWD, FWD, FWD]. */
export interface TeamRecord {
  id: string;
  name: string;
  lineup: number[];
  shirt: number;
  shirtPatternColor: number;
  shirtPatternStyle: number;
  pants: number;
  cashWin: number;
  cashDraw: number;
  cashLose: number;
}

export interface GameData {
  players: Map<number, PlayerRecord>;
  abilities: Map<number, AbilityRecord>;
  seasons: Map<string, Map<string, TeamRecord[]>>;
  tips: string[];
  initialRoster: number[];
}
