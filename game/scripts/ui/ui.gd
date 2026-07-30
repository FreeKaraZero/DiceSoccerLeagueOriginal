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


## A picture placed by hand. Sizing a TextureRect only works once it is told to
## ignore its texture size, otherwise the texture is its minimum size.
static func picture(texture: Texture2D, at: Vector2, size := Vector2.ZERO) -> TextureRect:
	var rect := TextureRect.new()
	rect.texture = texture
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rect.position = at
	if size != Vector2.ZERO:
		rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		rect.size = size
	return rect


## The MenuHeader strip every screen but the match wears, with its back arrow,
## the screen title and the bank balance on the right.
static func header(parent: Control, title: String, back_scene: String) -> void:
	var plate := TextureRect.new()
	plate.texture = App.image("MenuHeader")
	parent.add_child(plate)

	var back := texture_button("MenuHeaderBackButton", Vector2(90, 62))
	back.position = Vector2(8, 8)
	back.pressed.connect(func() -> void: App.goto(back_scene))
	parent.add_child(back)

	var heading := centred(title, 28, INK)
	heading.size = Vector2(280, 40)
	heading.position = Vector2(110, 12)
	parent.add_child(heading)

	parent.add_child(picture(App.image("CoinSmall"), Vector2(430, 12), Vector2(40, 40)))

	var bank := label(App.cash_text(App.career.cash), 24, INK)
	bank.position = Vector2(478, 16)
	parent.add_child(bank)


## A button built from a loose image and its "Pressed"/"Disabled" companions.
static func texture_button(image_name: String, size: Vector2) -> TextureButton:
	var button := TextureButton.new()
	button.texture_normal = App.image(image_name)
	button.texture_pressed = App.image(image_name + "Pressed")
	button.texture_disabled = App.image(image_name + "Disabled")
	button.ignore_texture_size = true
	button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.size = size
	return button


## The same, for a frame inside one of the extracted sprite sheets.
static func sheet_button(sheet: String, frame: String, size: Vector2) -> TextureButton:
	var button := TextureButton.new()
	button.texture_normal = App.sprite(sheet, frame)
	button.texture_pressed = App.sprite(sheet, frame + "Pressed")
	button.texture_disabled = App.sprite(sheet, frame + "Disabled")
	button.ignore_texture_size = true
	button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.size = size
	return button


## A club crest, built like the original from its laurels, body and design.
static func crest(emblem: int, size: Vector2) -> Control:
	var holder := Control.new()
	holder.custom_minimum_size = size
	var index := emblem % 4 + 1
	for part in ["Laurels", "Body", "Design"]:
		var texture := App.sprite("MatchSelectionSS1", "EnemyCrest%d%s" % [index, part])
		if texture == null:
			continue
		holder.add_child(picture(texture, Vector2.ZERO, size))
	return holder


## A caption drawn over a button, so the wooden art keeps its own label style.
static func caption(text: String, at: Vector2, size: Vector2, font_size := 26) -> Label:
	var node := centred(text, font_size)
	node.position = at
	node.size = size
	node.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return node


static func missing_assets_screen(parent: Control) -> void:
	var text := Ui.centred(
		"Assets missing.\n\nRun:\npython3 tools/extract_assets.py\n\nthen re-import the project.", 28
	)
	text.set_anchors_preset(Control.PRESET_FULL_RECT)
	parent.add_child(text)
