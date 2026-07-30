extends Control
## The handbook: the six tutorial pages of the original, turned with the two
## wooden arrows.

const PAGES := 6
const PAGE_SIZE := Vector2(594, 826)
const ARROW_SIZE := Vector2(72, 72)

var _page := 1
var _sheet: TextureRect
var _left: TextureButton
var _right: TextureButton
var _counter: Label


func _ready() -> void:
	add_child(Ui.background(App.sprite("HandbookSS", "HandbookBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	_sheet = TextureRect.new()
	_sheet.size = PAGE_SIZE
	_sheet.position = Vector2((640 - PAGE_SIZE.x) / 2.0, 70)
	_sheet.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_sheet.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	add_child(_sheet)

	_left = Ui.sheet_button("HandbookSS", "HandbookLeftArrow", ARROW_SIZE)
	_left.position = Vector2(40, 880)
	_left.pressed.connect(func() -> void: _turn(-1))
	add_child(_left)

	_right = Ui.sheet_button("HandbookSS", "HandbookRightArrow", ARROW_SIZE)
	_right.position = Vector2(528, 880)
	_right.pressed.connect(func() -> void: _turn(1))
	add_child(_right)

	_counter = Ui.centred("", 24)
	_counter.size = Vector2(200, 40)
	_counter.position = Vector2(220, 896)
	add_child(_counter)

	var back := Ui.texture_button("MenuHeaderBackButton", Vector2(90, 62))
	back.position = Vector2(8, 8)
	back.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	add_child(back)

	_refresh()


func _turn(by: int) -> void:
	_page = clampi(_page + by, 1, PAGES)
	_refresh()


func _refresh() -> void:
	_sheet.texture = App.sprite("HandbookSS", "Handbook%d" % _page)
	_left.disabled = _page <= 1
	_right.disabled = _page >= PAGES
	_counter.text = "%d / %d" % [_page, PAGES]
