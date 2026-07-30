class_name KitRecord
extends RefCounted
## A shirt of Shirt.json: the colours and the pattern the kit is drawn with.

var id: int
var shirt_colour: int
var sleeve_colour: int
var style: int
var style_colour: int
var price: float


static func from_dict(id_: int, raw: Dictionary) -> KitRecord:
	var kit := KitRecord.new()
	kit.id = id_
	kit.shirt_colour = int(raw.get("ShirtColour", 1))
	kit.sleeve_colour = int(raw.get("SleeveColour", 1))
	kit.style = int(raw.get("ShirtStyle", 0))
	kit.style_colour = int(raw.get("StyleColour", 0))
	kit.price = float(raw.get("InAppPurchasePrice", 0.0))
	return kit
