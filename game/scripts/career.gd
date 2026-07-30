class_name Career
extends RefCounted
## Career progress: cash, squad, entourage, kit, achievements and the fixtures
## of the five seasons with their two cups.

const SAVE_PATH := "user://career.json"
const LINEUP_SIZE := 7
## "The bench can hold up to 4 reserve players." - the original handbook.
const BENCH_SIZE := 4
## "It looks like we can use 2 of them per match for some extra bonuses!"
const ENTOURAGE_SLOTS := 2
const MATCH_MINUTES := 15
const STARS_WIN := 3
const STARS_DRAW := 1
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
var owned_entourage: Array[int] = []
var entourage_slots: Array[int] = []
var owned_shirts: Array[int] = []
var owned_pants: Array[int] = []
var shirt := 1
var pants := 1
var results: Array[Dictionary] = []
var unlocked_seasons := 1
var unlocked_achievements: Array[int] = []
var wins := 0
var draws := 0
var losses := 0
var spent := 0
## "season:competition" -> fixtures won, so a cup run can be tracked and lost.
var progress: Dictionary = {}

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
	unlocked_achievements = []
	wins = 0
	draws = 0
	losses = 0
	spent = 0
	progress = {}
	lineup = _data.initial_roster.duplicate()
	owned = _data.initial_cards.duplicate()
	for id in lineup:
		if not id in owned:
			owned.append(id)
	bench = []
	owned_entourage = _data.initial_entourage.duplicate()
	entourage_slots = []
	if not owned_entourage.is_empty():
		entourage_slots.append(owned_entourage[0])
	owned_shirts = [1]
	owned_pants = [1]
	shirt = 1
	pants = 1


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
	return league_fixtures().size() * STARS_TO_QUALIFY_PER_MATCH


func league_fixtures() -> Array[TeamRecord]:
	var current := season()
	return current.teams(SeasonRecord.LEAGUE) if current != null else [] as Array[TeamRecord]


func qualified() -> bool:
	return points >= stars_needed()


func can_play() -> bool:
	return next_opponent() != null and lineup_is_valid() and not cup_eliminated()


## A cup is a knockout: the original unlocks the next match only after a win.
func cup_eliminated() -> bool:
	if competition == SeasonRecord.LEAGUE:
		return false
	return int(progress.get(_progress_key(), 0)) < fixture_index


func cup_won() -> bool:
	if competition == SeasonRecord.LEAGUE:
		return false
	return int(progress.get(_progress_key(), 0)) >= fixtures().size()


func lineup_is_valid() -> bool:
	if lineup.size() != LINEUP_SIZE:
		return false
	for slot in LINEUP_SIZE:
		var player := _data.player(lineup[slot])
		if player == null or not player.fits_slot(slot):
			return false
	return true


func entourage_members() -> Array[EntourageRecord]:
	var members: Array[EntourageRecord] = []
	for id in entourage_slots:
		var member := _data.entourage_member(id)
		if member != null:
			members.append(member)
	return members


func equip_entourage(id: int) -> bool:
	if not id in owned_entourage or id in entourage_slots:
		return false
	var member := _data.entourage_member(id)
	if member == null or cash < member.minimum_cash:
		return false
	if entourage_slots.size() >= ENTOURAGE_SLOTS:
		entourage_slots.remove_at(0)
	entourage_slots.append(id)
	save()
	return true


func unequip_entourage(id: int) -> void:
	entourage_slots.erase(id)
	save()


func buy_entourage(id: int) -> bool:
	var member := _data.entourage_member(id)
	if member == null or id in owned_entourage or cash < member.cost:
		return false
	_spend(member.cost)
	owned_entourage.append(id)
	save()
	return true


func buy_kit(kit_id: int, price: int, is_shirt: bool) -> bool:
	var wardrobe := owned_shirts if is_shirt else owned_pants
	if kit_id in wardrobe or cash < price:
		return false
	_spend(price)
	wardrobe.append(kit_id)
	if is_shirt:
		shirt = kit_id
	else:
		pants = kit_id
	save()
	return true


## The original sold most of the wardrobe as in-app purchases; with no store to
## talk to, any kit the bundle describes may be worn.
func wear(kit_id: int, is_shirt: bool) -> bool:
	if is_shirt and _data.kit(kit_id) != null:
		shirt = kit_id
		if not kit_id in owned_shirts:
			owned_shirts.append(kit_id)
	elif not is_shirt and kit_id > 0:
		pants = kit_id
		if not kit_id in owned_pants:
			owned_pants.append(kit_id)
	else:
		return false
	save()
	return true


## Records a played match and returns the payout plus the achievements it won.
## ``stats`` carries what only the match knows: best roll, late events, and so on.
func record_result(goals_home: int, goals_away: int, stats: Dictionary = {}) -> Dictionary:
	var opponent := next_opponent()
	if opponent == null:
		return {"payout": 0, "stars": 0, "achievements": [] as Array[AchievementRecord]}
	var won := goals_home > goals_away
	var drawn := goals_home == goals_away
	var payout := opponent.cash_draw
	var stars := STARS_DRAW
	if won:
		payout = opponent.cash_win
		stars = STARS_WIN
		wins += 1
	elif drawn:
		draws += 1
	else:
		payout = opponent.cash_lose
		stars = 0
		losses += 1
	payout += _entourage_payout(won, drawn, opponent)
	cash += payout
	points += stars
	goals_for += goals_home
	goals_against += goals_away
	if won or competition == SeasonRecord.LEAGUE:
		progress[_progress_key()] = int(progress.get(_progress_key(), 0)) + 1
	results.append({
		"season": season_index,
		"competition": competition,
		"opponent": opponent.team_name,
		"goals_for": goals_home,
		"goals_against": goals_away,
		"stars": stars,
		"cash": payout,
	})
	fixture_index += 1
	if season_finished() and qualified():
		unlocked_seasons = maxi(unlocked_seasons, season_index + 2)
	var unlocked := _check_achievements(goals_home, goals_away, won, stats)
	save()
	return {"payout": payout, "stars": stars, "achievements": unlocked}


## Moves on to the next season once its fixtures are done and the stars are in.
func advance_season() -> bool:
	if not (season_finished() and qualified()):
		return false
	if season_index + 1 >= _data.seasons.size():
		return false
	season_index += 1
	competition = SeasonRecord.LEAGUE
	fixture_index = 0
	points = 0
	goals_for = 0
	goals_against = 0
	save()
	return true


## Switching competition resumes that competition where it was left.
func select_competition(name: String) -> void:
	if season() == null or not season().has_competition(name):
		return
	competition = name
	fixture_index = _played_in(name)
	save()


func stars_of(fixture: int) -> int:
	for entry in results:
		if int(entry["season"]) != season_index or String(entry["competition"]) != competition:
			continue
		if _index_of(entry) == fixture:
			return int(entry.get("stars", 0))
	return -1


func owns(player_id: int) -> bool:
	return player_id in owned


func can_buy(player_id: int) -> bool:
	var player := _data.player(player_id)
	return player != null and not owns(player_id) and cash >= player.cost


func buy(player_id: int) -> bool:
	if not can_buy(player_id):
		return false
	_spend(_data.player(player_id).cost)
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


func bench_players() -> Array[PlayerRecord]:
	return _data.lineup_players(bench)


func achievement_unlocked(id: int) -> bool:
	return id in unlocked_achievements


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
		"owned_entourage": owned_entourage,
		"entourage_slots": entourage_slots,
		"owned_shirts": owned_shirts,
		"owned_pants": owned_pants,
		"shirt": shirt,
		"pants": pants,
		"results": results,
		"unlocked_seasons": unlocked_seasons,
		"unlocked_achievements": unlocked_achievements,
		"wins": wins,
		"draws": draws,
		"losses": losses,
		"spent": spent,
		"progress": progress,
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
	owned_entourage = _to_int_array(raw.get("owned_entourage", []))
	entourage_slots = _to_int_array(raw.get("entourage_slots", []))
	owned_shirts = _to_int_array(raw.get("owned_shirts", [1]))
	owned_pants = _to_int_array(raw.get("owned_pants", [1]))
	shirt = int(raw.get("shirt", 1))
	pants = int(raw.get("pants", 1))
	unlocked_achievements = _to_int_array(raw.get("unlocked_achievements", []))
	wins = int(raw.get("wins", 0))
	draws = int(raw.get("draws", 0))
	losses = int(raw.get("losses", 0))
	spent = int(raw.get("spent", 0))
	progress = raw.get("progress", {})
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


func _spend(amount: int) -> void:
	cash -= amount
	spent += amount


func _progress_key() -> String:
	return "%d:%s" % [season_index, competition]


func _played_in(name: String) -> int:
	var played := 0
	for entry in results:
		if int(entry["season"]) == season_index and String(entry["competition"]) == name:
			played += 1
	return played


func _index_of(entry: Dictionary) -> int:
	var index := 0
	for other in results:
		if other == entry:
			return index
		if (
			int(other["season"]) == season_index
			and String(other["competition"]) == String(entry["competition"])
		):
			index += 1
	return -1


## The paid-for members change what a result is worth, win bonus and bets alike.
func _entourage_payout(won: bool, drawn: bool, opponent: TeamRecord) -> int:
	var extra := 0
	for member in entourage_members():
		if won:
			extra += member.win_bonus + member.wager
		elif drawn and member.draw_pays_win:
			extra += opponent.cash_win - opponent.cash_draw
			extra -= member.wager
		else:
			extra -= member.wager
	return extra


## Runs every achievement condition against the career and the match just played.
func _check_achievements(
	goals_home: int, goals_away: int, won: bool, stats: Dictionary
) -> Array[AchievementRecord]:
	var unlocked: Array[AchievementRecord] = []
	var ids: Array[int] = []
	for id: int in _data.achievements:
		ids.append(id)
	ids.sort()
	for id in ids:
		if achievement_unlocked(id):
			continue
		var record: AchievementRecord = _data.achievements[id]
		if not _condition_met(record, goals_home, goals_away, won, stats):
			continue
		unlocked_achievements.append(id)
		_grant(record)
		unlocked.append(record)
	return unlocked


func _condition_met(
	record: AchievementRecord, goals_home: int, goals_away: int, won: bool, stats: Dictionary
) -> bool:
	var late := int(stats.get("last_event_seconds", 3600)) < 60
	match record.kind:
		AchievementRecord.Kind.PLAY_ALL:
			return _completed_all(record.amount, SeasonRecord.LEAGUE, false)
		AchievementRecord.Kind.WIN_ALL_LEAGUE:
			return _completed_all(record.amount, SeasonRecord.LEAGUE, true)
		AchievementRecord.Kind.WIN_ALL_LEAGUES:
			for index in _data.seasons.size():
				if not _completed_all(index, SeasonRecord.LEAGUE, true):
					return false
			return true
		AchievementRecord.Kind.WIN_CUP:
			return _completed_all(record.amount, record.detail, true)
		AchievementRecord.Kind.WIN_ALL_CUPS:
			for index in _data.seasons.size():
				for cup in SeasonRecord.CUPS:
					var current: SeasonRecord = _data.season(index)
					if current != null and current.has_competition(cup):
						if not _completed_all(index, cup, true):
							return false
			return true
		AchievementRecord.Kind.QUALIFY:
			return unlocked_seasons > record.amount
		AchievementRecord.Kind.WINS:
			return wins >= record.amount
		AchievementRecord.Kind.DRAWS:
			return draws >= record.amount
		AchievementRecord.Kind.LOSSES:
			return losses >= record.amount
		AchievementRecord.Kind.UNLOCK_POSITION:
			return _owns_position(record.detail)
		AchievementRecord.Kind.UNLOCK_ALL_ENTOURAGE:
			return owned_entourage.size() >= _data.entourage.size()
		AchievementRecord.Kind.HOARD:
			return cash >= record.amount
		AchievementRecord.Kind.SPEND:
			return spent >= record.amount
		AchievementRecord.Kind.CLEAN_SHEET:
			return goals_away == 0
		AchievementRecord.Kind.CONCEDE:
			return goals_away >= record.amount
		AchievementRecord.Kind.SCORE:
			return goals_home >= record.amount
		AchievementRecord.Kind.ROLL:
			return int(stats.get("best_roll", 0)) >= record.amount
		AchievementRecord.Kind.ROLL_ZERO:
			return bool(stats.get("rolled_zero", false))
		AchievementRecord.Kind.ENEMY_ROLL_ZERO:
			return bool(stats.get("enemy_rolled_zero", false))
		AchievementRecord.Kind.LATE_GOAL:
			return late and bool(stats.get("late_goal", false))
		AchievementRecord.Kind.LATE_MISS:
			return late and bool(stats.get("late_miss", false))
		AchievementRecord.Kind.LATE_SAVE:
			return late and bool(stats.get("late_save", false))
		AchievementRecord.Kind.LATE_CONCEDE:
			return late and bool(stats.get("late_concede", false))
		AchievementRecord.Kind.LATE_EQUALISER:
			return late and bool(stats.get("late_equaliser", false))
		AchievementRecord.Kind.LATE_WINNER:
			return late and bool(stats.get("late_winner", false))
		AchievementRecord.Kind.WIN_WITH_WAG:
			if not won:
				return false
			for member in entourage_members():
				if member.position == "WAG":
					return true
			return false
		AchievementRecord.Kind.WIN_EMPTY_BENCH:
			return won and bench.is_empty() and season_index >= 3
		AchievementRecord.Kind.WIN_NO_ENTOURAGE:
			return won and entourage_slots.is_empty() and season_index >= 3
	return false


## True when every fixture of that competition was played, optionally all won.
func _completed_all(index: int, name: String, must_win: bool) -> bool:
	var current: SeasonRecord = _data.season(index)
	if current == null or not current.has_competition(name):
		return false
	var needed := current.teams(name).size()
	var played := 0
	for entry in results:
		if int(entry["season"]) != index or String(entry["competition"]) != name:
			continue
		if must_win and int(entry["goals_for"]) <= int(entry["goals_against"]):
			return false
		played += 1
	return played >= needed


func _owns_position(position: String) -> bool:
	if position.is_empty():
		return false
	for id: int in _data.players:
		var record: PlayerRecord = _data.players[id]
		if record.position == position and not owns(id):
			return false
	return true


func _grant(record: AchievementRecord) -> void:
	cash += record.gold_reward
	if record.player_reward > 0 and not owns(record.player_reward):
		owned.append(record.player_reward)
	if record.entourage_reward > 0 and not record.entourage_reward in owned_entourage:
		owned_entourage.append(record.entourage_reward)
	if record.shirt_reward > 0 and not record.shirt_reward in owned_shirts:
		owned_shirts.append(record.shirt_reward)
	if record.pants_reward > 0 and not record.pants_reward in owned_pants:
		owned_pants.append(record.pants_reward)


static func _to_int_array(values: Array) -> Array[int]:
	var ints: Array[int] = []
	for value in values:
		ints.append(int(value))
	return ints
