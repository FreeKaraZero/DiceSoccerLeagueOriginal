class_name GameData
extends RefCounted
## Everything the original game shipped as data, read from the JSON that
## tools/extract_assets.py writes out of the iOS bundle.

const DATA_DIR := "res://assets/data"
## DummyPlayerData holds the unlockable "premium" cards; the original offsets
## their ids by 1000 so both tables can share one id space.
const DUMMY_PLAYER_OFFSET := 1000

var players: Dictionary = {}  ## int -> PlayerRecord
var abilities: Dictionary = {}  ## int -> AbilityRecord
var seasons: Array[SeasonRecord] = []
var initial_roster: Array[int] = []
var tips: Array[String] = []


static func load_default() -> GameData:
	var data := GameData.new()
	data._load_players("PlayerData.json", 0)
	data._load_players("DummyPlayerData.json", DUMMY_PLAYER_OFFSET)
	data._load_abilities()
	data._load_seasons()
	data._load_config()
	return data


static func read_json(file_name: String) -> Dictionary:
	var path := DATA_DIR.path_join(file_name)
	if not FileAccess.file_exists(path):
		push_error("missing extracted data file %s - run tools/extract_assets.py" % path)
		return {}
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if parsed is Dictionary else {}


func is_loaded() -> bool:
	return not players.is_empty() and not seasons.is_empty()


func player(id: int) -> PlayerRecord:
	return players.get(id)


func ability(id: int) -> AbilityRecord:
	return abilities.get(id)


func season(index: int) -> SeasonRecord:
	return seasons[clampi(index, 0, seasons.size() - 1)] if not seasons.is_empty() else null


func lineup_players(ids: Array[int]) -> Array[PlayerRecord]:
	var lineup: Array[PlayerRecord] = []
	for id in ids:
		var record := player(id)
		if record != null:
			lineup.append(record)
	return lineup


## Players the shop can sell, cheapest first.
func buyable_players() -> Array[PlayerRecord]:
	var buyable: Array[PlayerRecord] = []
	for id: int in players:
		var record: PlayerRecord = players[id]
		if record.cost > 0 and id < DUMMY_PLAYER_OFFSET:
			buyable.append(record)
	buyable.sort_custom(func(a: PlayerRecord, b: PlayerRecord) -> bool: return a.cost < b.cost)
	return buyable


func _load_players(file_name: String, offset: int) -> void:
	var table := read_json(file_name)
	for key: String in table:
		if not key.is_valid_int():
			continue  # bookkeeping keys such as LastPlayerID
		var raw: Dictionary = table[key]
		if not raw.has("Position"):
			continue  # entry 0 is the referee die: faces but no position or name
		var id := offset + int(key)
		players[id] = PlayerRecord.from_dict(id, raw)


func _load_abilities() -> void:
	var raw: Dictionary = read_json("Abilities.json").get("Abilities", {})
	for key: String in raw:
		abilities[int(key)] = AbilityRecord.from_dict(int(key), raw[key])


func _load_seasons() -> void:
	var raw := read_json("MatchLineup.json")
	var index := 0
	while raw.has("Season%d" % index):
		seasons.append(SeasonRecord.from_dict(index, raw["Season%d" % index]))
		index += 1


func _load_config() -> void:
	var raw := read_json("GameConfig.json")
	for id in raw.get("InitialRoster", []):
		initial_roster.append(int(id))
	for tip in raw.get("Tips", []):
		tips.append(String(tip))
