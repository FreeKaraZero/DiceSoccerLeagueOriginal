extends Control
## The start screen: the sky background with its drifting clouds, the big logo
## and the stack of wooden menu buttons, with the season crest and the bank on
## the header, exactly where the original puts them.

const CLOUD_SIZE := Vector2(768, 212)
const CLOUD_SPEED := 8.0
const LOGO_SIZE := Vector2(400, 400)
const LOGO_TOP := 60.0
const BUTTON_SIZE := Vector2(410, 126)
const BUTTON_X := 115.0
const BUTTON_TOP := 440.0
const BUTTON_GAP := 6.0
const TAB_SIZE := Vector2(200, 120)
const TAB_TOP := 832.0

var _clouds: TextureRect


func _ready() -> void:
	add_child(Ui.background(App.sprite("StartScreenSS", "StartScreenBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	_clouds = TextureRect.new()
	_clouds.texture = App.sprite("StartScreenSS", "StartScreenClouds")
	_clouds.size = CLOUD_SIZE
	_clouds.position = Vector2(-64, 250)
	add_child(_clouds)

	var logo := TextureRect.new()
	logo.texture = App.image("StartScreenLogoBig")
	logo.size = LOGO_SIZE
	logo.position = Vector2((640 - LOGO_SIZE.x) / 2.0, LOGO_TOP)
	logo.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	logo.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	add_child(logo)

	_build_header()
	_build_buttons()


## The header carries the season crest, its name and the bank balance.
func _build_header() -> void:
	var career := App.career
	var header := TextureRect.new()
	header.texture = App.image("MenuHeader")
	add_child(header)

	var title := Ui.label(career.season().season_name.to_upper(), 26)
	title.position = Vector2(20, 8)
	add_child(title)

	var coin := TextureRect.new()
	coin.texture = App.image("CoinSmall")
	coin.position = Vector2(430, 12)
	add_child(coin)

	var bank := Ui.label(App.cash_text(career.cash), 24, Ui.INK)
	bank.position = Vector2(486, 16)
	add_child(bank)


## The original stacks CAMPAIGN, MULTIPLAYER and MANAGE, each with its own art,
## and keeps the shop, the trophy room and the options on the tabs beneath them.
func _build_buttons() -> void:
	var career := App.career
	_menu_button("MainMenuButtons01", 0, App.SEASON_SCENE)
	var multiplayer := _menu_button("MainMenuButtons02", 1, "")
	multiplayer.disabled = true  ## The remake has no server to play against.
	_menu_button("MainMenuButtons03", 2, App.SQUAD_SCENE)

	var tabs: Array = [
		["BottomTab01", App.SHOP_SCENE],
		["BottomTab02", App.TROPHY_SCENE],
		["BottomTab03", App.HANDBOOK_SCENE],
	]
	for index in tabs.size():
		var frame: String = tabs[index][0]
		var scene: String = tabs[index][1]
		var tab := Ui.texture_button(frame, TAB_SIZE)
		tab.position = Vector2(20 + index * 210, TAB_TOP)
		tab.pressed.connect(func() -> void: App.goto(scene))
		add_child(tab)

	if career.season_finished() and career.qualified() and (
		career.season_index + 1 < App.data.seasons.size()
	):
		var next_season := Ui.button("START SEASON %d" % (career.season_index + 2), 24)
		next_season.size = Vector2(410, 56)
		next_season.position = Vector2(BUTTON_X, 384)
		next_season.pressed.connect(func() -> void:
			App.career.advance_season()
			App.goto(App.MENU_SCENE))
		add_child(next_season)
	elif not career.lineup_is_valid():
		var warning := Ui.centred("Your line-up is invalid: fix it in MANAGE.", 20, Ui.RED)
		warning.size = Vector2(640, 40)
		warning.position = Vector2(0, 392)
		add_child(warning)


func _menu_button(frame: String, row: int, scene: String) -> TextureButton:
	var button := Ui.texture_button(frame, BUTTON_SIZE)
	button.position = Vector2(BUTTON_X, BUTTON_TOP + row * (BUTTON_SIZE.y + BUTTON_GAP))
	if not scene.is_empty():
		button.pressed.connect(func() -> void: App.goto(scene))
	add_child(button)
	return button


func _process(delta: float) -> void:
	if _clouds == null:
		return
	_clouds.position.x -= CLOUD_SPEED * delta
	if _clouds.position.x < -CLOUD_SIZE.x + 640.0:
		_clouds.position.x = -64.0
