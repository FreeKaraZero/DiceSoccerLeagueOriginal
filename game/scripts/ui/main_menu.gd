extends Control
## Career hub: shows where you are and opens the other screens.


func _ready() -> void:
	add_child(Ui.background(App.sprite("StartScreenSS", "StartScreenBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	var column := VBoxContainer.new()
	column.set_anchors_preset(Control.PRESET_FULL_RECT)
	column.offset_left = 40
	column.offset_right = -40
	column.offset_top = 60
	column.offset_bottom = -60
	column.add_theme_constant_override("separation", 16)
	add_child(column)

	var career := App.career
	var season := career.season()
	column.add_child(_title())
	column.add_child(Ui.centred(season.season_name, 32, Ui.CREAM))
	column.add_child(Ui.centred(_progress_text(), 22))
	column.add_child(Ui.centred(App.cash_text(career.cash), 34, Ui.GREEN))

	var opponent := career.next_opponent()
	if opponent != null:
		column.add_child(Ui.centred("Next: %s" % opponent.team_name, 26))

	column.add_child(_spacer())
	if opponent != null:
		var play := Ui.button("PLAY MATCH")
		play.pressed.connect(func() -> void: App.goto(App.MATCH_SCENE))
		play.disabled = not career.can_play()
		column.add_child(play)
	elif career.qualified() and career.season_index + 1 < App.data.seasons.size():
		var next_season := Ui.button("START NEXT SEASON")
		next_season.pressed.connect(func() -> void:
			App.career.advance_season()
			App.goto(App.MENU_SCENE))
		column.add_child(next_season)
	else:
		column.add_child(Ui.centred(_season_over_text(), 24, Ui.RED))

	for entry: Array in [
		["SQUAD", App.SQUAD_SCENE], ["SHOP", App.SHOP_SCENE], ["SEASON", App.SEASON_SCENE]
	]:
		var button := Ui.button(entry[0])
		var scene: String = entry[1]
		button.pressed.connect(func() -> void: App.goto(scene))
		column.add_child(button)

	if not career.lineup_is_valid():
		column.add_child(Ui.centred("Your line-up is invalid: fix it in SQUAD.", 20, Ui.RED))

	column.add_child(_spacer())
	var reset := Ui.button("NEW CAREER", 22)
	reset.pressed.connect(func() -> void:
		App.career.delete_save()
		App.goto(App.MENU_SCENE))
	column.add_child(reset)
	if not App.data.tips.is_empty():
		var tip := Ui.centred(App.data.tips[randi() % App.data.tips.size()], 18)
		tip.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		column.add_child(tip)


func _title() -> Control:
	var logo := App.image("StartScreenLogo")
	if logo == null:
		return Ui.centred("DICE SOCCER", 56)
	var rect := TextureRect.new()
	rect.texture = logo
	rect.custom_minimum_size = Vector2(0, 150)
	rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	return rect


func _progress_text() -> String:
	var career := App.career
	return "%s - match %d/%d - %d/%d stars" % [
		career.competition,
		mini(career.fixture_index + 1, career.fixtures().size()),
		career.fixtures().size(),
		career.points,
		career.stars_needed(),
	]


func _season_over_text() -> String:
	var career := App.career
	if not career.qualified():
		return "Season over: %d stars, %d needed. Start a new career or replay." % [
			career.points, career.stars_needed()
		]
	return "You have won every season. Champion!"


func _spacer() -> Control:
	var node := Control.new()
	node.custom_minimum_size = Vector2(0, 12)
	return node
