class_name Ui
extends RefCounted
## Small helpers so every screen shares the original typeface and look.

const CREAM := Color("f6e7c1")
const INK := Color("3b2b12")
const GREEN := Color("2f7d32")
const RED := Color("c62828")


static func label(text: String, size := 24, colour := CREAM) -> Label:
	var node := Label.new()
	node.text = text
	node.add_theme_font_size_override("font_size", size)
	node.add_theme_color_override("font_color", colour)
	node.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.8))
	node.add_theme_constant_override("outline_size", 6)
	if App.font != null:
		node.add_theme_font_override("font", App.font)
	return node


static func centred(text: String, size := 24, colour := CREAM) -> Label:
	var node := label(text, size, colour)
	node.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	node.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	return node


static func button(text: String, size := 28) -> Button:
	var node := Button.new()
	node.text = text
	node.custom_minimum_size = Vector2(0, 64)
	node.add_theme_font_size_override("font_size", size)
	if App.font != null:
		node.add_theme_font_override("font", App.font)
	return node


static func panel(colour := Color(0, 0, 0, 0.55)) -> PanelContainer:
	var node := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = colour
	style.set_corner_radius_all(12)
	style.content_margin_left = 12
	style.content_margin_right = 12
	style.content_margin_top = 8
	style.content_margin_bottom = 8
	node.add_theme_stylebox_override("panel", style)
	return node


## Fills the screen with a texture, or a flat colour when assets are missing.
static func background(texture: Texture2D, fallback := Color("14401b")) -> Control:
	if texture == null:
		var plate := ColorRect.new()
		plate.color = fallback
		plate.set_anchors_preset(Control.PRESET_FULL_RECT)
		return plate
	var rect := TextureRect.new()
	rect.texture = texture
	rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	return rect


static func missing_assets_screen(parent: Control) -> void:
	var text := Ui.centred(
		"Assets missing.\n\nRun:\npython3 tools/extract_assets.py\n\nthen re-import the project.", 28
	)
	text.set_anchors_preset(Control.PRESET_FULL_RECT)
	parent.add_child(text)
