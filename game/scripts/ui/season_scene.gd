extends Control
## Match selection, as the original draws it: the three campaign tabs on top of a
## scrolling column of VS bars, one per fixture, with the stars already won, and
## the MANAGE / PLAY pair at the foot of the screen.

const TAB_SIZE := Vector2(200, 250)
const TAB_Y := 77.0
const BAR_SIZE := Vector2(640, 120)
const BAR_TOP := 340.0
const BAR_GAP := 4.0
const STAR_SIZE := Vector2(34, 34)
const BUTTON_SIZE := Vector2(240, 93)
const FOOTER_Y := 855.0
const TAB_FRAMES := {
	SeasonRecord.LEAGUE: "CampaignLeagueTab",
	"T1": "CampaignT1Tab",
	"T2": "CampaignT2Tab",
}

var _tabs: Control
var _bars: VBoxContainer
var _title: Label
var _stars: Label
var _play: TextureButton


func _ready() -> void:
	add_child(Ui.background(App.sprite("StartScreenSS", "StartScreenBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	var header := TextureRect.new()
	header.texture = App.image("MenuHeader")
	header.position = Vector2.ZERO
	add_child(header)
	_title = Ui.centred("", 30)
	_title.size = Vector2(640, 77)
	add_child(_title)

	_tabs = Control.new()
	add_child(_tabs)

	var scroll := ScrollContainer.new()
	scroll.position = Vector2(0, BAR_TOP)
	scroll.size = Vector2(640, FOOTER_Y - BAR_TOP - 10)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	_bars = VBoxContainer.new()
	_bars.add_theme_constant_override("separation", int(BAR_GAP))
	_bars.custom_minimum_size = Vector2(640, 0)
	scroll.add_child(_bars)

	_stars = Ui.centred("", 22)
	_stars.size = Vector2(640, 40)
	_stars.position = Vector2(0, BAR_TOP - 44)
	add_child(_stars)

	_build_footer()
	_refresh()


func _build_footer() -> void:
	var manage := _texture_button("MatchSelectionSS1", "ManagePlayButton", "ManagePlayButtonPressed")
	manage.position = Vector2(40, FOOTER_Y)
	manage.pressed.connect(func() -> void: App.goto(App.SQUAD_SCENE))
	add_child(manage)
	add_child(_caption("MANAGE", manage.position))

	_play = _texture_button("MatchSelectionSS1", "ManagePlayButton", "ManagePlayButtonPressed")
	_play.position = Vector2(360, FOOTER_Y)
	_play.pressed.connect(_on_play)
	add_child(_play)
	add_child(_caption("PLAY", _play.position))

	var back := Ui.button("BACK", 20)
	back.size = Vector2(120, 44)
	back.position = Vector2(10, 84)
	back.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	add_child(back)


func _caption(text: String, at: Vector2) -> Label:
	var label := Ui.centred(text, 26)
	label.size = BUTTON_SIZE
	label.position = at
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return label


func _on_play() -> void:
	if App.career.can_play():
		App.goto(App.MATCH_SCENE)


func _refresh() -> void:
	var career := App.career
	var season := career.season()
	_title.text = season.season_name.to_upper()
	_stars.text = "%d / %d STARS" % [career.points, career.stars_needed()]
	_play.disabled = not career.can_play()
	_build_tabs()
	_build_bars()


## One tab per competition of the season; the cups the season does not run are
## drawn with the blank tab the original uses for them.
func _build_tabs() -> void:
	for child in _tabs.get_children():
		child.queue_free()
	var career := App.career
	var season := career.season()
	var index := 0
	for name: String in [SeasonRecord.LEAGUE] + SeasonRecord.CUPS:
		var available := season.has_competition(name)
		var frame: String = TAB_FRAMES[name] if available else "CampaignBlankTab"
		if available and name == career.competition:
			frame += "Selected"
		var tab := TextureButton.new()
		tab.texture_normal = App.sprite("MatchSelectionSS1", frame)
		tab.ignore_texture_size = true
		tab.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		tab.size = TAB_SIZE
		tab.position = Vector2(20 + index * 200, TAB_Y)
		tab.disabled = not available
		if available:
			tab.pressed.connect(func() -> void:
				App.career.select_competition(name)
				_refresh())
		_tabs.add_child(tab)
		var caption := Ui.centred(name if name == SeasonRecord.LEAGUE else "CUP " + name.substr(1), 20)
		caption.size = Vector2(TAB_SIZE.x, 40)
		caption.position = tab.position + Vector2(0, TAB_SIZE.y - 46)
		caption.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_tabs.add_child(caption)
		if not available:
			var lock := TextureRect.new()
			lock.texture = App.sprite("MatchSelectionSS1", "TournamentLocked")
			lock.position = tab.position + Vector2(50, 60)
			_tabs.add_child(lock)
		index += 1


func _build_bars() -> void:
	for child in _bars.get_children():
		child.queue_free()
	var career := App.career
	var fixtures := career.fixtures()
	for index in fixtures.size():
		_bars.add_child(_bar(index, fixtures[index]))


## A VS bar: the fixture number, the opponent, its purse and the stars won.
func _bar(index: int, team: TeamRecord) -> Control:
	var career := App.career
	var row := Control.new()
	row.custom_minimum_size = BAR_SIZE

	var plate := TextureRect.new()
	plate.texture = App.sprite("MatchSelectionSS1", "LeagueVSBar")
	plate.size = BAR_SIZE
	plate.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	plate.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	row.add_child(plate)

	var number := TextureRect.new()
	number.texture = App.sprite("MatchSelectionSS1", "MatchNo%d" % (index + 1))
	number.position = Vector2(18, 34)
	row.add_child(number)

	var crest := Ui.crest(team.emblem, Vector2(76, 76))
	crest.position = Vector2(80, 22)
	row.add_child(crest)

	var name := Ui.label(team.team_name, 24)
	name.position = Vector2(168, 26)
	row.add_child(name)

	var purse := Ui.label(App.cash_text(team.cash_win), 18)
	purse.position = Vector2(168, 62)
	row.add_child(purse)

	var stars := career.stars_of(index)
	for star in Career.STARS_WIN:
		var badge := TextureRect.new()
		badge.texture = App.sprite(
			"MatchSelectionSS1", "GoldStar" if star < stars else "EmptyStar"
		)
		badge.size = STAR_SIZE
		badge.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		badge.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		badge.position = Vector2(420 + star * 40, 44)
		row.add_child(badge)

	if index == career.fixture_index and career.can_play():
		var next := TextureRect.new()
		next.texture = App.sprite("MatchSelectionSS1", "NewIndicator")
		next.position = Vector2(560, 34)
		row.add_child(next)
	elif index > career.fixture_index or (index == career.fixture_index and career.cup_eliminated()):
		var locked := TextureRect.new()
		locked.texture = App.sprite("MatchSelectionSS1", "SoccerBallButtonLocked")
		locked.position = Vector2(560, 30)
		row.add_child(locked)
	return row


func _texture_button(sheet: String, normal: String, pressed: String) -> TextureButton:
	var button := TextureButton.new()
	button.texture_normal = App.sprite(sheet, normal)
	button.texture_pressed = App.sprite(sheet, pressed)
	button.ignore_texture_size = true
	button.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.size = BUTTON_SIZE
	return button
