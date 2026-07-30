extends Node
## Autoload holding the extracted game data, the career save and shared theming.

const FONT_PATH := "res://assets/fonts/Krungthep.ttf"
const IMAGES_DIR := "res://assets/images"
const SPRITES_DIR := "res://assets/sprites"
## Highest number a sheet family goes up to, e.g. GoalAnimationSS1...SS4.
const SHEET_SIBLINGS := 6

const MENU_SCENE := "res://scenes/main_menu.tscn"
const MATCH_SCENE := "res://scenes/match_scene.tscn"
const SQUAD_SCENE := "res://scenes/squad_scene.tscn"
const SHOP_SCENE := "res://scenes/shop_scene.tscn"
const SEASON_SCENE := "res://scenes/season_scene.tscn"
const TROPHY_SCENE := "res://scenes/trophy_scene.tscn"
const HANDBOOK_SCENE := "res://scenes/handbook_scene.tscn"
const ENTOURAGE_SCENE := "res://scenes/entourage_scene.tscn"
const KIT_SCENE := "res://scenes/kit_scene.tscn"

var data: GameData
var career: Career
var font: Font


func _ready() -> void:
	data = GameData.load_default()
	career = Career.new(data)
	if not career.load_saved():
		career.save()
	font = load(FONT_PATH) if ResourceLoader.exists(FONT_PATH) else null


func assets_available() -> bool:
	return data != null and data.is_loaded()


func goto(scene_path: String) -> void:
	get_tree().change_scene_to_file(scene_path)


## A frame of one of the extracted cocos2d sheets. The original split a screen
## over numbered sheets (MatchSelectionSS1, MatchSelectionSS2, ...) and spread its
## frames between them, so a miss is looked up in the sibling sheets too.
func sprite(sheet: String, frame: String) -> Texture2D:
	var found := texture(SPRITES_DIR.path_join(sheet).path_join(frame + ".png"))
	if found != null:
		return found
	var family := sheet.rstrip("0123456789")
	if family == sheet:
		return null
	for number in range(1, SHEET_SIBLINGS + 1):
		var sibling := "%s%d" % [family, number]
		if sibling == sheet:
			continue
		found = texture(SPRITES_DIR.path_join(sibling).path_join(frame + ".png"))
		if found != null:
			return found
	return null


func image(name: String) -> Texture2D:
	return texture(IMAGES_DIR.path_join(name + ".png"))


func texture(path: String) -> Texture2D:
	return load(path) if ResourceLoader.exists(path) else null


func cash_text(amount: int) -> String:
	var digits := str(absi(amount))
	var grouped := ""
	for index in digits.length():
		if index > 0 and (digits.length() - index) % 3 == 0:
			grouped += ","
		grouped += digits[index]
	return "%s$%s" % ["-" if amount < 0 else "", grouped]
