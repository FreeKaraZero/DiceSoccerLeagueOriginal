class_name Dice
extends RefCounted
## Semantics of the die sides stored in each card's DiceSides field.

const SPRITE_DIR := "res://assets/sprites/Buff6"
const MAX_NUMBER_FACE := 14
## Sides 16..18 add a flat bonus, 24..26 grant re-rolls, 31/32 are wild.
const BONUS_FACES := {16: 1, 17: 2, 18: 3}
const REROLL_FACES := {24: 1, 25: 2, 26: 3}
const WILD_FACES := [31, 32]


static func value(face: int) -> int:
	if face >= 1 and face <= MAX_NUMBER_FACE:
		return face
	return int(BONUS_FACES.get(face, 0))


static func rerolls(face: int) -> int:
	return int(REROLL_FACES.get(face, 0))


static func is_wild(face: int) -> bool:
	return face in WILD_FACES


static func is_bonus(face: int) -> bool:
	return BONUS_FACES.has(face)


static func texture_path(face: int) -> String:
	return SPRITE_DIR.path_join("DiceSide%02d.png" % face)


static func label(face: int) -> String:
	if is_bonus(face):
		return "+%d" % BONUS_FACES[face]
	if rerolls(face) > 0:
		return "re-roll x%d" % rerolls(face)
	if is_wild(face):
		return "wild"
	return str(value(face))
