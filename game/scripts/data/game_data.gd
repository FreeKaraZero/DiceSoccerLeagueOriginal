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
var entourage: Dictionary = {}  ## int -> EntourageRecord
var achievements: Dictionary = {}  ## int -> AchievementRecord
var kits: Dictionary = {}  ## int -> KitRecord
var colours: Dictionary = {}  ## int -> Color
var seasons: Array[SeasonRecord] = []
var initial_roster: Array[int] = []
var initial_cards: Array[int] = []
var initial_entourage: Array[int] = []
var cash_packs: Array[Dictionary] = []
var tips: Array[String] = []
## Entry 0 of PlayerData: the die the referee rolls when Dive! is played.
var referee: PlayerRecord


static func load_default() -> GameData:
	var data := GameData.new()
	data._load_players("PlayerData.json", 0)
	data._load_players("DummyPlayerData.json", DUMMY_PLAYER_OFFSET)
	data._load_abilities()
	data._load_entourage()
	data._load_achievements()
	data._load_kits()
	data._load_seasons()
	data._load_shop()
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


func entourage_member(id: int) -> EntourageRecord:
	return entourage.get(id)


func achievement(id: int) -> AchievementRecord:
	return achievements.get(id)


func kit(id: int) -> KitRecord:
	return kits.get(id)


func colour(id: int) -> Color:
	return colours.get(id, Color.WHITE)


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
		var id := offset + int(key)
		if not raw.has("Position"):
			# Entry 0 is the referee die: faces but no position or name.
			if id == 0:
				referee = PlayerRecord.from_dict(0, raw)
				referee.player_name = "Referee"
			continue
		players[id] = PlayerRecord.from_dict(id, raw)


func _load_abilities() -> void:
	var raw: Dictionary = read_json("Abilities.json").get("Abilities", {})
	for key: String in raw:
		abilities[int(key)] = AbilityRecord.from_dict(int(key), raw[key])


func _load_entourage() -> void:
	var raw := read_json("EntourageData.json")
	for key: String in raw:
		if key.is_valid_int():
			entourage[int(key)] = EntourageRecord.from_dict(int(key), raw[key])


func _load_achievements() -> void:
	var raw := read_json("Achievements.json")
	for key: String in raw:
		if key.is_valid_int():
			achievements[int(key)] = AchievementRecord.from_dict(int(key), raw[key])


func _load_kits() -> void:
	var raw := read_json("Shirt.json")
	for key: String in raw:
		if key.is_valid_int():
			kits[int(key)] = KitRecord.from_dict(int(key), raw[key])
	var palette := read_json("Colour.json")
	for key: String in palette:
		if not key.is_valid_int():
			continue
		var channels := String(palette[key]).split(",")
		if channels.size() == 3:
			colours[int(key)] = Color8(
				int(channels[0]), int(channels[1]), int(channels[2])
			)


func _load_shop() -> void:
	for entry in read_json("Shop.json").get("Cash", []):
		var pack: Dictionary = entry
		if pack.has("Comments"):
			continue  ## The first row is marked "Ignore this" in the original data.
		cash_packs.append({
			"price": float(pack.get("InAppPurchasePrice", 0.0)),
			"coins": int(pack.get("InGameCoinAmount", 0)),
		})


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
	# Status 2 marks what the player starts with, 0 what has to be bought.
	for index in raw.get("InitialCardStatus", []).size():
		if int(raw["InitialCardStatus"][index]) > 0:
			initial_cards.append(index)
	for index in raw.get("InitialEntourageStatus", []).size():
		if int(raw["InitialEntourageStatus"][index]) > 0:
			initial_entourage.append(index + 1)  ## The list is ordered by member id.
