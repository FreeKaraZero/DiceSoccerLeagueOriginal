class_name DieState
extends RefCounted
## One rolled die inside an encounter.
##
## Bonuses are kept apart from the rolled number because several original
## abilities act on them alone: Zonal Marking strips the positives, Step-Over
## clears the reductions, Saving Tackle swaps them and Feint steals them.

var slot: int
var player: PlayerRecord
var side: int
var face := 0
var wild := false
var plus := 0
var minus := 0
var multiplier := 1.0
var immune := false
var rerolls := 0
var referee := false  ## The face that makes the referee roll at the end of the play.


func _init(side_: int, slot_: int, player_: PlayerRecord) -> void:
	side = side_
	slot = slot_
	player = player_


## Rolls a fresh face, keeping the bonuses that were already applied.
func roll(rng: RandomNumberGenerator) -> void:
	set_face(player.roll(rng) if player != null else 0)


func set_face(face_: int) -> void:
	face = face_
	wild = Dice.is_wild(face)
	referee = Dice.is_referee(face)
	plus += Dice.bonus(face)
	rerolls += Dice.rerolls(face)


## The rolled number; a wild side counts as nothing towards the total.
func base_value() -> int:
	return 0 if wild else Dice.value(face)


func value() -> int:
	return maxi(int(floor((base_value() + plus - minus) * multiplier)), 0)


func strip_bonuses() -> void:
	plus = 0
	multiplier = minf(multiplier, 1.0)


func clear_reductions() -> void:
	minus = 0
	multiplier = maxf(multiplier, 1.0)


func swap_bonuses() -> void:
	var positive := plus
	plus = minus
	minus = positive


## Short text for the modifier badge drawn under the die.
func modifier_text() -> String:
	var parts: Array[String] = []
	if plus > 0:
		parts.append("+%d" % plus)
	if minus > 0:
		parts.append("-%d" % minus)
	if not is_equal_approx(multiplier, 1.0):
		parts.append("x%s" % ("%.1f" % multiplier).trim_suffix(".0"))
	if immune:
		parts.append("immune")
	if rerolls > 0:
		parts.append("R%d" % rerolls)
	return " ".join(parts)
