class_name Abilities
extends RefCounted
## Trigger detection and effect parsing for the original superpowers.
##
## The data gives every ability a BuffTrigger, a BuffTarget and an English
## description. BuffType groups unrelated behaviours together, so the effect
## itself is read out of the description, which is written to a fixed pattern
## ("Reduces all enemy dice by 2. ACTIVATES when your team rolls a PAIR.").

enum Trigger {
	NONE = 0,
	WIN_TIES = 1,
	PAIR = 2,
	ALL_ODD = 3,
	ALL_EVEN = 4,
	STRAIGHT = 5,
	TRIPLE = 6,
	TOTAL_ODD = 7,
	TOTAL_EVEN = 8,
}
## Triggers 19..26 fire when the team rolls the value (trigger - FACE_TRIGGER_OFFSET).
const FACE_TRIGGER_OFFSET := 18

enum Effect {
	NONE,
	ADD,
	SUBTRACT,
	MULTIPLY,
	DIVIDE,
	REROLL,
	STRIP_BONUSES,
	CLEAR_REDUCTIONS,
	IMMUNE,
	REMOVE_REROLL,
	SWAP_BONUSES,
	STEAL_BONUS,
	THROUGH_PASS,
	REFEREE,
	WIN_TIES,
	WILD,
}

## BuffTarget in the original data: 0 for the abilities that target no die at
## all ("Roll the Referee die"), 1 for the ones that ask for a die ("Subtracts 3
## from a target dice"), 2 and 3 for the ones that take a whole team ("Reduces
## all enemy dice by 2", "Adds 1 to all players on your team"). Which team is
## said in the description, not in the field.
enum Target { NONE = 0, ONE = 1, TEAM = 2, WHOLE_TEAM = 3 }


static func trigger_label(trigger: int) -> String:
	match trigger:
		Trigger.NONE:
			return "always"
		Trigger.WIN_TIES:
			return "on ties"
		Trigger.PAIR:
			return "PAIR"
		Trigger.ALL_ODD:
			return "all ODD"
		Trigger.ALL_EVEN:
			return "all EVEN"
		Trigger.STRAIGHT:
			return "3 in a row"
		Trigger.TRIPLE:
			return "TRIPLE"
		Trigger.TOTAL_ODD:
			return "odd total"
		Trigger.TOTAL_EVEN:
			return "even total"
	if trigger > FACE_TRIGGER_OFFSET:
		return "a %d" % (trigger - FACE_TRIGGER_OFFSET)
	return "unknown"


## A wild side stands in for any value, so every substitution is tried.
static func is_triggered(trigger: int, faces: Array[int]) -> bool:
	if trigger == Trigger.NONE or trigger == Trigger.WIN_TIES:
		return false
	var values: Array[int] = []
	var wilds := 0
	for face in faces:
		if Dice.is_wild(face):
			wilds += 1
		elif Dice.is_number(face):
			values.append(Dice.value(face))
	if wilds == 0:
		return matches_pattern(trigger, values)
	for candidate in range(1, Dice.MAX_NUMBER_FACE + 1):
		var filled := values.duplicate()
		for _i in range(wilds):
			filled.append(candidate)
		if matches_pattern(trigger, filled):
			return true
	return false


static func matches_pattern(trigger: int, values: Array[int]) -> bool:
	if values.is_empty():
		return false
	var total := 0
	var counts: Dictionary = {}
	for value in values:
		total += value
		counts[value] = int(counts.get(value, 0)) + 1
	match trigger:
		Trigger.PAIR:
			return counts.values().max() == 2
		Trigger.TRIPLE:
			return counts.values().max() >= 3
		Trigger.ALL_ODD:
			return values.all(func(value: int) -> bool: return value % 2 == 1)
		Trigger.ALL_EVEN:
			return values.all(func(value: int) -> bool: return value % 2 == 0)
		Trigger.TOTAL_ODD:
			return total % 2 == 1
		Trigger.TOTAL_EVEN:
			return total % 2 == 0
		Trigger.STRAIGHT:
			var sorted := values.duplicate()
			sorted.sort()
			for index in range(1, sorted.size()):
				if sorted[index] != sorted[index - 1] + 1:
					return false
			return sorted.size() > 1
	if trigger > FACE_TRIGGER_OFFSET:
		return (trigger - FACE_TRIGGER_OFFSET) in values
	return false


## Returns {kind, amount, on_enemy, scope} describing what the ability does.
static func parse_effect(ability: AbilityRecord) -> Dictionary:
	if ability.trigger == Trigger.WIN_TIES:
		return _effect(Effect.WIN_TIES, 0, false, Target.NONE)
	# Only the clause before "ACTIVATES" describes the effect; the trigger clause
	# after it always mentions "your team", which would confuse the target.
	var text := ability.description.to_lower().split("activates")[0]
	var scope: int = ability.buff_target
	var says_enemy := (
		text.contains("enemy")
		or text.contains("opponent")
		or text.contains("other team")
	)
	var says_own := text.contains("your own") or text.contains("your team") or text.contains(
		"on your"
	)
	var amount := _amount(text)

	if text.contains("wild side"):
		return _effect(Effect.WILD, 0, false, Target.NONE)
	if text.contains("instantly move forward"):
		return _effect(Effect.THROUGH_PASS, 0, false, Target.NONE)
	if text.contains("referee die") or text.contains("yellow card"):
		return _effect(Effect.REFEREE, 0, false, Target.NONE)
	if text.contains("immune"):
		return _effect(Effect.IMMUNE, 0, says_enemy, scope)
	if text.contains("re-roll") or text.contains("reroll"):
		if text.contains("remove"):
			var all_rerolls := text.contains("all re-roll")
			return _effect(Effect.REMOVE_REROLL, 0 if all_rerolls else 1, true, scope)
		return _effect(Effect.REROLL, maxi(_word_count(text), 1), false, scope)
	if text.contains("strip"):
		return _effect(Effect.STRIP_BONUSES, 0, not says_own, scope)
	if text.contains("clear all reductions"):
		return _effect(Effect.CLEAR_REDUCTIONS, 0, says_enemy, scope)
	if text.contains("switch its positive and negative"):
		return _effect(Effect.SWAP_BONUSES, 0, true, scope)
	if text.contains("steal"):
		return _effect(Effect.STEAL_BONUS, 0, false, scope)
	if text.contains("by half") or text.contains("value by half"):
		return _effect(Effect.DIVIDE, 2, not says_own, scope)
	if text.contains("double"):
		return _effect(Effect.MULTIPLY, 2, says_enemy, scope)
	if text.contains("triple"):
		return _effect(Effect.MULTIPLY, 3, says_enemy, scope)
	if text.contains("multiply"):
		return _effect(Effect.MULTIPLY, maxi(amount, 2), says_enemy, scope)
	if text.contains("subtract") or text.contains("reduce"):
		return _effect(Effect.SUBTRACT, maxi(amount, 1), not says_own, scope)
	if text.contains("add"):
		return _effect(Effect.ADD, maxi(amount, 1), says_enemy, scope)
	# A few entries only write the bonus, e.g. "+1 to all opponents!".
	var signed := RegEx.create_from_string("\\+([0-9]+)").search(text)
	if signed != null:
		return _effect(Effect.ADD, int(signed.get_string(1)), says_enemy, scope)
	return _effect(Effect.NONE, 0, says_enemy, Target.NONE)


static func _effect(kind: Effect, amount: int, on_enemy: bool, scope: int) -> Dictionary:
	return {"kind": kind, "amount": amount, "on_enemy": on_enemy, "scope": scope}


## True when the player has to pick which die the effect lands on.
static func needs_target(effect: Dictionary) -> bool:
	if int(effect["scope"]) != Target.ONE:
		return false
	return int(effect["kind"]) not in [
		Effect.NONE, Effect.WILD, Effect.WIN_TIES, Effect.THROUGH_PASS, Effect.REFEREE
	]


static func effect_label(effect: Dictionary) -> String:
	var amount := int(effect["amount"])
	match int(effect["kind"]):
		Effect.ADD:
			return "+%d" % amount
		Effect.SUBTRACT:
			return "-%d" % amount
		Effect.MULTIPLY:
			return "x%d" % amount
		Effect.DIVIDE:
			return "half"
		Effect.REROLL:
			return "re-roll x%d" % amount
		Effect.STRIP_BONUSES:
			return "strips bonuses"
		Effect.CLEAR_REDUCTIONS:
			return "clears reductions"
		Effect.IMMUNE:
			return "immune"
		Effect.REMOVE_REROLL:
			return "kills re-rolls"
		Effect.SWAP_BONUSES:
			return "swaps +/-"
		Effect.STEAL_BONUS:
			return "steals bonus"
		Effect.THROUGH_PASS:
			return "through pass"
		Effect.REFEREE:
			return "referee"
		Effect.WIN_TIES:
			return "wins ties"
		Effect.WILD:
			return "wild"
	return ""


## The badge the original draws for the effect, from the Buff6 sheet.
static func effect_icon(effect: Dictionary) -> String:
	match int(effect["kind"]):
		Effect.ADD:
			return "DisplayBuffPlus"
		Effect.SUBTRACT:
			return "DisplayBuffMinus"
		Effect.MULTIPLY:
			return "DisplayBuffTimes"
		Effect.DIVIDE:
			return "DisplayBuffDivide"
		Effect.REROLL:
			return "DisplayBuffTargetedReroll"
	return ""


## Reads the operand of the effect verb, e.g. "subtracts 3 from ..." -> 3.
## Taking the first number in the text would pick up the trigger clause instead
## ("... ACTIVATES when your team rolls a 1").
static func _amount(text: String) -> int:
	var regex := RegEx.create_from_string(
		"(?:adds?|subtracts?|reduces?|multiply|by)[^0-9]{0,14}([0-9]+)"
	)
	var found := regex.search(text)
	return int(found.get_string(1)) if found != null else 0


## "Re-roll any TWO dice of your choice" grants two picks.
static func _word_count(text: String) -> int:
	if text.contains("two"):
		return 2
	if text.contains("three"):
		return 3
	return 1
