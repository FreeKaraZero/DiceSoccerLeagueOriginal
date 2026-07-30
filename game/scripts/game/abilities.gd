class_name Abilities
extends RefCounted
## Trigger detection and effect parsing for the original superpowers.

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

enum Effect { NONE, DOUBLE, HALVE, ADD, SUBTRACT, REROLL, INSTANT_WIN, WIN_TIES, FLAVOUR }


static func trigger_label(trigger: int) -> String:
	match trigger:
		Trigger.NONE:
			return "always"
		Trigger.WIN_TIES:
			return "on ties"
		Trigger.PAIR:
			return "on a pair"
		Trigger.ALL_ODD:
			return "all odd"
		Trigger.ALL_EVEN:
			return "all even"
		Trigger.STRAIGHT:
			return "on a straight"
		Trigger.TRIPLE:
			return "on a triple"
		Trigger.TOTAL_ODD:
			return "odd total"
		Trigger.TOTAL_EVEN:
			return "even total"
	if trigger >= FACE_TRIGGER_OFFSET + 1:
		return "on a %d" % (trigger - FACE_TRIGGER_OFFSET)
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
		else:
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


## BuffType is not a usable effect enum in the original data (one value covers
## unrelated behaviours), so the effect is read out of the ability copy.
static func parse_effect(ability: AbilityRecord) -> Dictionary:
	if ability.trigger == Trigger.WIN_TIES:
		return _effect(Effect.WIN_TIES, 0, false, false)
	# Only the clause before "ACTIVATES" describes the effect; the rest is the
	# trigger and always mentions "your team", which would confuse the target.
	var text := ability.description.to_lower().split("activates")[0]
	var theirs := text.contains("opponent") or text.contains("enemy")
	var yours := text.contains("your") or text.contains("own")
	# "Reduces a die's value by 1" names no side: a penalty hits the other team
	# unless the text explicitly says otherwise.
	var whole_pool := text.contains(" all ") or text.contains("each") or text.contains("every")
	var amount := _amount_after_verb(text)
	if text.contains("double"):
		return _effect(Effect.DOUBLE, 0, theirs, whole_pool)
	if text.contains("halve") or text.contains("half"):
		return _effect(Effect.HALVE, 0, not yours, whole_pool)
	if text.contains("subtract") or text.contains("reduce") or text.contains("strip"):
		return _effect(Effect.SUBTRACT, maxi(amount, 1), not yours, whole_pool)
	if text.contains("add") and amount > 0:
		return _effect(Effect.ADD, amount, theirs, whole_pool)
	if text.contains("re-roll") or text.contains("reroll"):
		return _effect(Effect.REROLL, maxi(amount, 1), theirs, whole_pool)
	if text.contains("win the encounter") or text.contains("instantly win"):
		return _effect(Effect.INSTANT_WIN, 0, false, false)
	return _effect(Effect.FLAVOUR, 0, theirs, false)


static func _effect(kind: Effect, amount: int, on_enemy: bool, whole_pool: bool) -> Dictionary:
	return {"kind": kind, "amount": amount, "on_enemy": on_enemy, "whole_pool": whole_pool}


static func effect_label(effect: Dictionary) -> String:
	match int(effect["kind"]):
		Effect.DOUBLE:
			return "double"
		Effect.HALVE:
			return "halve"
		Effect.ADD:
			return "+%d" % int(effect["amount"])
		Effect.SUBTRACT:
			return "-%d" % int(effect["amount"])
		Effect.REROLL:
			return "re-roll"
		Effect.INSTANT_WIN:
			return "instant win"
		Effect.WIN_TIES:
			return "wins ties"
	return "flavour"


## Reads the operand of the effect verb, e.g. "subtracts 3 from ..." -> 3.
## Taking the first number in the text would pick up the trigger clause instead
## ("... ACTIVATES when your team rolls a 1").
static func _amount_after_verb(text: String) -> int:
	var regex := RegEx.create_from_string("(?:adds?|subtracts?|reduces?|re-?rolls?)[^0-9]{0,12}([0-9]+)")
	var found := regex.search(text)
	return int(found.get_string(1)) if found != null else 0
