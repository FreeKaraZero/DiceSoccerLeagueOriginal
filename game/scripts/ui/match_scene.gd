extends Control
## The match: two trios of dice, one encounter per PLAY ON, with the superpower
## targets, the re-rolls, the pause menu and the substitutions of the original.

enum State { IDLE, CHOICE, REROLL, OVER }

const DICE_SIZE := Vector2(130, 130)
const GLOW_SIZE := Vector2(160, 160)
const COLUMN_X := {MatchSim.Side.HOME: 170.0, MatchSim.Side.AWAY: 470.0}
const ROW_Y := [330.0, 500.0, 670.0]
const BALL_LANE_Y := 200.0
const BALL_SIZE := Vector2(62, 62)

var _match: MatchSim
var _state: int = State.IDLE
var _dice: Dictionary = {}  ## Side -> Array[TextureRect]
var _glows: Dictionary = {}  ## Side -> Array[TextureRect]
var _hits: Dictionary = {}  ## Side -> Array[Button]
var _dice_labels: Dictionary = {}  ## Side -> Array[Label]
var _totals: Dictionary = {}  ## Side -> Label
var _score: Label
var _clock: Label
var _log: Label
var _ball: TextureRect
var _banner: TextureRect
var _play_on: BaseButton
var _pause_panel: Control
var _finished := false


func _ready() -> void:
	var career := App.career
	var opponent := career.next_opponent()
	if not App.assets_available() or opponent == null:
		Ui.missing_assets_screen(self)
		return

	add_child(Ui.background(_field_texture()))
	_match = MatchSim.new(
		MatchSim.lineup_from(App.data, career.lineup),
		MatchSim.lineup_from(App.data, opponent.lineup),
		App.data.abilities,
		{
			"home_name": "Your Team",
			"away_name": opponent.team_name,
			"match_seconds": Career.MATCH_MINUTES * 60,
			"bench": career.bench_players(),
			"referee": App.data.referee,
			"entourage": {
				MatchSim.Side.HOME: career.entourage_members(),
				MatchSim.Side.AWAY: App.data.entourage_members(opponent.entourage),
			},
			"bonus": {MatchSim.Side.AWAY: opponent.bonus},
		}
	)

	_build_pitch()
	_build_scoreboard()
	_build_dice()
	_build_controls()
	_refresh()


func _field_texture() -> Texture2D:
	var sheet := "Season%dField2(1.1)" % App.career.season_index
	var texture := App.sprite(sheet, "Field")
	return texture if texture != null else App.image("GreenField")


func _build_pitch() -> void:
	_ball = TextureRect.new()
	_ball.texture = App.sprite("PlayerBody3", "GameSoccerBall")
	_ball.size = BALL_SIZE
	add_child(_ball)

	_banner = TextureRect.new()
	_banner.visible = false
	_banner.position = Vector2(160, 380)
	add_child(_banner)


func _build_scoreboard() -> void:
	var board := TextureRect.new()
	board.texture = App.image("scoreboardBG")
	board.position = Vector2.ZERO
	board.size = Vector2(640, 150)
	board.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	add_child(board)

	_score = Ui.centred("0 - 0", 52, Ui.INK)
	_score.position = Vector2(220, 30)
	_score.size = Vector2(200, 60)
	add_child(_score)

	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var name_label := Ui.centred(_match.team_name(side).to_upper(), 20, Ui.INK)
		name_label.position = Vector2(20 if side == MatchSim.Side.HOME else 340, 96)
		name_label.size = Vector2(280, 30)
		add_child(name_label)


func _build_dice() -> void:
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var faces: Array[TextureRect] = []
		var glows: Array[TextureRect] = []
		var hits: Array[Button] = []
		var labels: Array[Label] = []
		for row: float in ROW_Y:
			var centre := Vector2(COLUMN_X[side], row)
			var glow := TextureRect.new()
			glow.texture = App.sprite("Buff6", "DiceGlow")
			glow.position = centre - GLOW_SIZE / 2.0
			glow.size = GLOW_SIZE
			glow.visible = false
			add_child(glow)
			glows.append(glow)

			var background := TextureRect.new()
			background.texture = App.sprite("Buff6", "DiceBackground")
			background.position = centre - DICE_SIZE / 2.0
			background.size = DICE_SIZE
			add_child(background)

			var face := TextureRect.new()
			face.position = background.position
			face.size = DICE_SIZE
			face.visible = false  ## No side shows until the dice are rolled.
			add_child(face)
			faces.append(face)

			var hit := Button.new()
			hit.flat = true
			hit.position = background.position
			hit.size = DICE_SIZE
			hit.focus_mode = Control.FOCUS_NONE
			hit.visible = false
			var index := faces.size() - 1
			hit.pressed.connect(_on_die_pressed.bind(side, index))
			add_child(hit)
			hits.append(hit)

			var label := Ui.centred("", 18)
			label.position = Vector2(centre.x - 130, row + DICE_SIZE.y / 2.0)
			label.size = Vector2(260, 26)
			add_child(label)
			labels.append(label)
		_dice[side] = faces
		_glows[side] = glows
		_hits[side] = hits
		_dice_labels[side] = labels
		_name_rollers(side)

	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var total := Ui.centred("0", 44, Ui.GREEN if side == MatchSim.Side.HOME else Ui.RED)
		total.position = Vector2(10 if side == MatchSim.Side.HOME else 530, 690)
		total.size = Vector2(100, 60)
		add_child(total)
		_totals[side] = total


## Before a roll the cards name who will hold them this encounter.
func _name_rollers(side: int) -> void:
	var slots := _match.rolling_slots(side)
	var players := _match.lineup(side)
	for index in _dice_labels[side].size():
		var label: Label = _dice_labels[side][index]
		if index >= slots.size() or int(slots[index]) >= players.size():
			label.text = ""
			continue
		var player: PlayerRecord = players[int(slots[index])]
		label.text = player.player_name if player != null else ""
	for index in _dice[side].size():
		var face: TextureRect = _dice[side][index]
		face.texture = null
		face.visible = false


func _build_controls() -> void:
	_log = Ui.centred("Tap PLAY ON to kick off", 20)
	_log.position = Vector2(20, 752)
	_log.size = Vector2(600, 88)
	_log.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_log)

	_play_on = _textured_button(
		"PLAY ON", "PlayOnButton", Rect2(Vector2(200, 848), Vector2(240, 80))
	)
	_play_on.pressed.connect(_on_play_on)
	add_child(_play_on)
	# Keyboard confirm goes through the focused button, so it fires once.
	_play_on.grab_focus()

	var pause := _textured_button(
		"PAUSE", "PauseButton", Rect2(Vector2(30, 848), Vector2(90, 80))
	)
	pause.pressed.connect(_open_pause)
	add_child(pause)

	_clock = Ui.centred("", 28)
	_clock.position = Vector2(460, 856)
	_clock.size = Vector2(150, 40)
	add_child(_clock)


## A TextureButton keeps its texture's own size unless told otherwise, which
## would let the art overlap its neighbours, so the rect is enforced here.
func _textured_button(text: String, image_name: String, rect: Rect2) -> BaseButton:
	var art := App.image(image_name)
	var button: BaseButton
	if art == null:
		button = Ui.button(text, 20)
	else:
		var textured := TextureButton.new()
		textured.texture_normal = art
		textured.texture_pressed = App.image(image_name + "Pressed")
		textured.texture_disabled = App.image(image_name + "Disabled")
		textured.ignore_texture_size = true
		textured.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		button = textured
	button.position = rect.position
	button.size = rect.size
	return button


func _on_play_on() -> void:
	match _state:
		State.IDLE:
			_roll()
		State.REROLL:
			_settle()
		State.CHOICE:
			pass  ## A superpower is waiting for a die.


func _roll() -> void:
	if _match.is_finished():
		return
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		_name_rollers(side)
	_match.start_encounter()
	if _match.encounter == null:
		return
	_show_dice()
	_report_triggers()
	_advance_state()


func _settle() -> void:
	var result := _match.settle()
	_show_dice()
	_report_result(result)
	_state = State.IDLE
	_refresh()
	if _match.is_finished():
		_show_full_time()


func _advance_state() -> void:
	var encounter := _match.encounter
	if not encounter.current_choice().is_empty():
		_state = State.CHOICE
	else:
		_state = State.REROLL
	_refresh_targets()


func _on_die_pressed(side: int, index: int) -> void:
	var encounter := _match.encounter
	if encounter == null:
		return
	if _state == State.CHOICE:
		if encounter.choose_target(side, index):
			_show_dice()
			_advance_state()
		return
	if _state == State.REROLL and encounter.can_reroll(side, index):
		encounter.use_reroll(side, index)
		_show_dice()
		_report_triggers()
		_advance_state()


## Lights up the dice the player may tap: targets of a superpower, or the dice
## that still hold a re-roll.
func _refresh_targets() -> void:
	var encounter := _match.encounter
	var allowed: Array = []
	if _state == State.CHOICE:
		allowed = encounter.choice_candidates()
	elif _state == State.REROLL and encounter != null:
		for index in _dice[MatchSim.Side.HOME].size():
			if encounter.can_reroll(MatchSim.Side.HOME, index):
				allowed.append([MatchSim.Side.HOME, index])
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		for index in _hits[side].size():
			var on := [side, index] in allowed
			_hits[side][index].visible = on
			_glows[side][index].visible = on
	if _state == State.CHOICE:
		var choice := encounter.current_choice()
		var ability: RefCounted = choice["ability"]
		_log.text = "%s: pick a die" % _ability_name(ability)


func _show_dice() -> void:
	var encounter := _match.encounter
	if encounter == null:
		return
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var dice: Array = encounter.dice[side]
		for index in _dice[side].size():
			var face: TextureRect = _dice[side][index]
			var label: Label = _dice_labels[side][index]
			if index >= dice.size():
				face.visible = false
				label.text = ""
				continue
			var die: DieState = dice[index]
			face.visible = true
			face.texture = App.texture(Dice.texture_path(die.face))
			var modifier := die.modifier_text()
			label.text = "%s%s" % [
				die.player.player_name if die.player != null else "-",
				" %s" % modifier if not modifier.is_empty() else "",
			]
		_totals[side].text = str(encounter.total(side))


func _report_triggers() -> void:
	var lines: Array[String] = []
	for entry: Dictionary in _match.encounter.triggered:
		var owner: PlayerRecord = entry["player"]
		lines.append("%s: %s %s" % [
			"YOU" if int(entry["side"]) == MatchSim.Side.HOME else "THEM",
			owner.player_name if owner != null else "Entourage",
			_ability_name(entry["ability"]),
		])
	var left := _match.encounter.rerolls_left(MatchSim.Side.HOME)
	if left > 0:
		lines.append("%d re-roll(s): tap a glowing die" % left)
	_log.text = "\n".join(lines) if not lines.is_empty() else "Tap PLAY ON to resolve"


func _report_result(result: Dictionary) -> void:
	var lines: Array[String] = []
	var shot: Dictionary = result.get("shot", {})
	var banner_frame := ""
	if not shot.is_empty():
		var goal: bool = shot["goal"]
		banner_frame = "Goal" if goal else "Save"
		lines.append("%s! %d vs keeper %d" % [
			"GOAL" if goal else "SAVED", int(shot["striker"]), int(shot["goalkeeper"])
		])
	elif result.get("winner") == null:
		banner_frame = "Tie"
		lines.append("Tie - nothing moves, roll again")
	else:
		var winner: int = result["winner"]
		lines.append("%s %s the ball" % [
			_match.team_name(winner), "keeps" if winner == int(result["attacker"]) else "wins"
		])
	if bool(result.get("through_pass", false)):
		lines.append("Through pass: the ball moves on again")
	var referee: Dictionary = result.get("referee", {})
	if not referee.is_empty():
		lines.append("Referee: %s" % String(referee.get("call", "")))
	_log.text = "\n".join(lines)
	_show_banner(banner_frame)


func _ability_name(record: RefCounted) -> String:
	if record is AbilityRecord:
		return (record as AbilityRecord).ability_name
	if record is EntourageRecord:
		return (record as EntourageRecord).ability_name
	return ""


func _show_banner(frame: String) -> void:
	if frame.is_empty():
		_banner.visible = false
		return
	var texture := App.sprite("Prompt1", frame)
	if texture == null:
		_banner.visible = false
		return
	_banner.texture = texture
	_banner.size = texture.get_size()
	_banner.position = Vector2(320, 430) - texture.get_size() / 2.0
	_banner.visible = true
	var tween := create_tween()
	_banner.modulate.a = 1.0
	tween.tween_property(_banner, "modulate:a", 0.0, 1.2)
	tween.tween_callback(func() -> void: _banner.visible = false)


func _refresh() -> void:
	_score.text = "%d - %d" % [_match.score[MatchSim.Side.HOME], _match.score[MatchSim.Side.AWAY]]
	_clock.text = _match.clock_text()
	_ball.position = Vector2(
		60 + _match.ball_zone * 130 - BALL_SIZE.x / 2.0, BALL_LANE_Y
	)
	_play_on.disabled = _match.is_finished() or _state == State.CHOICE
	_refresh_targets()


## PAUSE offers what the original did: resume, substitute, forfeit.
func _open_pause() -> void:
	if _pause_panel != null:
		return
	_pause_panel = Control.new()
	_pause_panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_pause_panel)

	var shade := ColorRect.new()
	shade.color = Color(0, 0, 0, 0.7)
	shade.set_anchors_preset(Control.PRESET_FULL_RECT)
	_pause_panel.add_child(shade)

	var title := Ui.centred("PAUSED", 40)
	title.position = Vector2(160, 240)
	title.size = Vector2(320, 60)
	_pause_panel.add_child(title)

	var rows := [
		["PLAY ON", func() -> void: _close_pause()],
		["SUBSTITUTION", func() -> void: _open_substitutions()],
		["FORFEIT", func() -> void: _forfeit()],
	]
	for index in rows.size():
		var row: Array = rows[index]
		var button := _textured_button(
			String(row[0]),
			"PauseOptionsButton",
			Rect2(Vector2(160, 340 + index * 100), Vector2(319, 80))
		)
		button.pressed.connect(row[1])
		_pause_panel.add_child(button)
		var caption := Ui.centred(String(row[0]), 26, Ui.INK)
		caption.position = Vector2(160, 360 + index * 100)
		caption.size = Vector2(319, 40)
		caption.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_pause_panel.add_child(caption)


func _close_pause() -> void:
	if _pause_panel != null:
		_pause_panel.queue_free()
		_pause_panel = null


func _forfeit() -> void:
	_close_pause()
	App.career.record_result(_match.score[MatchSim.Side.HOME], _match.score[MatchSim.Side.AWAY])
	App.goto(App.MENU_SCENE)


## Unlimited substitutions, as the handbook promises, but they cost the clock.
func _open_substitutions() -> void:
	_close_pause()
	_pause_panel = Control.new()
	_pause_panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_pause_panel)
	var shade := ColorRect.new()
	shade.color = Color(0, 0, 0, 0.8)
	shade.set_anchors_preset(Control.PRESET_FULL_RECT)
	_pause_panel.add_child(shade)

	var title := Ui.centred("SUBSTITUTIONS", 32)
	title.position = Vector2(120, 120)
	title.size = Vector2(400, 50)
	_pause_panel.add_child(title)

	if _match.bench.is_empty():
		var empty := Ui.centred("The bench is empty", 24)
		empty.position = Vector2(120, 200)
		empty.size = Vector2(400, 40)
		_pause_panel.add_child(empty)

	var row := 0
	for bench_index in _match.bench.size():
		var reserve: PlayerRecord = _match.bench[bench_index]
		for slot in Career.LINEUP_SIZE:
			var starter: PlayerRecord = _match.home_lineup[slot]
			if not reserve.fits_slot(slot):
				continue
			var button := Ui.button(
				"%s -> %s" % [reserve.player_name, starter.player_name], 20
			)
			button.position = Vector2(60, 190 + row * 62)
			button.size = Vector2(520, 56)
			button.pressed.connect(func() -> void:
				_match.substitute(slot, bench_index)
				_name_rollers(MatchSim.Side.HOME)
				_close_pause()
			)
			_pause_panel.add_child(button)
			row += 1
			break

	var back := Ui.button("BACK", 24)
	back.position = Vector2(200, 860)
	back.size = Vector2(240, 60)
	back.pressed.connect(_close_pause)
	_pause_panel.add_child(back)


func _show_full_time() -> void:
	if _finished:
		return
	_finished = true
	_state = State.OVER
	var home: int = _match.score[MatchSim.Side.HOME]
	var away: int = _match.score[MatchSim.Side.AWAY]
	var outcome := App.career.record_result(home, away, _match.stats)

	var box := TextureRect.new()
	box.texture = App.sprite("GameOver", "GameOverVictoryBox")
	box.position = Vector2(115, 180)
	box.size = Vector2(410, 600)
	add_child(box)

	var title := TextureRect.new()
	var frame := "GameOverDraw"
	if home > away:
		frame = "GameOverVictory"
	elif home < away:
		frame = "GameOverDefeat"
	title.texture = App.sprite("GameOver", frame)
	title.position = Vector2(154, 210)
	title.size = Vector2(332, 80)
	add_child(title)

	var column := VBoxContainer.new()
	column.position = Vector2(140, 320)
	column.size = Vector2(360, 380)
	column.add_theme_constant_override("separation", 10)
	add_child(column)
	column.add_child(Ui.centred("%d - %d" % [home, away], 44, Ui.INK))
	column.add_child(Ui.centred("%d star(s)" % int(outcome["stars"]), 26, Ui.INK))
	column.add_child(
		Ui.centred("Earned %s" % App.cash_text(int(outcome["payout"])), 26, Ui.INK)
	)
	for record: AchievementRecord in outcome["achievements"]:
		column.add_child(
			Ui.centred("Achievement: %s" % record.achievement_name, 22, Ui.GREEN)
		)
	var carry_on := Ui.button("CONTINUE")
	carry_on.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	column.add_child(carry_on)
