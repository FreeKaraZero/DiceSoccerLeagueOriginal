class_name Career
extends RefCounted
## Career progress: cash, squad, fixtures and the five seasons.

const SAVE_PATH := "user://career.json"
const LINEUP_SIZE := 7
const BENCH_SIZE := 5
const MATCH_MINUTES := 15
const POINTS_WIN := 3
const POINTS_DRAW := 1
## Stars needed per fixture to qualify for the next season: two thirds of a win.
const STARS_TO_QUALIFY_PER_MATCH := 2

var cash := 0
var season_index := 0
var competition := SeasonRecord.LEAGUE
var fixture_index := 0
var points := 0
var goals_for := 0
var goals_against := 0
var owned: Array[int] = []
var lineup: Array[int] = []
var bench: Array[int] = []
var results: Array[Dictionary] = []
var unlocked_seasons := 1

var _data: GameData


func _init(data: GameData) -> void:
	_data = data
	reset()


func reset() -> void:
	cash = 0
	season_index = 0
	competition = SeasonRecord.LEAGUE
	fixture_index = 0
	points = 0
	goals_for = 0
	goals_against = 0
	results = []
	unlocked_seasons = 1
	lineup = _data.initial_roster.duplicate()
	owned = lineup.duplicate()
	bench = []


func season() -> SeasonRecord:
	return _data.season(season_index)


func fixtures() -> Array[TeamRecord]:
	var current := season()
	return current.teams(competition) if current != null else [] as Array[TeamRecord]


func next_opponent() -> TeamRecord:
	var list := fixtures()
	return list[fixture_index] if fixture_index < list.size() else null


func season_finished() -> bool:
	return fixture_index >= fixtures().size()


func stars_needed() -> int:
	return fixtures().size() * STARS_TO_QUALIFY_PER_MATCH


func qualified() -> bool:
	return points >= stars_needed()


func can_play() -> bool:
	return next_opponent() != null and lineup_is_valid()


func lineup_is_valid() -> bool:
	if lineup.size() != LINEUP_SIZE:
		return false
	for slot in LINEUP_SIZE:
		var player := _data.player(lineup[slot])
		if player == null or not player.fits_slot(slot):
			return false
	return true


## Records the result of a played match and returns the cash it paid out.
func record_result(goals_home: int, goals_away: int) -> int:
	var opponent := next_opponent()
	if opponent == null:
		return 0
	var payout := opponent.cash_draw
	var earned_points := POINTS_DRAW
	if goals_home > goals_away:
		payout = opponent.cash_win
		earned_points = POINTS_WIN
	elif goals_home < goals_away:
		payout = opponent.cash_lose
		earned_points = 0
	cash += payout
	points += earned_points
	goals_for += goals_home
	goals_against += goals_away
	results.append({
		"season": season_index,
		"competition": competition,
		"opponent": opponent.team_name,
		"goals_for": goals_home,
		"goals_against": goals_away,
		"points": earned_points,
		"cash": payout,
	})
	fixture_index += 1
	if season_finished() and qualified():
		unlocked_seasons = maxi(unlocked_seasons, season_index + 2)
	save()
	return payout


## Moves on to the next season once its fixtures are done and the stars are in.
func advance_season() -> bool:
	if not (season_finished() and qualified()):
		return false
	if season_index + 1 >= _data.seasons.size():
		return false
	season_index += 1
	fixture_index = 0
	points = 0
	goals_for = 0
	goals_against = 0
	save()
	return true


func select_competition(name: String) -> void:
	if season() != null and season().has_competition(name):
		competition = name
		fixture_index = 0
		points = 0
		save()


func owns(player_id: int) -> bool:
	return player_id in owned


func can_buy(player_id: int) -> bool:
	var player := _data.player(player_id)
	return player != null and not owns(player_id) and cash >= player.cost


func buy(player_id: int) -> bool:
	if not can_buy(player_id):
		return false
	cash -= _data.player(player_id).cost
	owned.append(player_id)
	if bench.size() < BENCH_SIZE:
		bench.append(player_id)
	save()
	return true


func sell(player_id: int) -> bool:
	if player_id in lineup or not owns(player_id):
		return false
	owned.erase(player_id)
	bench.erase(player_id)
	cash += int(_data.player(player_id).cost / 2.0)
	save()
	return true


## Puts a player in a starting slot, swapping with whoever was there.
func assign_to_slot(slot: int, player_id: int) -> bool:
	var player := _data.player(player_id)
	if player == null or slot < 0 or slot >= LINEUP_SIZE or not owns(player_id):
		return false
	if not player.fits_slot(slot):
		return false
	var previous := lineup[slot]
	var existing_slot := lineup.find(player_id)
	if existing_slot != -1:
		var previous_player := _data.player(previous)
		if previous_player == null or not previous_player.fits_slot(existing_slot):
			return false
		lineup[existing_slot] = previous
	else:
		bench.erase(player_id)
		if previous != 0 and bench.size() < BENCH_SIZE:
			bench.append(previous)
	lineup[slot] = player_id
	save()
	return true


func squad_players() -> Array[PlayerRecord]:
	return _data.lineup_players(owned)


func to_dict() -> Dictionary:
	return {
		"cash": cash,
		"season_index": season_index,
		"competition": competition,
		"fixture_index": fixture_index,
		"points": points,
		"goals_for": goals_for,
		"goals_against": goals_against,
		"owned": owned,
		"lineup": lineup,
		"bench": bench,
		"results": results,
		"unlocked_seasons": unlocked_seasons,
	}


func save() -> void:
	var file := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(to_dict(), "\t"))


func load_saved() -> bool:
	if not FileAccess.file_exists(SAVE_PATH):
		return false
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(SAVE_PATH))
	if not parsed is Dictionary:
		return false
	var raw: Dictionary = parsed
	cash = int(raw.get("cash", 0))
	season_index = int(raw.get("season_index", 0))
	competition = String(raw.get("competition", SeasonRecord.LEAGUE))
	fixture_index = int(raw.get("fixture_index", 0))
	points = int(raw.get("points", 0))
	goals_for = int(raw.get("goals_for", 0))
	goals_against = int(raw.get("goals_against", 0))
	unlocked_seasons = int(raw.get("unlocked_seasons", 1))
	owned = _to_int_array(raw.get("owned", []))
	lineup = _to_int_array(raw.get("lineup", []))
	bench = _to_int_array(raw.get("bench", []))
	results = []
	for entry in raw.get("results", []):
		results.append(entry)
	if not lineup_is_valid():
		lineup = _data.initial_roster.duplicate()
	return true


func delete_save() -> void:
	if FileAccess.file_exists(SAVE_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH))
	reset()


static func _to_int_array(values: Array) -> Array[int]:
	var ints: Array[int] = []
	for value in values:
		ints.append(int(value))
	return ints
