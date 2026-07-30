class_name MatchSim
extends RefCounted
## One Dice Soccer match: the attacking trio rolls against the defending trio,
## the winner pushes the ball one zone forward, and a win inside the goal zone
## becomes a shot of the central forward against the goalkeeper. Every roll,
## re-rolls included, burns about a minute of the clock.

const GOALKEEPER_SLOT := 0
const DEFENCE_SLOTS := [1, 2, 3]
const ATTACK_SLOTS := [4, 5, 6]
const CENTRAL_ATTACK_SLOT := 5
const BALL_ZONES := 5
const CENTRE_ZONE := 2
const SECONDS_PER_ROLL := 60
const DEFAULT_MATCH_SECONDS := 15 * 60
## The six sides of the referee die, as PlayerData annotates entry 0.
const REFEREE_CALLS := [
	"offside", "offside", "yellow card", "red card", "blank", "blank"
]

enum Side { HOME, AWAY }

var home_name: String
var away_name: String
var home_lineup: Array[PlayerRecord]
var away_lineup: Array[PlayerRecord]
var bench: Array[PlayerRecord] = []
var score := {Side.HOME: 0, Side.AWAY: 0}
var possession: int = Side.HOME
var ball_zone := CENTRE_ZONE
var seconds_remaining := DEFAULT_MATCH_SECONDS
var encounter: Encounter
var substitutions := 0
## What the achievements need to know about the match once it is over.
var stats := {
	"best_roll": 0,
	"rolled_zero": false,
	"enemy_rolled_zero": false,
	"late_goal": false,
	"late_miss": false,
	"late_save": false,
	"late_concede": false,
	"late_equaliser": false,
	"late_winner": false,
	"last_event_seconds": 1 << 30,
}

var _abilities: Dictionary
var _entourage := {Side.HOME: [], Side.AWAY: []}
var _bonus := {Side.HOME: 0, Side.AWAY: 0}
var _rng := RandomNumberGenerator.new()
var _referee: PlayerRecord


func _init(
	home: Array[PlayerRecord],
	away: Array[PlayerRecord],
	abilities: Dictionary,
	options: Dictionary = {}
) -> void:
	home_lineup = home
	away_lineup = away
	_abilities = abilities
	home_name = String(options.get("home_name", "Your Team"))
	away_name = String(options.get("away_name", "Opponent"))
	seconds_remaining = int(options.get("match_seconds", DEFAULT_MATCH_SECONDS))
	possession = int(options.get("kickoff", Side.HOME))
	_referee = options.get("referee")
	bench.assign(options.get("bench", []))
	if options.has("entourage"):
		var members: Dictionary = options["entourage"]
		for side: int in members:
			_entourage[side] = members[side]
	if options.has("bonus"):
		var bonuses: Dictionary = options["bonus"]
		for side: int in bonuses:
			_bonus[side] = int(bonuses[side])
	if options.has("seed"):
		_rng.seed = int(options["seed"])
	else:
		_rng.randomize()


func is_finished() -> bool:
	return seconds_remaining <= 0


func clock_text() -> String:
	var seconds := maxi(seconds_remaining, 0)
	return "%d:%02d" % [seconds / 60, seconds % 60]


func winner() -> Variant:
	if score[Side.HOME] == score[Side.AWAY]:
		return null
	return Side.HOME if score[Side.HOME] > score[Side.AWAY] else Side.AWAY


## Slots that roll for the side this encounter: the attackers of whoever holds
## the ball against the defenders of the other one, or the central forward
## against the goalkeeper once the ball is on the goal line.
func rolling_slots(side: int) -> Array:
	if _shot_pending():
		return [CENTRAL_ATTACK_SLOT] if side == possession else [GOALKEEPER_SLOT]
	return ATTACK_SLOTS if side == possession else DEFENCE_SLOTS


## Rolls the next encounter and hands it back for the coach to play.
func start_encounter() -> Encounter:
	if is_finished():
		return null
	var shot := _shot_pending()
	encounter = Encounter.new(
		possession,
		_trio(Side.HOME),
		_trio(Side.AWAY),
		_abilities,
		_rng,
		{
			"shot": shot,
			"player_side": Side.HOME,
			"entourage": _entourage,
			"bonus": _bonus,
			"context": {
				"seconds_remaining": seconds_remaining,
				"goals_for": score[Side.HOME],
				"goals_against": score[Side.AWAY],
				"bench_empty": bench.is_empty(),
			},
		}
	)
	encounter.run_opponent()
	return encounter


## Ends the encounter and applies its outcome to the match.
func settle() -> Dictionary:
	if encounter == null:
		return {}
	encounter.play_on()
	seconds_remaining = maxi(seconds_remaining - encounter.rolls_used * SECONDS_PER_ROLL, 0)
	seconds_remaining += encounter.clock_bonus
	_record_stats()
	var result := {
		"attacker": encounter.attacker,
		"winner": encounter.winner,
		"totals": encounter.totals,
		"triggered": encounter.triggered,
		"shot": {},
		"through_pass": false,
		"referee": {},
	}

	var winner_side: Variant = encounter.winner
	if winner_side != null:
		var side: int = winner_side
		if encounter.is_shot:
			result["shot"] = _resolve_shot(side)
		else:
			_advance_ball(side)
			possession = side
			if bool(encounter.through_pass[side]):
				_advance_ball(side)
				result["through_pass"] = true
	if encounter.referee_due:
		result["referee"] = _roll_referee()
	return result


## True when the side in possession has pushed the ball onto the goal line and
## the next encounter is a shot at the keeper.
func _shot_pending() -> bool:
	return ball_zone == (BALL_ZONES - 1 if possession == Side.HOME else 0)


func lineup(side: int) -> Array[PlayerRecord]:
	return home_lineup if side == Side.HOME else away_lineup


func team_name(side: int) -> String:
	return home_name if side == Side.HOME else away_name


func other(side: int) -> int:
	return Side.AWAY if side == Side.HOME else Side.HOME


## Swaps a reserve into the lineup; the original allows this at any time.
func substitute(slot: int, bench_index: int) -> bool:
	if slot < 0 or slot >= home_lineup.size() or bench_index >= bench.size():
		return false
	var reserve: PlayerRecord = bench[bench_index]
	if not reserve.fits_slot(slot):
		return false
	bench[bench_index] = home_lineup[slot]
	home_lineup[slot] = reserve
	substitutions += 1
	return true


func _trio(side: int) -> Array:
	var out: Array = []
	var players := lineup(side)
	for slot: int in rolling_slots(side):
		var player: PlayerRecord = players[slot] if slot < players.size() else null
		out.append(DieState.new(side, slot, player))
	return out


## The home team always attacks to the right, as in the original.
func _advance_ball(side: int) -> void:
	ball_zone = clampi(ball_zone + (1 if side == Side.HOME else -1), 0, BALL_ZONES - 1)


## A won shot is a goal and restarts from the centre; a lost one is a save and
## the keeper's side clears the ball out of its own goal.
func _resolve_shot(shot_winner: int) -> Dictionary:
	var goal := shot_winner == encounter.attacker
	if goal:
		score[encounter.attacker] += 1
		ball_zone = CENTRE_ZONE
		possession = other(encounter.attacker)
	else:
		possession = shot_winner
		_advance_ball(shot_winner)
	_record_shot_stats(goal)
	return {
		"shooter": encounter.attacker,
		"goal": goal,
		"striker": int(encounter.totals[encounter.attacker]),
		"goalkeeper": int(encounter.totals[other(encounter.attacker)]),
		"totals": encounter.totals,
	}


## Keeps the numbers the achievements ask about: the best roll of the match, a
## blank roll on either side and whatever happened in the last minute.
func _record_stats() -> void:
	var own: int = encounter.totals[Side.HOME]
	var theirs: int = encounter.totals[Side.AWAY]
	stats["best_roll"] = maxi(int(stats["best_roll"]), own)
	if own == 0:
		stats["rolled_zero"] = true
	if theirs == 0:
		stats["enemy_rolled_zero"] = true


func _record_shot_stats(goal: bool) -> void:
	if seconds_remaining >= 60:
		return
	stats["last_event_seconds"] = seconds_remaining
	var ours := encounter.attacker == Side.HOME
	if goal and ours:
		stats["late_goal"] = true
		if score[Side.HOME] == score[Side.AWAY]:
			stats["late_equaliser"] = true
		elif score[Side.HOME] == score[Side.AWAY] + 1:
			stats["late_winner"] = true
	elif goal:
		stats["late_concede"] = true
	elif ours:
		stats["late_miss"] = true
	else:
		stats["late_save"] = true


## Dive! makes the referee roll once the play is over. Its die is entry 0 of
## PlayerData, annotated there as "offside, offside, yellow card, red card,
## blank, blank", so the side that comes up decides the call.
func _roll_referee() -> Dictionary:
	if _referee == null or _referee.faces.is_empty():
		return {}
	var side_index := _rng.randi_range(0, _referee.faces.size() - 1)
	var call: String = REFEREE_CALLS[side_index % REFEREE_CALLS.size()]
	match call:
		"offside":
			possession = other(possession)
			ball_zone = CENTRE_ZONE
		"yellow card":
			seconds_remaining = maxi(seconds_remaining - SECONDS_PER_ROLL, 0)
		"red card":
			_send_off()
	return {"face": _referee.faces[side_index], "call": call}


## A red card takes the central forward out; a reserve steps in when there is one.
func _send_off() -> void:
	for index in bench.size():
		if substitute(CENTRAL_ATTACK_SLOT, index):
			return


## Builds a lineup from a team's stored player ids, padding with what exists.
static func lineup_from(data: GameData, ids: Array[int]) -> Array[PlayerRecord]:
	var players: Array[PlayerRecord] = []
	for id in ids:
		var record := data.player(id)
		if record != null:
			players.append(record)
	return players
