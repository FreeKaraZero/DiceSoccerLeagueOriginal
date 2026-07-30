extends SceneTree
## Headless test suite: godot --headless --script res://tests/run_tests.gd

var _failures := 0
var _checks := 0


func _initialize() -> void:
	_test_dice()
	_test_triggers()
	_test_effects()
	_test_match()
	_test_data()
	_test_career()
	print("%d checks, %d failed" % [_checks, _failures])
	quit(1 if _failures > 0 else 0)


func check(condition: bool, what: String) -> void:
	_checks += 1
	if not condition:
		_failures += 1
		printerr("FAIL: %s" % what)


func equal(actual: Variant, expected: Variant, what: String) -> void:
	_checks += 1
	if actual != expected:
		_failures += 1
		printerr("FAIL: %s (got %s, expected %s)" % [what, actual, expected])


func _test_dice() -> void:
	equal(Dice.value(7), 7, "numeric side keeps its value")
	equal(Dice.value(17), 2, "side 17 is the +2 bonus")
	equal(Dice.value(31), 0, "a wild side scores nothing")
	equal(Dice.rerolls(25), 2, "side 25 grants two re-rolls")
	check(Dice.is_wild(32), "side 32 is wild")
	equal(Dice.label(24), "re-roll x1", "re-roll sides are labelled")


func _test_triggers() -> void:
	check(Abilities.is_triggered(Abilities.Trigger.PAIR, [3, 3, 5] as Array[int]), "pair fires")
	check(not Abilities.is_triggered(Abilities.Trigger.PAIR, [3, 3, 3] as Array[int]), "a triple is not a pair")
	check(Abilities.is_triggered(Abilities.Trigger.TRIPLE, [4, 4, 4] as Array[int]), "triple fires")
	check(Abilities.is_triggered(Abilities.Trigger.STRAIGHT, [2, 3, 4] as Array[int]), "straight fires")
	check(Abilities.is_triggered(Abilities.Trigger.ALL_ODD, [1, 3, 5] as Array[int]), "all odd fires")
	check(Abilities.is_triggered(Abilities.Trigger.TOTAL_EVEN, [1, 3, 4] as Array[int]), "even total fires")
	check(Abilities.is_triggered(21, [1, 3, 6] as Array[int]), "trigger 21 wants a rolled 3")
	check(not Abilities.is_triggered(21, [1, 4, 6] as Array[int]), "trigger 21 without a 3 stays quiet")
	check(Abilities.is_triggered(Abilities.Trigger.TRIPLE, [4, 4, 31] as Array[int]), "a wild side completes a triple")
	check(not Abilities.is_triggered(Abilities.Trigger.WIN_TIES, [4, 4, 4] as Array[int]), "win-ties is not a roll trigger")


func _test_effects() -> void:
	var double := _ability("Choose a die and double its value. ACTIVATES when your team rolls a PAIR.", 2)
	equal(Abilities.parse_effect(double)["kind"], Abilities.Effect.DOUBLE, "doubling is recognised")
	var subtract := _ability("Subtracts 3 from all opponent dice. ACTIVATES when your team rolls a 1.", 19)
	var subtract_effect := Abilities.parse_effect(subtract)
	equal(subtract_effect["kind"], Abilities.Effect.SUBTRACT, "subtraction is recognised")
	equal(subtract_effect["amount"], 3, "the operand comes from the effect clause, not the trigger")
	equal(subtract_effect["on_enemy"], true, "opponent dice are targeted")
	var reroll := _ability("Re-roll any one die of your choice. ACTIVATES when your team rolls a 1.", 19)
	equal(Abilities.parse_effect(reroll)["kind"], Abilities.Effect.REROLL, "re-rolls are recognised")
	var ties := _ability("Your team wins all ties.", Abilities.Trigger.WIN_TIES)
	equal(Abilities.parse_effect(ties)["kind"], Abilities.Effect.WIN_TIES, "tie breaking is recognised")


func _test_match() -> void:
	var strong := _team([9])
	var weak := _team([1])
	var contest := MatchSim.new(strong, weak, {}, {"seed": 1, "match_seconds": 600})
	var encounter := contest.play_encounter()
	equal(encounter["attacker"], MatchSim.Side.HOME, "the kickoff side attacks")
	equal(encounter["totals"][MatchSim.Side.HOME], 27, "three nines total 27")
	equal(encounter["totals"][MatchSim.Side.AWAY], 3, "three ones total 3")
	equal(encounter["winner"], MatchSim.Side.HOME, "the higher total wins")
	equal(contest.ball_zone, MatchSim.CENTRE_ZONE + 1, "the ball moves one zone")
	equal(contest.seconds_remaining, 600 - MatchSim.SECONDS_PER_ENCOUNTER, "an encounter costs a minute")

	var even := MatchSim.new(_team([4]), _team([4]), {}, {"seed": 2, "match_seconds": 600})
	var tie := even.play_encounter()
	equal(tie["winner"], null, "equal totals are a tie")
	equal(even.ball_zone, MatchSim.CENTRE_ZONE, "a tie does not move the ball")

	var scorer := MatchSim.new(strong, weak, {}, {"seed": 4, "match_seconds": 6000})
	var goals := 0
	while not scorer.is_finished():
		var played := scorer.play_encounter()
		var shot: Dictionary = played["shot"]
		if not shot.is_empty() and bool(shot["goal"]):
			goals += 1
	check(goals > 0, "the stronger team scores")
	equal(scorer.score[MatchSim.Side.HOME], goals, "the scoreboard counts the goals")
	equal(scorer.play_encounter(), {}, "a finished match plays no more encounters")


func _test_data() -> void:
	var data := GameData.load_default()
	if not data.is_loaded():
		print("skipping data tests: run tools/extract_assets.py first")
		return
	check(data.players.size() > 80, "the full player table is loaded")
	check(data.abilities.size() > 50, "the ability table is loaded")
	equal(data.seasons.size(), 5, "all five seasons are loaded")
	equal(data.initial_roster.size(), Career.LINEUP_SIZE, "the opening roster is a full line-up")
	var keeper := data.player(data.initial_roster[0])
	check(keeper != null and keeper.position == PlayerRecord.GK, "the first roster slot is a goalkeeper")
	check(not data.player(1).faces.is_empty(), "players have die faces")
	var first_opponent := data.season(0).teams(SeasonRecord.LEAGUE)[0]
	equal(first_opponent.team_name, "The Neighbours", "the first training fixture is The Neighbours")
	check(data.buyable_players().size() > 10, "the shop has stock")


func _test_career() -> void:
	var data := GameData.load_default()
	if not data.is_loaded():
		return
	var career := Career.new(data)
	check(career.lineup_is_valid(), "the opening line-up is legal")
	check(career.next_opponent() != null, "the first fixture exists")
	var fixtures := career.fixtures().size()
	var payout := career.record_result(3, 0)
	check(payout > 0, "a win pays out")
	equal(career.points, Career.POINTS_WIN, "a win is worth three stars")
	equal(career.cash, payout, "the payout lands in the bank")
	while not career.season_finished():
		career.record_result(2, 0)
	equal(career.points, fixtures * Career.POINTS_WIN, "every win counts")
	check(career.qualified(), "winning everything qualifies")
	check(career.advance_season(), "qualifying opens the next season")
	equal(career.season_index, 1, "the next season is current")
	equal(career.points, 0, "stars reset each season")

	var goalkeeper_slot_taken := career.assign_to_slot(0, career.lineup[4])
	check(not goalkeeper_slot_taken, "a forward cannot keep goal")
	career.delete_save()


func _ability(description: String, trigger: int) -> AbilityRecord:
	return AbilityRecord.from_dict(1, {
		"Name": "Test",
		"AbilityDescription": description,
		"BuffTrigger": trigger,
	})


## Seven identical players, so totals are predictable.
func _team(faces: Array) -> Array[PlayerRecord]:
	var players: Array[PlayerRecord] = []
	for slot in Career.LINEUP_SIZE:
		players.append(PlayerRecord.from_dict(slot, {
			"Name": "Player %d" % slot,
			"Position": PlayerRecord.GK if slot == 0 else PlayerRecord.MID,
			"DiceSides": ",".join(faces.map(func(face: int) -> String: return str(face))),
		}))
	return players
