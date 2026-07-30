extends Control
## Kit selection: the shirt of the original built from its coloured layers
## (sleeves, body, design) out of Shirt.json and Colour.json, with the arrows that
## leaf through the 28 shirts and the 13 pairs of shorts of the wardrobe.
##
## The original sold the extra kits as in-app purchases; with no store to talk to
## the remake simply lets the wardrobe be worn, and shows the old price as a note.

const SHIRT_SIZE := Vector2(360, 360)
const ARROW_SIZE := Vector2(72, 72)
const PANTS_COLOURS := 13

var _shirt_id := 1
var _pants_id := 1
var _layers: Control
var _title: Label
var _note: Label


func _ready() -> void:
	add_child(Ui.background(App.image("KitSelectionBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	Ui.header(self, "KIT", App.SQUAD_SCENE)
	_shirt_id = App.career.shirt
	_pants_id = App.career.pants

	_title = Ui.centred("", 26)
	_title.size = Vector2(640, 40)
	_title.position = Vector2(0, 96)
	add_child(_title)

	_layers = Control.new()
	_layers.position = Vector2((640 - SHIRT_SIZE.x) / 2.0, 170)
	add_child(_layers)

	_arrows(560.0, "SHIRT", func(by: int) -> void: _leaf_shirt(by))
	_arrows(660.0, "SHORTS", func(by: int) -> void: _leaf_pants(by))

	_note = Ui.centred("", 18)
	_note.size = Vector2(640, 40)
	_note.position = Vector2(0, 740)
	add_child(_note)

	var wear := Ui.texture_button("SelectButton", Vector2(220, 70))
	wear.position = Vector2(90, 810)
	wear.pressed.connect(func() -> void:
		App.career.wear(_shirt_id, true)
		App.career.wear(_pants_id, false)
		_refresh())
	add_child(wear)
	add_child(Ui.caption("WEAR", wear.position, Vector2(220, 70), 24))

	var done := Ui.texture_button("DoneButton", Vector2(220, 70))
	done.position = Vector2(330, 810)
	done.pressed.connect(func() -> void: App.goto(App.SQUAD_SCENE))
	add_child(done)
	add_child(Ui.caption("DONE", done.position, Vector2(220, 70), 24))

	_refresh()


func _arrows(y: float, caption: String, leaf: Callable) -> void:
	var left := Ui.texture_button("LeftArrow", ARROW_SIZE)
	left.position = Vector2(60, y)
	left.pressed.connect(func() -> void: leaf.call(-1))
	add_child(left)

	var right := Ui.texture_button("RightArrow", ARROW_SIZE)
	right.position = Vector2(508, y)
	right.pressed.connect(func() -> void: leaf.call(1))
	add_child(right)

	add_child(Ui.caption(caption, Vector2(170, y + 16), Vector2(300, 40), 24))


func _leaf_shirt(by: int) -> void:
	var ids := _kit_ids()
	if ids.is_empty():
		return
	_shirt_id = ids[wrapi(ids.find(_shirt_id) + by, 0, ids.size())]
	_refresh()


func _leaf_pants(by: int) -> void:
	_pants_id = wrapi(_pants_id - 1 + by, 0, PANTS_COLOURS) + 1
	_refresh()


func _kit_ids() -> Array[int]:
	var ids: Array[int] = []
	for id: int in App.data.kits:
		ids.append(id)
	ids.sort()
	return ids


func _refresh() -> void:
	var kit := App.data.kit(_shirt_id)
	if kit == null:
		return
	_title.text = "SHIRT %d / %d - SHORTS %d / %d" % [
		_shirt_id, App.data.kits.size(), _pants_id, PANTS_COLOURS
	]
	var worn := App.career.shirt == _shirt_id and App.career.pants == _pants_id
	_note.text = "WORN" if worn else "In 2010 this kit cost $%.2f" % kit.price
	_build_shirt(kit)


## The shirt is drawn in the original's own order: sleeves, body, then design,
## each tinted with its Colour.json entry, with the shorts underneath.
func _build_shirt(kit: KitRecord) -> void:
	for child in _layers.get_children():
		child.queue_free()
	_add_layer("KitSelectionShorts", App.data.colour(_pants_id))
	_add_layer("KitSelectionSleeves", App.data.colour(kit.sleeve_colour))
	_add_layer("KitSelectionShirt", App.data.colour(kit.shirt_colour))
	if kit.style > 0:
		_add_layer("KitSelectionDesign%d" % kit.style, App.data.colour(kit.style_colour))


func _add_layer(frame: String, tint: Color) -> void:
	var layer := TextureRect.new()
	layer.texture = App.sprite("KitSelectionSS", frame)
	if layer.texture == null:
		return
	layer.size = SHIRT_SIZE
	layer.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	layer.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	layer.modulate = tint
	_layers.add_child(layer)
