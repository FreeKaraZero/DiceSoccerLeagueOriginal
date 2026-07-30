extends Node
## Saves a 640x960 shot of every screen so the layout can be compared with the
## original art: godot --display-driver x11 tests/shots.tscn -- <output directory>

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
	var directory := "user://shots"
	for argument in OS.get_cmdline_user_args():
		directory = argument
	DirAccess.make_dir_recursive_absolute(directory)
	for path in SCENES:
		var screen: Node = (load(path) as PackedScene).instantiate()
		add_child(screen)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		var image := get_viewport().get_texture().get_image()
		image.save_png(directory.path_join(path.get_file().get_basename() + ".png"))
		print("shot %s" % path.get_file())
		remove_child(screen)
		screen.free()
	get_tree().quit()
