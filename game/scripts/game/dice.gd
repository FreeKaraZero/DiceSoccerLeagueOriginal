class_name Dice
extends RefCounted
## Semantics of the die sides stored in each card's DiceSides field.
##
## A side is either a plain number or one of the special sides that the original
## describes in Abilities.json under the same id: 16..18 are the Dribble sides
## ("Add N to your final roll. Roll this die again."), 24 is Cross ("Re-roll this
## die and any one die of your choice"), 31 is the Wild side of Total Footballer
## and 32 only appears on the referee die.

const SPRITE_SHEET := "Buff6"
const SPRITE_DIR := "res://assets/sprites/Buff6"
const MAX_NUMBER_FACE := 14
const DRIBBLE_FACES := {16: 1, 17: 2, 18: 3}
## Every special side that says "roll this die again" grants its own re-roll;
## Cross additionally lets you re-roll a die of your choice, which comes from
## the ability of the same id.
const REROLL_FACES := {16: 1, 17: 1, 18: 1, 23: 1, 24: 1, 25: 2, 26: 3}
const WILD_FACE := 31
const REFEREE_FACE := 32


static func value(face: int) -> int:
	return face if face >= 1 and face <= MAX_NUMBER_FACE else 0


static func bonus(face: int) -> int:
	return int(DRIBBLE_FACES.get(face, 0))


static func rerolls(face: int) -> int:
	return int(REROLL_FACES.get(face, 0))


static func is_wild(face: int) -> bool:
	return face == WILD_FACE


static func is_referee(face: int) -> bool:
	return face == REFEREE_FACE


static func is_number(face: int) -> bool:
	return face >= 1 and face <= MAX_NUMBER_FACE


static func texture_path(face: int) -> String:
	return SPRITE_DIR.path_join("DiceSide%02d.png" % face)


static func label(face: int) -> String:
	if DRIBBLE_FACES.has(face):
		return "+%d and roll again" % DRIBBLE_FACES[face]
	if rerolls(face) > 0:
		return "re-roll"
	if is_wild(face):
		return "wild"
	if is_referee(face):
		return "referee"
	return str(value(face))
