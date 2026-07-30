extends Control
## Season overview: fixtures, results and the cups of the current season.

var _list: VBoxContainer
var _header: Label


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

	var career := App.career
	column.add_child(Ui.centred("SEASON %d/%d" % [
		career.season_index + 1, App.data.seasons.size()
	], 36))
	_header = Ui.centred("", 22)
	column.add_child(_header)

	var competitions := HBoxContainer.new()
	competitions.add_theme_constant_override("separation", 8)
	column.add_child(competitions)
	for name: String in career.season().competition_names():
		var button := Ui.button(name, 20)
		button.custom_minimum_size = Vector2(0, 44)
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.disabled = name == career.competition
		button.pressed.connect(func() -> void:
			App.career.select_competition(name)
			_refresh())
		competitions.add_child(button)

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


func _refresh() -> void:
	var career := App.career
	_header.text = "%s - %s - %d/%d stars" % [
		career.season().season_name, career.competition, career.points, career.stars_needed()
	]
	for child in _list.get_children():
		child.queue_free()

	var played := _played_results()
	var fixtures := career.fixtures()
	for index in fixtures.size():
		var team := fixtures[index]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		var title := Ui.label("%d. %s" % [index + 1, team.team_name], 20)
		title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(title)
		if index < played.size():
			var result: Dictionary = played[index]
			row.add_child(Ui.label("%d - %d" % [
				int(result["goals_for"]), int(result["goals_against"])
			], 20, Ui.GREEN if int(result["points"]) == Career.POINTS_WIN else Ui.RED))
		elif index == career.fixture_index:
			row.add_child(Ui.label("NEXT", 20, Ui.CREAM))
		else:
			row.add_child(Ui.label(App.cash_text(team.cash_win), 18))
		_list.add_child(row)


## Results already played in the current season and competition, in order.
func _played_results() -> Array[Dictionary]:
	var career := App.career
	var played: Array[Dictionary] = []
	for entry: Dictionary in career.results:
		if int(entry["season"]) == career.season_index and String(entry["competition"]) == career.competition:
			played.append(entry)
	return played
