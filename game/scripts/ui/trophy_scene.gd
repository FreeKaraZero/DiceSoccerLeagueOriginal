extends Control
## The trophy room: the 72 achievements of the original on their wooden shelves,
## each with its trophy, locked until its condition is met. Tapping one reads out
## its description and its reward.

const SHELF_SIZE := Vector2(640, 220)
const TROPHY_SIZE := Vector2(150, 150)
const COLUMNS := 4
const GRID_TOP := 90.0
const DIALOG_SIZE := Vector2(410, 410)

var _grid: GridContainer
var _dialog: Control


func _ready() -> void:
	add_child(Ui.background(App.sprite("TrophyRoomBGSS", "WoodenShelfBG")))
	if not App.assets_available():
		Ui.missing_assets_screen(self)
		return

	Ui.header(self, "TROPHY ROOM", App.MENU_SCENE)

	var count := Ui.centred(
		"%d / %d" % [App.career.unlocked_achievements.size(), App.data.achievements.size()], 22
	)
	count.size = Vector2(200, 40)
	count.position = Vector2(430, 24)
	add_child(count)

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

	var ids: Array[int] = []
	for id: int in App.data.achievements:
		ids.append(id)
	ids.sort()
	for id in ids:
		_grid.add_child(_trophy(id, App.data.achievements[id]))


func _trophy(id: int, record: AchievementRecord) -> Control:
	var unlocked := App.career.achievement_unlocked(id)
	var cell := Control.new()
	cell.custom_minimum_size = TROPHY_SIZE

	var icon := TextureRect.new()
	icon.texture = App.image("Trophy%02d" % id)
	if icon.texture == null:
		icon.texture = App.image("CupBlank")
	icon.size = TROPHY_SIZE
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.modulate = Color.WHITE if unlocked else Color(0.25, 0.25, 0.25, 0.85)
	cell.add_child(icon)

	var button := Button.new()
	button.flat = true
	button.size = TROPHY_SIZE
	button.pressed.connect(func() -> void: _show(id, record))
	cell.add_child(button)
	return cell


## The original opens its long dialog box on a trophy, with the reward burst
## behind the prize it hands out.
func _show(id: int, record: AchievementRecord) -> void:
	if _dialog != null:
		_dialog.queue_free()
	_dialog = Control.new()
	_dialog.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_dialog)

	var shade := ColorRect.new()
	shade.color = Color(0, 0, 0, 0.6)
	shade.set_anchors_preset(Control.PRESET_FULL_RECT)
	_dialog.add_child(shade)

	var box := TextureRect.new()
	box.texture = App.image("DialogBox")
	box.size = DIALOG_SIZE
	box.position = Vector2((640 - DIALOG_SIZE.x) / 2.0, 260)
	box.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	box.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_dialog.add_child(box)

	var unlocked := App.career.achievement_unlocked(id)
	var title := Ui.centred(record.achievement_name.to_upper(), 26, Ui.INK)
	title.size = Vector2(340, 40)
	title.position = box.position + Vector2(35, 40)
	_dialog.add_child(title)

	var body := Ui.centred(record.unlocked_text if unlocked else record.locked_text, 20, Ui.INK)
	body.size = Vector2(320, 140)
	body.position = box.position + Vector2(45, 100)
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_dialog.add_child(body)

	var reward := Ui.centred(_reward_text(record), 20, Ui.INK)
	reward.size = Vector2(320, 60)
	reward.position = box.position + Vector2(45, 250)
	reward.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_dialog.add_child(reward)

	var close := Ui.texture_button("DoneButton", Vector2(160, 60))
	close.position = box.position + Vector2(125, 320)
	close.pressed.connect(func() -> void:
		_dialog.queue_free()
		_dialog = null)
	_dialog.add_child(close)


func _reward_text(record: AchievementRecord) -> String:
	var parts: Array[String] = []
	if record.gold_reward > 0:
		parts.append(App.cash_text(record.gold_reward))
	if record.player_reward > 0:
		var player := App.data.player(record.player_reward)
		parts.append(player.player_name if player != null else "a new player")
	if record.entourage_reward > 0:
		var member := App.data.entourage_member(record.entourage_reward)
		parts.append(member.member_name if member != null else "an entourage member")
	if record.shirt_reward > 0:
		parts.append("shirt #%d" % record.shirt_reward)
	if record.pants_reward > 0:
		parts.append("shorts #%d" % record.pants_reward)
	return "REWARD: " + ", ".join(parts) if not parts.is_empty() else "No reward"
