extends Control
## Transfer market: hire the players of the original PlayerData table.

var _list: VBoxContainer
var _cash: Label
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

	column.add_child(Ui.centred("SHOP", 40))
	_cash = Ui.centred("", 30, Ui.GREEN)
	column.add_child(_cash)
	_hint = Ui.centred("Better numbers and easier ability triggers win matches.", 18)
	_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(_hint)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_list)

	var back := Ui.button("BACK", 24)
	back.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	column.add_child(back)

	_refresh()


func _buy(player_id: int) -> void:
	var player := App.data.player(player_id)
	if App.career.buy(player_id):
		_hint.text = "%s joins your squad." % player.player_name
	else:
		_hint.text = "Not enough cash for %s." % player.player_name
	_refresh()


func _refresh() -> void:
	var career := App.career
	_cash.text = App.cash_text(career.cash)
	for child in _list.get_children():
		child.queue_free()
	for player in App.data.buyable_players():
		if career.owns(player.id):
			continue
		var row := Ui.panel()
		var line := HBoxContainer.new()
		line.add_theme_constant_override("separation", 8)
		row.add_child(line)

		var info := VBoxContainer.new()
		info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		info.add_child(Ui.label("%s  %s" % [player.position, player.player_name], 20))
		info.add_child(Ui.label(_faces_text(player), 16))
		var ability := App.data.ability(player.ability_id)
		if ability != null:
			info.add_child(Ui.label("%s (%s)" % [
				ability.ability_name, Abilities.trigger_label(ability.trigger)
			], 16, Ui.CREAM))
		line.add_child(info)

		var buy := Ui.button(App.cash_text(player.cost), 18)
		buy.custom_minimum_size = Vector2(150, 44)
		buy.disabled = not career.can_buy(player.id)
		buy.pressed.connect(_buy.bind(player.id))
		line.add_child(buy)
		_list.add_child(row)


func _faces_text(player: PlayerRecord) -> String:
	var labels: Array[String] = []
	for face in player.faces:
		labels.append(Dice.label(face))
	return "[%s]" % ", ".join(labels)
