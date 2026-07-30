extends Node
## Builds every screen once so a broken layout or a missing frame is caught before
## the game is played. Autoloads are needed, so it runs as a scene:
## godot --headless tests/screens.tscn

const SCENES := [
	"res://scenes/main_menu.tscn",
	"res://scenes/season_scene.tscn",
	"res://scenes/squad_scene.tscn",
	"res://scenes/entourage_scene.tscn",
	"res://scenes/kit_scene.tscn",
	"res://scenes/shop_scene.tscn",
	"res://scenes/trophy_scene.tscn",
	"res://scenes/handbook_scene.tscn",
	"res://scenes/match_scene.tscn",
]


func _ready() -> void:
	var failures := 0
	for path in SCENES:
		var packed: PackedScene = load(path)
		if packed == null:
			failures += 1
			printerr("FAIL: %s does not load" % path)
			continue
		var screen: Node = packed.instantiate()
		add_child(screen)
		await get_tree().process_frame
		var nodes := _count(screen)
		var blanks := _blank_art(screen)
		if not blanks.is_empty():
			failures += 1
			printerr("FAIL: %s draws %d nodes without art: %s" % [
				path, blanks.size(), ", ".join(blanks.slice(0, 8))
			])
		if nodes < 4:
			failures += 1
			printerr("FAIL: %s built %d nodes" % [path, nodes])
		else:
			print("%s: %d nodes" % [path.get_file(), nodes])
		remove_child(screen)
		screen.free()
	print("%d screens, %d failed" % [SCENES.size(), failures])
	get_tree().quit(1 if failures > 0 else 0)


## Picture nodes left without a texture: a frame the extraction does not have under
## the name the screen asks for.
func _blank_art(node: Node) -> Array[String]:
	var blanks: Array[String] = []
	if node is CanvasItem and not (node as CanvasItem).visible:
		return blanks
	if node is TextureRect and (node as TextureRect).texture == null:
		blanks.append(String(node.name))
	elif node is TextureButton and (node as TextureButton).texture_normal == null:
		blanks.append(String(node.name))
	for child in node.get_children():
		blanks.append_array(_blank_art(child))
	return blanks


func _count(node: Node) -> int:
	var total := 1
	for child in node.get_children():
		total += _count(child)
	return total
