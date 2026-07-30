class_name MatchSim
extends RefCounted
## Deterministic simulation of one Dice Soccer match.
##
## Each encounter the attacking trio rolls against the defending trio; the higher
## total takes the ball and pushes it one zone. The goal zone triggers a shot of
## the central forward against the goalkeeper. One encounter costs a minute.

const GOALKEEPER_SLOT := 0
const DEFENCE_SLOTS := [1, 2, 3]
const ATTACK_SLOTS := [4, 5, 6]
const CENTRAL_ATTACK_SLOT := 5
const BALL_ZONES := 5
const CENTRE_ZONE := 2
const SECONDS_PER_ENCOUNTER := 60
const DEFAULT_MATCH_SECONDS := 90 * 60

enum Side { HOME, AWAY }

var home_name: String
var away_name: String
var home_lineup: Array[PlayerRecord]
var away_lineup: Array[PlayerRecord]
var score := {Side.HOME: 0, Side.AWAY: 0}
var possession: int = Side.HOME
var ball_zone := CENTRE_ZONE
var seconds_remaining := DEFAULT_MATCH_SECONDS

var _abilities: Dictionary
var _rng := RandomNumberGenerator.new()


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
	possession = options.get("kickoff", Side.HOME)
	if options.has("seed"):
		_rng.seed = int(options["seed"])
	else:
		_rng.randomize()


func is_finished() -> bool:
	return seconds_remaining <= 0


func clock_text() -> String:
	var minutes := int(ceil(seconds_remaining / 60.0))
	return "%d:00" % maxi(minutes, 0)


func winner() -> Variant:
	if score[Side.HOME] == score[Side.AWAY]:
		return null
	return Side.HOME if score[Side.HOME] > score[Side.AWAY] else Side.AWAY


## Resolves one encounter and returns everything the UI needs to show it.
func play_encounter() -> Dictionary:
	if is_finished():
		return {}
	var attacker: int = possession
	var defender: int = _other(attacker)
	var attack_dice := _roll_trio(attacker, ATTACK_SLOTS)
	var defence_dice := _roll_trio(defender, DEFENCE_SLOTS)

	var triggered := _resolve_abilities(attacker, attack_dice, defence_dice)
	triggered.append_array(_resolve_abilities(defender, defence_dice, attack_dice))

	var attack_total := _total(attack_dice)
	var defence_total := _total(defence_dice)
	var encounter := {
		"attacker": attacker,
		"dice": {attacker: attack_dice, defender: defence_dice},
		"totals": {attacker: attack_total, defender: defence_total},
		"triggered": triggered,
		"winner": null,
		"shot": {},
	}

	var tie_breaker: Variant = _tie_winner(triggered)
	if attack_total > defence_total:
		encounter["winner"] = attacker
	elif defence_total > attack_total:
		encounter["winner"] = defender
	elif tie_breaker != null:
		encounter["winner"] = tie_breaker

	if encounter["winner"] != null:
		_advance_ball(encounter["winner"])
		possession = encounter["winner"]
		if _at_goal(possession):
			encounter["shot"] = _resolve_shot(possession)

	seconds_remaining = maxi(seconds_remaining - SECONDS_PER_ENCOUNTER, 0)
	return encounter


func lineup(side: int) -> Array[PlayerRecord]:
	return home_lineup if side == Side.HOME else away_lineup


func team_name(side: int) -> String:
	return home_name if side == Side.HOME else away_name


func _other(side: int) -> int:
	return Side.AWAY if side == Side.HOME else Side.HOME


## The home team always attacks to the right, as in the original.
func _advance_ball(side: int) -> void:
	ball_zone = clampi(ball_zone + (1 if side == Side.HOME else -1), 0, BALL_ZONES - 1)


func _at_goal(side: int) -> bool:
	return ball_zone == (BALL_ZONES - 1 if side == Side.HOME else 0)


func _resolve_shot(shooter: int) -> Dictionary:
	var keeper := _other(shooter)
	var striker := _die_for(shooter, CENTRAL_ATTACK_SLOT)
	var goalkeeper := _die_for(keeper, GOALKEEPER_SLOT)
	var goal: bool = striker["value"] > goalkeeper["value"]
	if goal:
		score[shooter] += 1
	ball_zone = CENTRE_ZONE
	possession = keeper
	return {
		"shooter": shooter,
		"goal": goal,
		"striker": striker,
		"goalkeeper": goalkeeper,
	}


func _roll_trio(side: int, slots: Array) -> Array[Dictionary]:
	var dice: Array[Dictionary] = []
	for slot: int in slots:
		dice.append(_die_for(side, slot))
	return dice


func _die_for(side: int, slot: int) -> Dictionary:
	var players := lineup(side)
	var player: PlayerRecord = players[slot] if slot < players.size() else null
	var face := player.roll(_rng) if player != null else 0
	var die := {
		"slot": slot,
		"player": player,
		"face": face,
		"value": Dice.value(face),
		"wild": Dice.is_wild(face),
		"rerolls": Dice.rerolls(face),
		"modifier": "",
	}
	# A re-roll side rolls again immediately, keeping the better result.
	for _i in range(die["rerolls"]):
		var again := player.roll(_rng) if player != null else 0
		if Dice.value(again) > int(die["value"]):
			die["face"] = again
			die["value"] = Dice.value(again)
			die["wild"] = Dice.is_wild(again)
			die["modifier"] = "re-rolled"
	return die


func _total(dice: Array[Dictionary]) -> int:
	var total := 0
	for die in dice:
		total += int(die["value"])
	return total


func _faces(dice: Array[Dictionary]) -> Array[int]:
	var faces: Array[int] = []
	for die in dice:
		faces.append(int(die["face"]))
	return faces


func _resolve_abilities(side: int, own: Array[Dictionary], enemy: Array[Dictionary]) -> Array[Dictionary]:
	var triggered: Array[Dictionary] = []
	var faces := _faces(own)
	for die in own:
		var player: PlayerRecord = die["player"]
		if player == null or player.ability_id == 0:
			continue
		var ability: AbilityRecord = _abilities.get(player.ability_id)
		if ability == null or not Abilities.is_triggered(ability.trigger, faces):
			continue
		var effect := Abilities.parse_effect(ability)
		_apply_effect(effect, own, enemy)
		triggered.append({"side": side, "player": player, "ability": ability, "effect": effect})
	return triggered


## Targets are picked automatically: the original let the player choose.
func _apply_effect(effect: Dictionary, own: Array[Dictionary], enemy: Array[Dictionary]) -> void:
	var pool: Array[Dictionary] = enemy if bool(effect["on_enemy"]) else own
	if pool.is_empty():
		return
	var kind := int(effect["kind"])
	var amount := int(effect["amount"])
	match kind:
		Abilities.Effect.DOUBLE:
			var best := _best_die(pool, not bool(effect["on_enemy"]))
			best["value"] = int(best["value"]) * 2
			best["modifier"] = "x2"
		Abilities.Effect.HALVE:
			var target := _best_die(pool, false)
			target["value"] = int(target["value"]) / 2
			target["modifier"] = "half"
		Abilities.Effect.ADD:
			var lowest := _best_die(pool, false)
			lowest["value"] = int(lowest["value"]) + amount
			lowest["modifier"] = "+%d" % amount
		Abilities.Effect.SUBTRACT:
			for die in pool:
				die["value"] = maxi(int(die["value"]) - amount, 0)
				die["modifier"] = "-%d" % amount
		Abilities.Effect.REROLL:
			var weakest := _best_die(pool, false)
			var player: PlayerRecord = weakest["player"]
			if player != null:
				var face := player.roll(_rng)
				weakest["face"] = face
				weakest["value"] = Dice.value(face)
				weakest["wild"] = Dice.is_wild(face)
				weakest["modifier"] = "re-roll"
		Abilities.Effect.INSTANT_WIN:
			var striker := _best_die(own, true)
			striker["value"] = int(striker["value"]) + 99
			striker["modifier"] = "instant win"
		_:
			pass  # WIN_TIES is handled when comparing totals; FLAVOUR is log-only


func _best_die(pool: Array[Dictionary], highest: bool) -> Dictionary:
	var chosen: Dictionary = pool[0]
	for die in pool:
		var better: bool = int(die["value"]) > int(chosen["value"]) if highest else int(die["value"]) < int(chosen["value"])
		if better:
			chosen = die
	return chosen


func _tie_winner(triggered: Array[Dictionary]) -> Variant:
	for entry in triggered:
		if int(entry["effect"]["kind"]) == Abilities.Effect.WIN_TIES:
			return entry["side"]
	return null


## Builds a lineup from a team's stored player ids, padding with what exists.
static func lineup_from(data: GameData, ids: Array[int]) -> Array[PlayerRecord]:
	var players: Array[PlayerRecord] = []
	for id in ids:
		var record := data.player(id)
		if record != null:
			players.append(record)
	return players
