extends Control
## The entourage: the 21 fans, officials and coaches of the original, with the
## two slots a match allows, their medals, prices and unlock notes.

const CARD_SIZE := Vector2(200, 130)
const HEAD_SIZE := Vector2(122, 114)
const SLOT_SIZE := Vector2(88, 88)
const INFO_SIZE := Vector2(470, 200)
const GRID_TOP := 300.0
const COLUMNS := 3

var _grid: GridContainer
var _slots: Control
var _info: Control
var _selected := 0


func _ready() -> void:
	add_child(Ui.background(App.sprite("StartScreenSS", "StartScreenBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	Ui.header(self, "ENTOURAGE", App.SQUAD_SCENE)

	_slots = Control.new()
	add_child(_slots)
	_info = Control.new()
	add_child(_info)

	var scroll := ScrollContainer.new()
	scroll.position = Vector2(0, GRID_TOP)
	scroll.size = Vector2(640, 960 - GRID_TOP)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	_grid = GridContainer.new()
	_grid.columns = COLUMNS
	_grid.add_theme_constant_override("h_separation", 8)
	_grid.add_theme_constant_override("v_separation", 8)
	scroll.add_child(_grid)

	_refresh()


func _refresh() -> void:
	_build_slots()
	_build_grid()
	_build_info()


## "It looks like we can use 2 of them per match": the two slots of the original.
func _build_slots() -> void:
	for child in _slots.get_children():
		child.queue_free()
	var career := App.career
	var title := Ui.centred("AT THE MATCH (%d/%d)" % [
		career.entourage_slots.size(), Career.ENTOURAGE_SLOTS
	], 22)
	title.size = Vector2(640, 30)
	title.position = Vector2(0, 84)
	_slots.add_child(title)

	for slot in Career.ENTOURAGE_SLOTS:
		var at := Vector2(120 + slot * 300, 120)
		var button := Ui.sheet_button("EnemyLineupSS1", "AssignEntourageButton", SLOT_SIZE)
		button.position = at
		_slots.add_child(button)
		if slot < career.entourage_slots.size():
			var id: int = career.entourage_slots[slot]
			var member := App.data.entourage_member(id)
			var head := TextureRect.new()
			head.texture = App.image("EntourageHead%02d" % id)
			head.size = SLOT_SIZE
			head.position = at
			head.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			head.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
			head.mouse_filter = Control.MOUSE_FILTER_IGNORE
			_slots.add_child(head)
			button.pressed.connect(func() -> void:
				App.career.unequip_entourage(id)
				_refresh())
			if member != null:
				_slots.add_child(Ui.caption(
					member.member_name, at + Vector2(-56, SLOT_SIZE.y), Vector2(200, 30), 18
				))
		else:
			_slots.add_child(Ui.caption("EMPTY", at, SLOT_SIZE, 18))


func _build_grid() -> void:
	for child in _grid.get_children():
		child.queue_free()
	var ids: Array[int] = []
	for id: int in App.data.entourage:
		ids.append(id)
	ids.sort()
	for id in ids:
		_grid.add_child(_card(id, App.data.entourage[id]))


func _card(id: int, member: EntourageRecord) -> Control:
	var career := App.career
	var owned := id in career.owned_entourage
	var cell := Control.new()
	cell.custom_minimum_size = CARD_SIZE

	var head := TextureRect.new()
	head.texture = App.image("EntourageHead%02d" % id)
	head.size = HEAD_SIZE
	head.position = Vector2((CARD_SIZE.x - HEAD_SIZE.x) / 2.0, 0)
	head.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	head.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	head.modulate = Color.WHITE if owned else Color(0.3, 0.3, 0.3, 0.9)
	cell.add_child(head)

	var name := Ui.centred(member.member_name, 16, Ui.CREAM if owned else Ui.RED)
	name.size = Vector2(CARD_SIZE.x, 24)
	name.position = Vector2(0, HEAD_SIZE.y - 4)
	cell.add_child(name)

	var line := App.cash_text(member.cost) if not owned else (
		"IN SLOT" if id in career.entourage_slots else "OWNED"
	)
	var status := Ui.centred(line, 16)
	status.size = Vector2(CARD_SIZE.x, 24)
	status.position = Vector2(0, HEAD_SIZE.y + 16)
	cell.add_child(status)

	var button := Button.new()
	button.flat = true
	button.size = CARD_SIZE
	button.pressed.connect(func() -> void:
		_selected = id
		_build_info())
	cell.add_child(button)
	return cell


## The info bar of the original: medal, ability name, what it does and when.
func _build_info() -> void:
	for child in _info.get_children():
		child.queue_free()
	if _selected == 0:
		var hint := Ui.centred("Pick a member to read his ability.", 20)
		hint.size = Vector2(640, 40)
		hint.position = Vector2(0, 240)
		_info.add_child(hint)
		return
	var member: EntourageRecord = App.data.entourage[_selected]
	var career := App.career
	var owned := _selected in career.owned_entourage

	var bar := TextureRect.new()
	bar.texture = App.sprite("GoalAnimationSS4", "EntourageInfoBar")
	bar.size = INFO_SIZE
	bar.position = Vector2(20, 210)
	bar.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	bar.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_info.add_child(bar)

	var medal := TextureRect.new()
	medal.texture = App.image("EntourageAbility%02d" % member.icon_id)
	medal.position = Vector2(26, 214)
	_info.add_child(medal)

	var title := Ui.label("%s - %s" % [member.member_name, member.ability_name], 20)
	title.position = Vector2(120, 214)
	_info.add_child(title)

	var text := Ui.label("%s\nActivates: %s" % [member.description, member.trigger_text()], 17)
	text.position = Vector2(120, 242)
	text.size = Vector2(360, 60)
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_info.add_child(text)

	var action := Ui.texture_button("BuyButton" if not owned else "SelectButton", Vector2(150, 56))
	action.position = Vector2(470, 226)
	action.disabled = (
		career.cash < member.cost if not owned else _selected in career.entourage_slots
	)
	action.pressed.connect(func() -> void:
		if owned:
			App.career.equip_entourage(_selected)
		else:
			App.career.buy_entourage(_selected)
		_refresh())
	_info.add_child(action)
	_info.add_child(Ui.caption(
		"BUY" if not owned else "USE", action.position, Vector2(150, 56), 22
	))
	if not owned and not member.unlock_criteria.is_empty():
		var note := Ui.label(member.unlock_criteria, 15)
		note.position = Vector2(120, 300)
		note.size = Vector2(400, 40)
		note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		_info.add_child(note)
