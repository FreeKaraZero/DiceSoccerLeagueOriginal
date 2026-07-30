extends Node
## Autoload holding the extracted game data, the career save and shared theming.

const FONT_PATH := "res://assets/fonts/Krungthep.ttf"
const IMAGES_DIR := "res://assets/images"
const SPRITES_DIR := "res://assets/sprites"

const MENU_SCENE := "res://scenes/main_menu.tscn"
const MATCH_SCENE := "res://scenes/match_scene.tscn"
const SQUAD_SCENE := "res://scenes/squad_scene.tscn"
const SHOP_SCENE := "res://scenes/shop_scene.tscn"
const SEASON_SCENE := "res://scenes/season_scene.tscn"

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


func sprite(sheet: String, frame: String) -> Texture2D:
	return texture(SPRITES_DIR.path_join(sheet).path_join(frame + ".png"))


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
