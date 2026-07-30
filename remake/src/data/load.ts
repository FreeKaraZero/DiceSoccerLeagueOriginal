import type { AbilityRecord, GameData, PlayerRecord, Position, TeamRecord } from "./types";

/** Lineups reference the reserve roster in `DummyPlayerData.plist` with this offset. */
export const DUMMY_PLAYER_OFFSET = 1000;

type Json = string | number | boolean | null | Json[] | { [key: string]: Json };
type JsonObject = { [key: string]: Json };

function asObject(value: Json | undefined, where: string): JsonObject {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error(`expected an object at ${where}`);
  }
  return value;
}

function asString(value: Json | undefined, where: string): string {
  if (typeof value !== "string") throw new Error(`expected a string at ${where}`);
  return value;
}

function asNumber(value: Json | undefined, where: string): number {
  if (typeof value === "number") return value;
  if (typeof value === "string" && value.trim() !== "" && Number.isFinite(Number(value))) return Number(value);
  throw new Error(`expected a number at ${where}`);
}

function asNumberArray(value: Json | undefined, where: string): number[] {
  if (!Array.isArray(value)) throw new Error(`expected an array at ${where}`);
  return value.map((entry, index) => asNumber(entry, `${where}[${index}]`));
}

function isPosition(value: string): value is Position {
  return value === "GK" || value === "DEF" || value === "MID" || value === "FWD";
}

function parsePlayer(id: number, raw: JsonObject, where: string): PlayerRecord {
  const position = asString(raw["Position"], `${where}.Position`);
  if (!isPosition(position)) throw new Error(`unknown position "${position}" at ${where}`);
  return {
    id,
    name: asString(raw["Name"], `${where}.Name`),
    position,
    faces: asString(raw["DiceSides"], `${where}.DiceSides`)
      .split(",")
      .map((face) => asNumber(face.trim(), `${where}.DiceSides`)),
    abilityId: asNumber(raw["Superpower"] ?? 0, `${where}.Superpower`),
    description: asString(raw["Player Description"] ?? "", `${where}.Player Description`),
    cost: asNumber(raw["Player Cost"] ?? 0, `${where}.Player Cost`),
    imageId: asNumber(raw["ImageID"] ?? 0, `${where}.ImageID`),
    skinColour: asNumber(raw["SkinColour"] ?? 0, `${where}.SkinColour`),
  };
}

function parsePlayers(raw: JsonObject, offset: number, source: string): PlayerRecord[] {
  const players: PlayerRecord[] = [];
  for (const [key, value] of Object.entries(raw)) {
    if (!/^\d+$/.test(key)) continue; // skips bookkeeping keys such as LastPlayerID
    players.push(parsePlayer(offset + Number(key), asObject(value, `${source}.${key}`), `${source}.${key}`));
  }
  return players;
}

function parseAbilities(raw: JsonObject): AbilityRecord[] {
  const abilities = asObject(raw["Abilities"], "Abilities.Abilities");
  return Object.entries(abilities).map(([key, value]) => {
    const where = `Abilities.${key}`;
    const record = asObject(value, where);
    return {
      id: Number(key),
      name: asString(record["Name"], `${where}.Name`),
      description: asString(record["AbilityDescription"], `${where}.AbilityDescription`),
      spriteId: asNumber(record["SpriteID"], `${where}.SpriteID`),
      trigger: asNumber(record["BuffTrigger"], `${where}.BuffTrigger`),
      buffType: asNumber(record["BuffType"], `${where}.BuffType`),
      buffSlot: asNumber(record["BuffSlot"], `${where}.BuffSlot`),
      buffTarget: asNumber(record["BuffTarget"], `${where}.BuffTarget`),
    };
  });
}

function parseSeasons(raw: JsonObject): Map<string, Map<string, TeamRecord[]>> {
  const seasons = new Map<string, Map<string, TeamRecord[]>>();
  for (const [seasonKey, seasonValue] of Object.entries(raw)) {
    if (!seasonKey.startsWith("Season")) continue;
    const modes = new Map<string, TeamRecord[]>();
    for (const [modeKey, modeValue] of Object.entries(asObject(seasonValue, seasonKey))) {
      const teams: TeamRecord[] = [];
      for (const [teamKey, teamValue] of Object.entries(asObject(modeValue, `${seasonKey}.${modeKey}`))) {
        const where = `${seasonKey}.${modeKey}.${teamKey}`;
        const team = asObject(teamValue, where);
        teams.push({
          id: teamKey,
          name: asString(team["TeamName"], `${where}.TeamName`),
          lineup: asNumberArray(team["Lineup"], `${where}.Lineup`),
          shirt: asNumber(team["Shirt"] ?? 1, `${where}.Shirt`),
          shirtPatternColor: asNumber(team["ShirtPatternColor"] ?? 0, `${where}.ShirtPatternColor`),
          shirtPatternStyle: asNumber(team["ShirtPatternStyle"] ?? 0, `${where}.ShirtPatternStyle`),
          pants: asNumber(team["Pants"] ?? 1, `${where}.Pants`),
          cashWin: asNumber(team["CashWin"] ?? 0, `${where}.CashWin`),
          cashDraw: asNumber(team["CashDraw"] ?? 0, `${where}.CashDraw`),
          cashLose: asNumber(team["CashLose"] ?? 0, `${where}.CashLose`),
        });
      }
      teams.sort((a, b) => Number(a.id) - Number(b.id));
      modes.set(modeKey, teams);
    }
    seasons.set(seasonKey, modes);
  }
  return seasons;
}

export function buildGameData(files: {
  playerData: JsonObject;
  dummyPlayerData: JsonObject;
  abilities: JsonObject;
  matchLineup: JsonObject;
  gameConfig: JsonObject;
}): GameData {
  const players = new Map<number, PlayerRecord>();
  for (const player of parsePlayers(files.playerData, 0, "PlayerData")) players.set(player.id, player);
  for (const player of parsePlayers(files.dummyPlayerData, DUMMY_PLAYER_OFFSET, "DummyPlayerData")) {
    players.set(player.id, player);
  }

  const abilities = new Map<number, AbilityRecord>();
  for (const ability of parseAbilities(files.abilities)) abilities.set(ability.id, ability);

  const tips = Array.isArray(files.gameConfig["Tips"])
    ? files.gameConfig["Tips"].map((tip, index) => asString(tip, `GameConfig.Tips[${index}]`))
    : [];

  return {
    players,
    abilities,
    seasons: parseSeasons(files.matchLineup),
    tips,
    initialRoster: asNumberArray(files.gameConfig["InitialRoster"], "GameConfig.InitialRoster"),
  };
}

async function fetchJson(url: string): Promise<JsonObject> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`failed to load ${url}: ${response.status}`);
  return asObject((await response.json()) as Json, url);
}

/** Loads the JSON produced by `tools/extract_assets.py`. */
export async function loadGameData(base = "data"): Promise<GameData> {
  const [playerData, dummyPlayerData, abilities, matchLineup, gameConfig] = await Promise.all([
    fetchJson(`${base}/PlayerData.json`),
    fetchJson(`${base}/DummyPlayerData.json`),
    fetchJson(`${base}/Abilities.json`),
    fetchJson(`${base}/MatchLineup.json`),
    fetchJson(`${base}/GameConfig.json`),
  ]);
  return buildGameData({ playerData, dummyPlayerData, abilities, matchLineup, gameConfig });
}
