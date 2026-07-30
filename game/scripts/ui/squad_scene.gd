extends Control
## The formation screen: the pitch with the seven cards of the line-up in their
## slots, the four bench cards under it and the row of wooden buttons the original
## keeps at the foot - deck, kit, shop, entourage and PLAY.

const SLOT_LABELS := ["GK", "DEF", "DEF", "DEF", "FWD", "FWD", "FWD"]
const CARD_SIZE := Vector2(130, 195)
const CARD_X := [20.0, 255.0, 490.0]
const ROW_Y := {"FWD": 85.0, "DEF": 290.0, "GK": 490.0}
const BENCH_SIZE_PX := Vector2(120, 100)
const BENCH_Y := 695.0
const FOOTER_Y := 810.0
const FOOTER_SIZE := Vector2(120, 100)

var _selected_slot := -1
var _pitch: Control
var _bench: Control
var _picker: Control
var _hint: Label


func _ready() -> void:
	add_child(Ui.background(_field_texture()))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	Ui.header(self, "FORMATION", App.MENU_SCENE)
	_pitch = Control.new()
	add_child(_pitch)
	_bench = Control.new()
	add_child(_bench)

	_hint = Ui.centred("Tap a slot, then a player.", 18)
	_hint.size = Vector2(640, 28)
	_hint.position = Vector2(0, 664)
	add_child(_hint)

	_build_footer()
	_refresh()


func _field_texture() -> Texture2D:
	var field := App.image("Season%dField3(1.1)" % App.career.season_index)
	return field if field != null else App.image("GreenField")


func _build_footer() -> void:
	var entries: Array = [
		["DeckButton", "SQUAD", func() -> void: _open_picker(-1)],
		["KitButton", "KIT", func() -> void: App.goto(App.KIT_SCENE)],
		["FormationShopButton", "SHOP", func() -> void: App.goto(App.SHOP_SCENE)],
		["AssignEntourageButton", "CREW", func() -> void: App.goto(App.ENTOURAGE_SCENE)],
		["FormationPlayButton", "PLAY", func() -> void: App.goto(App.SEASON_SCENE)],
	]
	for index in entries.size():
		var frame: String = entries[index][0]
		var caption: String = entries[index][1]
		var action: Callable = entries[index][2]
		var button := Ui.sheet_button("EnemyLineupSS1", frame, FOOTER_SIZE)
		button.position = Vector2(10 + index * 125, FOOTER_Y)
		button.pressed.connect(action)
		add_child(button)
		add_child(Ui.caption(
			caption, button.position + Vector2(0, FOOTER_SIZE.y - 26), Vector2(FOOTER_SIZE.x, 26), 18
		))


func _refresh() -> void:
	_build_pitch()
	_build_bench()


func _build_pitch() -> void:
	for child in _pitch.get_children():
		child.queue_free()
	for slot in Career.LINEUP_SIZE:
		var at := _slot_position(slot)
		_pitch.add_child(_card(slot, App.data.player(App.career.lineup[slot]), at))


## Slot 0 is the goalkeeper, 1-3 the defence, 4-6 the attack: the order the
## original stores its line-up in, drawn from its own goal upwards.
func _slot_position(slot: int) -> Vector2:
	if slot == 0:
		return Vector2(CARD_X[1], ROW_Y["GK"])
	if slot <= 3:
		return Vector2(CARD_X[slot - 1], ROW_Y["DEF"])
	return Vector2(CARD_X[slot - 4], ROW_Y["FWD"])


func _card(slot: int, player: PlayerRecord, at: Vector2) -> Control:
	var holder := Control.new()
	holder.position = at

	var blank := TextureRect.new()
	blank.texture = App.image("playercardblanksmall")
	blank.size = CARD_SIZE
	blank.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	blank.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	holder.add_child(blank)

	if player != null:
		var head := TextureRect.new()
		head.texture = App.image("PlayerHead%02d" % player.id)
		head.size = Vector2(92, 72)
		head.position = Vector2((CARD_SIZE.x - 92) / 2.0, 22)
		head.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		head.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		holder.add_child(head)

		var badge := TextureRect.new()
		badge.texture = App.sprite("Card6", "%sType" % player.position)
		badge.position = Vector2(6, 6)
		holder.add_child(badge)

		var name := Ui.centred(player.player_name, 15)
		name.size = Vector2(CARD_SIZE.x, 24)
		name.position = Vector2(0, 100)
		holder.add_child(name)

		var faces := Ui.centred(_faces_text(player), 12)
		faces.size = Vector2(CARD_SIZE.x - 8, 60)
		faces.position = Vector2(4, 124)
		faces.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		holder.add_child(faces)
	else:
		holder.add_child(Ui.caption("EMPTY", Vector2.ZERO, CARD_SIZE, 18))

	var mark := Ui.caption(SLOT_LABELS[slot], Vector2(0, CARD_SIZE.y - 26), Vector2(CARD_SIZE.x, 26), 16)
	mark.modulate = Ui.CREAM if slot != _selected_slot else Ui.GREEN
	holder.add_child(mark)

	var hit := Button.new()
	hit.flat = true
	hit.size = CARD_SIZE
	hit.focus_mode = Control.FOCUS_NONE
	hit.pressed.connect(func() -> void: _open_picker(slot))
	holder.add_child(hit)
	return holder


func _build_bench() -> void:
	for child in _bench.get_children():
		child.queue_free()
	var career := App.career
	_bench.add_child(Ui.caption("BENCH", Vector2(0, BENCH_Y - 26), Vector2(640, 26), 18))
	for index in Career.BENCH_SIZE:
		var at := Vector2(70 + index * 130, BENCH_Y)
		var button := Ui.sheet_button("EnemyLineupSS1", "BenchButton", BENCH_SIZE_PX)
		button.position = at
		_bench.add_child(button)
		if index < career.bench.size():
			var player := App.data.player(career.bench[index])
			if player == null:
				continue
			var head := TextureRect.new()
			head.texture = App.image("PlayerHead%02d" % player.id)
			head.size = Vector2(80, 62)
			head.position = at + Vector2(20, 6)
			head.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			head.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
			head.mouse_filter = Control.MOUSE_FILTER_IGNORE
			_bench.add_child(head)
			_bench.add_child(Ui.caption(
				player.player_name, at + Vector2(-10, BENCH_SIZE_PX.y - 24), Vector2(140, 24), 14
			))


## The deck: every player owned, with the ones that fit the chosen slot on top.
func _open_picker(slot: int) -> void:
	_selected_slot = slot
	if _picker != null:
		_picker.queue_free()
	_picker = Control.new()
	_picker.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_picker)

	var shade := ColorRect.new()
	shade.color = Color(0, 0, 0, 0.72)
	shade.set_anchors_preset(Control.PRESET_FULL_RECT)
	_picker.add_child(shade)

	var title := Ui.centred(
		"YOUR SQUAD" if slot < 0 else "PICK A %s" % SLOT_LABELS[slot], 30
	)
	title.size = Vector2(640, 50)
	title.position = Vector2(0, 40)
	_picker.add_child(title)

	var scroll := ScrollContainer.new()
	scroll.position = Vector2(20, 100)
	scroll.size = Vector2(600, 740)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_picker.add_child(scroll)
	var list := VBoxContainer.new()
	list.add_theme_constant_override("separation", 4)
	scroll.add_child(list)

	var career := App.career
	for player in career.squad_players():
		list.add_child(_picker_row(player, slot))

	var close := Ui.texture_button("DoneButton", Vector2(200, 64))
	close.position = Vector2(220, 860)
	close.pressed.connect(func() -> void:
		_picker.queue_free()
		_picker = null
		_selected_slot = -1
		_refresh())
	_picker.add_child(close)
	_picker.add_child(Ui.caption("DONE", close.position, Vector2(200, 64), 24))


func _picker_row(player: PlayerRecord, slot: int) -> Control:
	var career := App.career
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	row.custom_minimum_size = Vector2(560, 48)

	var head := TextureRect.new()
	head.texture = App.image("PlayerHead%02d" % player.id)
	head.custom_minimum_size = Vector2(60, 48)
	head.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	head.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	row.add_child(head)

	var info := Ui.label("%s  %s  %s" % [
		player.position, player.player_name, _faces_text(player)
	], 17)
	info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(info)

	if player.id in career.lineup:
		row.add_child(Ui.label("PLAYING", 17, Ui.GREEN))
	elif slot >= 0 and player.fits_slot(slot):
		var pick := Ui.button("IN", 17)
		pick.custom_minimum_size = Vector2(80, 40)
		pick.pressed.connect(func() -> void: _assign(player.id))
		row.add_child(pick)
	elif player.id in career.bench:
		row.add_child(Ui.label("BENCH", 17))
	else:
		var bench := Ui.button("BENCH", 17)
		bench.custom_minimum_size = Vector2(100, 40)
		bench.disabled = career.bench.size() >= Career.BENCH_SIZE
		bench.pressed.connect(func() -> void:
			App.career.bench.append(player.id)
			App.career.save()
			_open_picker(slot))
		row.add_child(bench)
	return row


func _assign(player_id: int) -> void:
	if App.career.assign_to_slot(_selected_slot, player_id):
		_hint.text = "%s takes the %s slot." % [
			App.data.player(player_id).player_name, SLOT_LABELS[_selected_slot]
		]
		if _picker != null:
			_picker.queue_free()
			_picker = null
		_selected_slot = -1
	else:
		_hint.text = "%s cannot play there." % App.data.player(player_id).player_name
	_refresh()


func _faces_text(player: PlayerRecord) -> String:
	var labels: Array[String] = []
	for face in player.faces:
		labels.append(Dice.label(face))
	return ", ".join(labels)
