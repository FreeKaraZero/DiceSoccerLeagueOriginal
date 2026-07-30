class_name AchievementRecord
extends RefCounted
## One of the 72 achievements. The bundle only describes the condition in
## English ("Win All Matches in Season 2 League"), so it is parsed into the
## shape the career can check after every match.

enum Kind {
	MANUAL,  ## Credits and multiplayer, which this remake has no flow for yet.
	PLAY_ALL,
	WIN_ALL_LEAGUE,
	WIN_ALL_LEAGUES,
	WIN_CUP,
	WIN_ALL_CUPS,
	QUALIFY,
	WINS,
	DRAWS,
	LOSSES,
	UNLOCK_POSITION,
	UNLOCK_ALL_ENTOURAGE,
	HOARD,
	SPEND,
	CLEAN_SHEET,
	CONCEDE,
	SCORE,
	ROLL,
	ROLL_ZERO,
	ENEMY_ROLL_ZERO,
	LATE_GOAL,
	LATE_MISS,
	LATE_SAVE,
	LATE_CONCEDE,
	LATE_EQUALISER,
	LATE_WINNER,
	WIN_WITH_WAG,
	WIN_EMPTY_BENCH,
	WIN_NO_ENTOURAGE,
}

var id: int
var achievement_name: String
var locked_text: String
var unlocked_text: String
var condition_text: String
var popup_lines: Array[String] = []
var player_reward: int
var entourage_reward: int
var shirt_reward: int
var pants_reward: int
var gold_reward: int
var kind: int = Kind.MANUAL
var amount := 0
var detail := ""  ## Season index or position the condition speaks about.


static func from_dict(id_: int, raw: Dictionary) -> AchievementRecord:
	var record := AchievementRecord.new()
	record.id = id_
	record.achievement_name = String(raw.get("Name", "Achievement"))
	record.locked_text = String(raw.get("LockedDescription", ""))
	record.unlocked_text = String(raw.get("UnlockedDescription", ""))
	record.condition_text = String(raw.get("InHouseDescription", ""))
	for line in raw.get("PopupText", []):
		record.popup_lines.append(String(line))
	record.player_reward = int(raw.get("PlayerReward", 0))
	record.entourage_reward = int(raw.get("EntourageReward", 0))
	record.shirt_reward = int(raw.get("ShirtReward", 0))
	record.pants_reward = int(raw.get("PantsReward", 0))
	record.gold_reward = int(raw.get("GoldReward", 0))
	record._read_condition()
	return record


func has_reward() -> bool:
	return (
		player_reward > 0
		or entourage_reward > 0
		or shirt_reward > 0
		or pants_reward > 0
		or gold_reward > 0
	)


func _read_condition() -> void:
	var text := condition_text.to_lower()
	var number := _first_number(text)
	# "Training Season" is season 0 in the data; "Season N League" is season N.
	var season := 0 if text.contains("training") else maxi(_season_number(text), 0)
	if text.contains("multiplayer") or text.contains("credits"):
		kind = Kind.MANUAL
	elif text.contains("white bordered") or text.contains("tier"):
		kind = Kind.MANUAL  ## The card tiers are not part of the extracted data.
	elif text.contains("play all matches"):
		kind = Kind.PLAY_ALL
		amount = season
	elif text.contains("win all league matches"):
		kind = Kind.WIN_ALL_LEAGUES
	elif text.contains("win all") and (text.contains("league") or text.contains("matches in")):
		kind = Kind.WIN_ALL_LEAGUE
		amount = season
	elif text.contains("win all") and text.contains("tournaments"):
		kind = Kind.WIN_ALL_CUPS
	elif text.begins_with("win season") or text.contains("win t"):
		kind = Kind.WIN_CUP
		amount = season
		detail = SeasonRecord.CUPS[_cup_number(text) - 1]
	elif text.contains("qualify for season"):
		kind = Kind.QUALIFY
		amount = maxi(number - 1, 1)  ## "Season 2" is index 1.
	elif text.contains("win") and text.contains("wag"):
		kind = Kind.WIN_WITH_WAG
	elif text.contains("empty bench"):
		kind = Kind.WIN_EMPTY_BENCH
	elif text.contains("assistance slots empty"):
		kind = Kind.WIN_NO_ENTOURAGE
	elif text.contains("win") and text.contains("matches"):
		kind = Kind.WINS
		amount = number
	elif text.contains("draw") and text.contains("matches"):
		kind = Kind.DRAWS
		amount = number
	elif text.contains("lose") and text.contains("matches"):
		kind = Kind.LOSSES
		amount = number
	elif text.contains("unlock all") and text.contains("entourage"):
		kind = Kind.UNLOCK_ALL_ENTOURAGE
	elif text.contains("unlocked all entourage"):
		kind = Kind.UNLOCK_ALL_ENTOURAGE
	elif text.contains("unlock all"):
		kind = Kind.UNLOCK_POSITION
		detail = _position(text)
	elif text.contains("hoard"):
		kind = Kind.HOARD
		amount = number
	elif text.contains("spend"):
		kind = Kind.SPEND
		amount = number
	elif text.contains("concede 0"):
		kind = Kind.CLEAN_SHEET
	elif text.contains("concede a goal"):
		kind = Kind.LATE_CONCEDE
	elif text.contains("concede"):
		kind = Kind.CONCEDE
		amount = number
	elif text.contains("equaliser"):
		kind = Kind.LATE_EQUALISER
	elif text.contains("tie breaker"):
		kind = Kind.LATE_WINNER
	elif text.contains("score a goal"):
		kind = Kind.LATE_GOAL
	elif text.contains("missed a goal"):
		kind = Kind.LATE_MISS
	elif text.contains("save a goal"):
		kind = Kind.LATE_SAVE
	elif text.contains("score"):
		kind = Kind.SCORE
		amount = number
	elif text.contains("enemy rolls 0"):
		kind = Kind.ENEMY_ROLL_ZERO
	elif text.contains("roll 0"):
		kind = Kind.ROLL_ZERO
	elif text.contains("roll"):
		kind = Kind.ROLL
		amount = number


static func _first_number(text: String) -> int:
	var found := RegEx.create_from_string("([0-9][0-9,]*)").search(text)
	return int(found.get_string(1).replace(",", "")) if found != null else 0


## "Win All Matches in Season 2 League" -> index 2, the data's Season2.
static func _season_number(text: String) -> int:
	var found := RegEx.create_from_string("(?:season|t|league)\\s*([0-9])").search(text)
	return int(found.get_string(1)) if found != null else -1


## "Win Season 2 Tournament 1" and "Win T4.2" both name which of the two cups.
static func _cup_number(text: String) -> int:
	var found := RegEx.create_from_string("(?:tournament|t[0-9]\\.)\\s*([12])").search(text)
	return int(found.get_string(1)) if found != null else 1


static func _position(text: String) -> String:
	if text.contains("goalkeeper"):
		return PlayerRecord.GK
	if text.contains("forward"):
		return PlayerRecord.FWD
	if text.contains("midfielder"):
		return PlayerRecord.MID
	if text.contains("defender"):
		return PlayerRecord.DEF
	return ""
