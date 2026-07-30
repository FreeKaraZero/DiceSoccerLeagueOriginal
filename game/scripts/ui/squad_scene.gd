extends Control
## Line-up management: pick a slot, then a player from the squad.

const SLOT_LABELS := ["GK", "DEF", "DEF", "DEF", "FWD", "FWD", "FWD"]

var _selected_slot := -1
var _slot_buttons: Array[Button] = []
var _squad_list: VBoxContainer
var _hint: Label


func _ready() -> void:
	add_child(Ui.background(App.image("GreenField")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	var column := VBoxContainer.new()
	column.set_anchors_preset(Control.PRESET_FULL_RECT)
	column.offset_left = 20
	column.offset_right = -20
	column.offset_top = 20
	column.offset_bottom = -20
	column.add_theme_constant_override("separation", 8)
	add_child(column)

	column.add_child(Ui.centred("SQUAD", 40))
	_hint = Ui.centred("Tap a slot, then a player to put him in.", 18)
	_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(_hint)

	var slots := GridContainer.new()
	slots.columns = 2
	slots.add_theme_constant_override("h_separation", 8)
	column.add_child(slots)
	for slot in Career.LINEUP_SIZE:
		var button := Ui.button("", 20)
		button.custom_minimum_size = Vector2(290, 52)
		button.pressed.connect(_select_slot.bind(slot))
		slots.add_child(button)
		_slot_buttons.append(button)

	column.add_child(Ui.centred("YOUR PLAYERS", 26))
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(scroll)
	_squad_list = VBoxContainer.new()
	_squad_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_squad_list)

	var back := Ui.button("BACK", 24)
	back.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	column.add_child(back)

	_refresh()


func _select_slot(slot: int) -> void:
	_selected_slot = -1 if _selected_slot == slot else slot
	_refresh()


func _assign(player_id: int) -> void:
	if _selected_slot < 0:
		_hint.text = "Pick a slot first."
		return
	if App.career.assign_to_slot(_selected_slot, player_id):
		_selected_slot = -1
		_hint.text = "Line-up updated."
	else:
		_hint.text = "%s cannot play in %s." % [
			App.data.player(player_id).player_name, SLOT_LABELS[_selected_slot]
		]
	_refresh()


func _refresh() -> void:
	var career := App.career
	for slot in Career.LINEUP_SIZE:
		var player := App.data.player(career.lineup[slot])
		var marker := "> " if slot == _selected_slot else ""
		_slot_buttons[slot].text = "%s%s  %s" % [
			marker, SLOT_LABELS[slot], player.player_name if player != null else "empty"
		]

	for child in _squad_list.get_children():
		child.queue_free()
	for player in career.squad_players():
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		var starting := player.id in career.lineup
		var info := Ui.label("%s  %s  %s" % [
			player.position, player.player_name, _faces_text(player)
		], 18)
		info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(info)
		if starting:
			row.add_child(Ui.label("PLAYING", 18, Ui.GREEN))
		else:
			var pick := Ui.button("IN", 18)
			pick.custom_minimum_size = Vector2(90, 40)
			pick.pressed.connect(_assign.bind(player.id))
			row.add_child(pick)
		_squad_list.add_child(row)


func _faces_text(player: PlayerRecord) -> String:
	var labels: Array[String] = []
	for face in player.faces:
		labels.append(Dice.label(face))
	return "[%s]" % ", ".join(labels)
