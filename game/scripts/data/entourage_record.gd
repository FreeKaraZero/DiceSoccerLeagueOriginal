class_name EntourageRecord
extends RefCounted
## A member of the entourage: the fans, officials and coaches you may bring to a
## match, two at a time. Their triggers are numbered on their own scale in
## EntourageData, unrelated to the player superpowers, so they are read here and
## checked against the state of the encounter.

enum Trig {
	NONE = 0,
	LOW_CLOCK = 1,
	LOSING = 2,
	PAIR = 3,
	PAIR_EMPTY_BENCH = 4,
	TRIPLE = 5,
	LOW_TOTAL = 7,
	ENEMY_DIE_HIGH = 8,
	ENEMY_SUM_HIGH = 9,
	ENEMY_HIGHER = 10,
	ENEMY_LEAD = 11,
	OWN_MULTIPLIER = 12,
}

var id: int
var member_name: String
var ability_name: String
var position: String
var description: String
var long_description: String
var unlock_criteria: String
var cost: int
var selling_price: int
var minimum_cash: int
var trigger: int
var icon_id: int  ## Ability id whose medal art the member shows.
var effect: Dictionary = {}
var threshold := 0  ## Operand of the triggers that compare totals.
var win_bonus := 0
var draw_pays_win := false
var wager := 0
var clock_bonus := 0


static func from_dict(id_: int, raw: Dictionary) -> EntourageRecord:
	var member := EntourageRecord.new()
	member.id = id_
	member.member_name = String(raw.get("Name", "Fan"))
	member.ability_name = String(raw.get("Ability Name", ""))
	member.position = String(raw.get("Position", "WAG"))
	member.description = String(raw.get("In Game Description", ""))
	member.long_description = String(raw.get("Ability Description", ""))
	member.unlock_criteria = String(raw.get("Unlock Criteria", ""))
	member.cost = int(raw.get("Player Cost", 0))
	member.selling_price = int(raw.get("Selling Price", 0))
	member.minimum_cash = int(raw.get("Minimum Cash To Equip", 0))
	member.trigger = int(raw.get("Ability Trigger", 0))
	member.icon_id = int(raw.get("Ability Icon", 0))
	member._read_description()
	return member


## True when the member changes the dice rather than the money or the clock.
func affects_dice() -> bool:
	return int(effect.get("kind", Abilities.Effect.NONE)) != Abilities.Effect.NONE


func is_triggered(context: Dictionary) -> bool:
	match trigger:
		Trig.NONE:
			return false
		Trig.LOW_CLOCK:
			return int(context.get("seconds_remaining", 0)) < 7 * 60
		Trig.LOSING:
			return int(context.get("goals_for", 0)) < int(context.get("goals_against", 0))
		Trig.PAIR:
			return Abilities.is_triggered(Abilities.Trigger.PAIR, context["own_faces"])
		Trig.PAIR_EMPTY_BENCH:
			return (
				bool(context.get("bench_empty", false))
				and Abilities.is_triggered(Abilities.Trigger.PAIR, context["own_faces"])
			)
		Trig.TRIPLE:
			return Abilities.is_triggered(Abilities.Trigger.TRIPLE, context["own_faces"])
		Trig.LOW_TOTAL:
			return int(context.get("own_total", 0)) < threshold
		Trig.ENEMY_DIE_HIGH:
			return int(context.get("enemy_best", 0)) > threshold
		Trig.ENEMY_SUM_HIGH:
			return int(context.get("enemy_total", 0)) > threshold
		Trig.ENEMY_HIGHER:
			return int(context.get("enemy_total", 0)) > int(context.get("own_total", 0))
		Trig.ENEMY_LEAD:
			return (
				int(context.get("enemy_total", 0)) - int(context.get("own_total", 0)) > threshold
			)
		Trig.OWN_MULTIPLIER:
			return bool(context.get("own_multiplier", false))
	return false


func trigger_text() -> String:
	match trigger:
		Trig.NONE:
			return "always"
		Trig.LOW_CLOCK:
			return "under 7:00"
		Trig.LOSING:
			return "when losing"
		Trig.PAIR:
			return "PAIR"
		Trig.PAIR_EMPTY_BENCH:
			return "PAIR, empty bench"
		Trig.TRIPLE:
			return "TRIPLE"
		Trig.LOW_TOTAL:
			return "total under %d" % threshold
		Trig.ENEMY_DIE_HIGH:
			return "enemy die over %d" % threshold
		Trig.ENEMY_SUM_HIGH:
			return "enemy total over %d" % threshold
		Trig.ENEMY_HIGHER:
			return "when behind on the roll"
		Trig.ENEMY_LEAD:
			return "beaten by over %d" % threshold
		Trig.OWN_MULTIPLIER:
			return "on your own x2"
	return ""


## The in-game copy carries the effect, exactly as it does for the superpowers.
func _read_description() -> void:
	var text := description.to_lower()
	threshold = _threshold(text)
	win_bonus = _money(text) if text.contains("increases reward for victory") else 0
	draw_pays_win = text.contains("draw awards you the same")
	wager = _money(text) if text.contains("extra +") and text.contains("if you win") else 0
	clock_bonus = 5 * 60 if text.contains("minutes") else 0
	var carrier := AbilityRecord.new()
	carrier.description = _effect_clause(description)
	carrier.trigger = Abilities.Trigger.NONE
	carrier.buff_target = _scope(text)
	if text.contains("wins any turn that ends in a tie"):
		effect = {
			"kind": Abilities.Effect.WIN_TIES,
			"amount": 0,
			"on_enemy": false,
			"scope": Abilities.Target.NONE,
		}
		return
	if text.contains("anchor"):
		effect = {
			"kind": Abilities.Effect.IMMUNE,
			"amount": 0,
			"on_enemy": false,
			"scope": Abilities.Target.WHOLE_TEAM,
		}
		return
	if text.contains("swap powerups") or text.contains("flip + and -"):
		effect = {
			"kind": Abilities.Effect.SWAP_BONUSES,
			"amount": 0,
			"on_enemy": false,
			"scope": carrier.buff_target,
		}
		return
	if text.contains("yellow card"):
		effect = {
			"kind": Abilities.Effect.REFEREE,
			"amount": 0,
			"on_enemy": true,
			"scope": Abilities.Target.NONE,
		}
		return
	if text.contains("4x token"):
		effect = {
			"kind": Abilities.Effect.MULTIPLY,
			"amount": 4,
			"on_enemy": false,
			"scope": Abilities.Target.ONE,
		}
		return
	if win_bonus > 0 or draw_pays_win or wager > 0 or clock_bonus > 0:
		effect = {
			"kind": Abilities.Effect.NONE,
			"amount": 0,
			"on_enemy": false,
			"scope": Abilities.Target.NONE,
		}
		return
	effect = Abilities.parse_effect(carrier)
	effect["scope"] = carrier.buff_target
	var clause := carrier.description.to_lower()
	effect["on_enemy"] = clause.contains("enemy") or clause.contains("opponent")


## Members write trigger and effect in one line ("Adds +5 when you roll a
## triple.", "If the enemy beats you by more than five, double one dice."), so
## the trigger half is dropped before the effect is read.
static func _effect_clause(text: String) -> String:
	var clause := RegEx.create_from_string(
		"(?i)\\b(when|triggers?(?: on| when| at| if)?|valid if)\\b[^.]*"
	).sub(text, "", true)
	return RegEx.create_from_string("(?i)^\\s*if\\b[^,]*,\\s*").sub(clause, "")


## "all your dice" and "3 dice" hit the whole trio, "a dice" only one of them.
static func _scope(text: String) -> int:
	if text.contains("all your dice") or text.contains("3 dice") or text.contains("to your team"):
		return Abilities.Target.WHOLE_TEAM
	return Abilities.Target.ONE


## "rolls less than 5", "more than 20", "by more than five", "rolls >7".
static func _threshold(text: String) -> int:
	var found := RegEx.create_from_string("(?:than|>)\\s*([0-9]+)").search(text)
	if found != null:
		return int(found.get_string(1))
	if text.contains("more than five"):
		return 5
	return 0


static func _money(text: String) -> int:
	var found := RegEx.create_from_string("([0-9][0-9,]*)\\s*k?").search(text)
	if found == null:
		return 0
	var digits := found.get_string(1).replace(",", "")
	var amount := int(digits)
	if text.contains("%sk" % digits):
		amount *= 1000
	return amount
