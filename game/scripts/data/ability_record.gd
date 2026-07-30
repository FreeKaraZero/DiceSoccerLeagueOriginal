class_name AbilityRecord
extends RefCounted
## A player superpower. The trigger is a data field; the effect only exists in
## the English copy of the original, so it is parsed out of the description.

var id: int
var ability_name: String
var description: String
var sprite_id: int
var trigger: int
var buff_type: int
var buff_slot: int
var buff_target: int


static func from_dict(id_: int, raw: Dictionary) -> AbilityRecord:
	var ability := AbilityRecord.new()
	ability.id = id_
	ability.ability_name = String(raw.get("Name", "Ability"))
	ability.description = String(raw.get("AbilityDescription", ""))
	ability.sprite_id = int(raw.get("SpriteID", 0))
	ability.trigger = int(raw.get("BuffTrigger", 0))
	ability.buff_type = int(raw.get("BuffType", 0))
	ability.buff_slot = int(raw.get("BuffSlot", 0))
	ability.buff_target = int(raw.get("BuffTarget", 0))
	return ability
