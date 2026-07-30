class_name PlayerRecord
extends RefCounted
## One player card of the original game: a die whose sides are printed on the card.

const GK := "GK"
const DEF := "DEF"
const MID := "MID"
const FWD := "FWD"

var id: int
var player_name: String
var position: String
var faces: Array[int] = []
var ability_id: int
var description: String
var cost: int
var image_id: int
var skin_colour: int


static func from_dict(id_: int, raw: Dictionary) -> PlayerRecord:
	var player := PlayerRecord.new()
	player.id = id_
	player.player_name = String(raw.get("Name", "Unknown"))
	player.position = String(raw.get("Position", MID))
	for side in String(raw.get("DiceSides", "")).split(",", false):
		player.faces.append(int(side))
	player.ability_id = int(raw.get("Superpower", 0))
	player.description = String(raw.get("Player Description", ""))
	player.cost = int(raw.get("Player Cost", 0))
	player.image_id = int(raw.get("ImageID", 0))
	player.skin_colour = int(raw.get("SkinColour", 0))
	return player


## Slots are the original lineup order: [GK, DEF, DEF, DEF, FWD, FWD, FWD].
func fits_slot(slot: int) -> bool:
	if slot == 0:
		return position == GK
	if position == MID:
		return true
	return position == (DEF if slot <= 3 else FWD)


func roll(rng: RandomNumberGenerator) -> int:
	return faces[rng.randi_range(0, faces.size() - 1)] if not faces.is_empty() else 0
