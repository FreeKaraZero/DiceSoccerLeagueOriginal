extends Control
## The match itself: two trios of dice, one encounter per PLAY ON.

const DICE_SIZE := Vector2(130, 130)
const COLUMN_X := {MatchSim.Side.HOME: 170.0, MatchSim.Side.AWAY: 470.0}
const ROW_Y := [330.0, 500.0, 670.0]
const BALL_LANE_Y := 200.0
const BALL_SIZE := Vector2(62, 62)

var _match: MatchSim
var _dice: Dictionary = {}  ## Side -> Array[TextureRect]
var _dice_labels: Dictionary = {}  ## Side -> Array[Label]
var _totals: Dictionary = {}  ## Side -> Label
var _score: Label
var _clock: Label
var _log: Label
var _ball: TextureRect
var _banner: TextureRect
var _play_on: BaseButton
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
		var labels: Array[Label] = []
		# Before kickoff the cards show who will roll them: attackers for the
		# side in possession, defenders for the other one.
		var slots: Array = MatchSim.ATTACK_SLOTS if side == _match.possession else MatchSim.DEFENCE_SLOTS
		var players := _match.lineup(side)
		for row: float in ROW_Y:
			var centre := Vector2(COLUMN_X[side], row)
			var background := TextureRect.new()
			background.texture = App.sprite("Buff6", "DiceBackground")
			background.position = centre - DICE_SIZE / 2.0
			background.size = DICE_SIZE
			add_child(background)

			var face := TextureRect.new()
			face.position = background.position
			face.size = DICE_SIZE
			add_child(face)
			faces.append(face)

			var starter: PlayerRecord = players[slots[labels.size()]] if slots[labels.size()] < players.size() else null
			var label := Ui.centred(starter.player_name if starter != null else "", 18)
			label.position = Vector2(centre.x - 130, row + DICE_SIZE.y / 2.0)
			label.size = Vector2(260, 26)
			add_child(label)
			labels.append(label)
		_dice[side] = faces
		_dice_labels[side] = labels

	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var total := Ui.centred("0", 44, Ui.GREEN if side == MatchSim.Side.HOME else Ui.RED)
		total.position = Vector2(10 if side == MatchSim.Side.HOME else 530, 690)
		total.size = Vector2(100, 60)
		add_child(total)
		_totals[side] = total


func _build_controls() -> void:
	_log = Ui.centred("Tap PLAY ON to kick off", 20)
	_log.position = Vector2(20, 760)
	_log.size = Vector2(600, 80)
	_log.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_log)

	_play_on = _textured_button(
		"PLAY ON", "PlayOnButton", Rect2(Vector2(200, 848), Vector2(240, 80))
	)
	_play_on.pressed.connect(_play_encounter)
	add_child(_play_on)
	# Keyboard confirm goes through the focused button, so it fires once.
	_play_on.grab_focus()

	var forfeit := _textured_button(
		"ABANDON", "ForfeitButton", Rect2(Vector2(20, 856), Vector2(150, 64))
	)
	forfeit.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	add_child(forfeit)

	_clock = Ui.centred("", 28)
	_clock.position = Vector2(220, 925)
	_clock.size = Vector2(200, 30)
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


func _play_encounter() -> void:
	if _match.is_finished():
		return
	var encounter := _match.play_encounter()
	if encounter.is_empty():
		return
	_show_encounter(encounter)
	_refresh()
	if _match.is_finished():
		_show_full_time()


func _show_encounter(encounter: Dictionary) -> void:
	var attacker: int = encounter["attacker"]
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var dice: Array = encounter["dice"][side]
		for index in dice.size():
			var die: Dictionary = dice[index]
			var player: PlayerRecord = die["player"]
			_dice[side][index].texture = App.texture(Dice.texture_path(int(die["face"])))
			var suffix: String = " %s" % die["modifier"] if not String(die["modifier"]).is_empty() else ""
			_dice_labels[side][index].text = "%s%s" % [
				player.player_name if player != null else "-", suffix
			]
		_totals[side].text = str(encounter["totals"][side])

	var lines: Array[String] = []
	for entry: Dictionary in encounter["triggered"]:
		var ability: AbilityRecord = entry["ability"]
		var player: PlayerRecord = entry["player"]
		lines.append("%s: %s %s (%s)" % [
			"YOU" if entry["side"] == MatchSim.Side.HOME else "THEM",
			player.player_name,
			ability.ability_name,
			Abilities.effect_label(entry["effect"]),
		])

	var shot: Dictionary = encounter["shot"]
	var banner_frame := ""
	if not shot.is_empty():
		var goal: bool = shot["goal"]
		banner_frame = "Goal" if goal else "Save"
		lines.append("%s! %d vs keeper %d" % [
			"GOAL" if goal else "SAVED",
			int(shot["striker"]["value"]),
			int(shot["goalkeeper"]["value"]),
		])
	elif encounter["winner"] == null:
		banner_frame = "Tie"
		lines.append("Tie - nothing moves, roll again")
	else:
		var winner: int = encounter["winner"]
		lines.append("%s %s the ball" % [
			_match.team_name(winner), "keeps" if winner == attacker else "wins"
		])
	_log.text = "\n".join(lines)
	_show_banner(banner_frame)


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
	_play_on.disabled = _match.is_finished()


func _show_full_time() -> void:
	if _finished:
		return
	_finished = true
	var home: int = _match.score[MatchSim.Side.HOME]
	var away: int = _match.score[MatchSim.Side.AWAY]
	var payout := App.career.record_result(home, away)

	var overlay := Ui.panel(Color(0, 0, 0, 0.97))
	overlay.position = Vector2(60, 330)
	overlay.size = Vector2(520, 300)
	add_child(overlay)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 12)
	overlay.add_child(column)
	var result := "DRAW"
	if home > away:
		result = "YOU WIN!"
	elif home < away:
		result = "YOU LOSE"
	column.add_child(Ui.centred("FULL TIME", 40))
	column.add_child(Ui.centred(result, 34, Ui.GREEN if home > away else Ui.RED))
	column.add_child(Ui.centred("%d - %d" % [home, away], 30))
	column.add_child(Ui.centred("Earned %s" % App.cash_text(payout), 26, Ui.GREEN))
	var carry_on := Ui.button("CONTINUE")
	carry_on.pressed.connect(func() -> void: App.goto(App.MENU_SCENE))
	column.add_child(carry_on)
