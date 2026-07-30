class_name TeamRecord
extends RefCounted
## An opponent of a season's league or cup.

var id: int
var team_name: String
var lineup: Array[int] = []
var shirt: int
var shirt_pattern_colour: int
var shirt_pattern_style: int
var pants: int
var emblem: int
var sleeves: int
var cash_win: int
var cash_draw: int
var cash_lose: int
## The two entourage members the opponent brings, 0 for an empty slot.
var entourage: Array[int] = []
## PBonus: a flat bonus the opponent's rolls carry in the later seasons.
var bonus: int


static func from_dict(id_: int, raw: Dictionary) -> TeamRecord:
	var team := TeamRecord.new()
	team.id = id_
	team.team_name = String(raw.get("TeamName", "Opponent"))
	for player_id in raw.get("Lineup", []):
		team.lineup.append(int(player_id))
	team.shirt = int(raw.get("Shirt", 1))
	team.shirt_pattern_colour = int(raw.get("ShirtPatternColor", 0))
	team.shirt_pattern_style = int(raw.get("ShirtPatternStyle", 0))
	team.pants = int(raw.get("Pants", 1))
	team.emblem = int(raw.get("Emblem", 0))
	team.cash_win = int(raw.get("CashWin", 0))
	team.cash_draw = int(raw.get("CashDraw", 0))
	team.cash_lose = int(raw.get("CashLose", 0))
	team.sleeves = int(raw.get("SleevesSock", 1))
	team.bonus = int(raw.get("PBonus", 0))
	for member_id in raw.get("EntourageLineup", []):
		if int(member_id) > 0:
			team.entourage.append(int(member_id))
	return team
