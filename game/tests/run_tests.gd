extends SceneTree
## Headless test suite: godot --headless --script res://tests/run_tests.gd

var _failures := 0
var _checks := 0


func _initialize() -> void:
	_test_dice()
	_test_triggers()
	_test_effects()
	_test_encounter()
	_test_match()
	_test_entourage()
	_test_achievements()
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
	equal(Dice.value(17), 0, "a dribble side scores no number of its own")
	equal(Dice.bonus(17), 2, "side 17 is the +2 dribble")
	equal(Dice.rerolls(17), 1, "a dribble side rolls again")
	equal(Dice.value(31), 0, "a wild side scores nothing")
	equal(Dice.rerolls(25), 2, "side 25 grants two re-rolls")
	check(Dice.is_wild(31), "side 31 is wild")
	check(Dice.is_referee(32), "side 32 is the referee")
	check(not Dice.is_wild(32), "the referee side is not wild")


func _test_triggers() -> void:
	check(Abilities.is_triggered(Abilities.Trigger.PAIR, [3, 3, 5] as Array[int]), "pair fires")
	check(
		not Abilities.is_triggered(Abilities.Trigger.PAIR, [3, 3, 3] as Array[int]),
		"a triple is not a pair"
	)
	check(Abilities.is_triggered(Abilities.Trigger.TRIPLE, [4, 4, 4] as Array[int]), "triple fires")
	check(
		Abilities.is_triggered(Abilities.Trigger.STRAIGHT, [2, 3, 4] as Array[int]),
		"straight fires"
	)
	check(
		Abilities.is_triggered(Abilities.Trigger.ALL_ODD, [1, 3, 5] as Array[int]), "all odd fires"
	)
	check(
		Abilities.is_triggered(Abilities.Trigger.TOTAL_EVEN, [1, 3, 4] as Array[int]),
		"even total fires"
	)
	# Trigger 24 fires on a 6: the face triggers start at 19 for a 1.
	check(Abilities.is_triggered(24, [1, 3, 6] as Array[int]), "trigger 24 wants a rolled 6")
	check(not Abilities.is_triggered(24, [1, 4, 5] as Array[int]), "trigger 24 without a 6 is mute")
	check(
		Abilities.is_triggered(Abilities.Trigger.TRIPLE, [4, 4, 31] as Array[int]),
		"a wild side completes a triple"
	)
	check(
		not Abilities.is_triggered(Abilities.Trigger.WIN_TIES, [4, 4, 4] as Array[int]),
		"win-ties is not a roll trigger"
	)


func _test_effects() -> void:
	var double := Abilities.parse_effect(_ability(
		"Choose a die and double its value. ACTIVATES when your team rolls a PAIR.",
		2,
		Abilities.Target.ONE
	))
	equal(double["kind"], Abilities.Effect.MULTIPLY, "doubling multiplies")
	equal(double["amount"], 2, "doubling is x2")
	check(Abilities.needs_target(double), "a single-die effect asks for a target")
	var sliding := Abilities.parse_effect(_ability(
		"Subtracts 3 from all opponent dice. ACTIVATES when your team rolls a PAIR.",
		2,
		Abilities.Target.TEAM
	))
	equal(sliding["kind"], Abilities.Effect.SUBTRACT, "subtraction is recognised")
	equal(sliding["amount"], 3, "the operand comes from the effect clause, not the trigger")
	equal(sliding["on_enemy"], true, "opponent dice are targeted")
	check(not Abilities.needs_target(sliding), "a whole team needs no pick")
	# Tackle names no side; a penalty must not fall on the owner's own trio.
	var tackle := Abilities.parse_effect(
		_ability("Reduces a die's value by 1. ACTIVATES when your team rolls a PAIR.", 2, 1)
	)
	equal(tackle["on_enemy"], true, "an unqualified penalty hits the opponent")
	var wall := Abilities.parse_effect(_ability(
		"Adds 1 to all players on your team. ACTIVATES when your team rolls a TRIPLE.", 6, 3
	))
	equal(wall["kind"], Abilities.Effect.ADD, "a team buff adds")
	equal(wall["on_enemy"], false, "'your team' stays at home")
	equal(
		Abilities.parse_effect(_ability("Re-roll any TWO dice of your choice.", 0, 1))["amount"],
		2,
		"two picks are two re-rolls"
	)
	equal(
		Abilities.parse_effect(_ability("Roll the Referee die after this play.", 0, 0))["kind"],
		Abilities.Effect.REFEREE,
		"Dive! calls the referee"
	)
	equal(
		Abilities.parse_effect(_ability("Instantly move forward! ACTIVATES on POSSESSION.", 3, 1))[
			"kind"
		],
		Abilities.Effect.THROUGH_PASS,
		"a through pass moves the ball"
	)
	equal(
		Abilities.parse_effect(_ability("Your team will win any turn that ends in a tie.", 1, 6))[
			"kind"
		],
		Abilities.Effect.WIN_TIES,
		"the captain wins ties"
	)
	equal(
		Abilities.parse_effect(
			_ability("Make one dice immune to ALL abilities. Triggers on a PAIR.", 2, 1)
		)["kind"],
		Abilities.Effect.IMMUNE,
		"Anchor makes a die immune"
	)


## The player picks the die a superpower lands on; the opponent's picks itself.
func _test_encounter() -> void:
	var tackle := _ability(
		"Reduces a die's value by 2. ACTIVATES when your team rolls a TRIPLE.",
		Abilities.Trigger.TRIPLE,
		Abilities.Target.ONE
	)
	var contest := MatchSim.new(
		_team([4], 7), _team([4]), {7: tackle}, {"seed": 3, "match_seconds": 600}
	)
	var encounter := contest.start_encounter()
	equal(encounter.attacker, MatchSim.Side.HOME, "the kickoff side attacks")
	equal(encounter.total(MatchSim.Side.AWAY), 12, "three fours total 12")
	equal(encounter.phase, Encounter.Phase.CHOICE, "the pair asks the coach for a die")
	equal(encounter.choice_candidates().size(), 3, "any enemy die may be tackled")
	check(
		encounter.choose_target(MatchSim.Side.AWAY, 0), "the coach may tackle an opponent die"
	)
	equal(encounter.total(MatchSim.Side.HOME), 12, "the owner's own trio is untouched")
	equal(encounter.total(MatchSim.Side.AWAY), 10, "the penalty lands on the other trio")
	var settled := contest.settle()
	equal(settled["winner"], MatchSim.Side.HOME, "the ability wins the encounter")
	equal(contest.ball_zone, MatchSim.CENTRE_ZONE + 1, "the winner moves the ball")
	equal(
		contest.seconds_remaining,
		600 - MatchSim.SECONDS_PER_ROLL,
		"a roll costs a minute of the clock"
	)


func _test_match() -> void:
	var strong := _team([9])
	var weak := _team([1])
	var even := MatchSim.new(_team([4]), _team([4]), {}, {"seed": 2, "match_seconds": 600})
	even.start_encounter()
	var tie := even.settle()
	equal(tie["winner"], null, "equal totals are a tie")
	equal(even.ball_zone, MatchSim.CENTRE_ZONE, "a tie does not move the ball")

	var scorer := MatchSim.new(strong, weak, {}, {"seed": 4, "match_seconds": 6000})
	var goals := 0
	var shots := 0
	while not scorer.is_finished():
		var running := scorer.start_encounter()
		if running == null:
			break
		if running.is_shot:
			shots += 1
			equal(running.dice[scorer.possession].size(), 1, "a shot is one striker")
		var played := scorer.settle()
		var shot: Dictionary = played["shot"]
		if not shot.is_empty() and bool(shot["goal"]):
			goals += 1
	check(shots > 0, "the ball reaches the goal line")
	check(goals > 0, "the stronger team scores")
	equal(scorer.score[MatchSim.Side.HOME], goals, "the scoreboard counts the goals")
	equal(scorer.start_encounter(), null, "a finished match plays no more encounters")
	check(int(scorer.stats["best_roll"]) >= 27, "the best roll of the match is kept")

	var bench_side := MatchSim.new(
		strong, weak, {}, {"seed": 5, "match_seconds": 600, "bench": _team([2])}
	)
	check(bench_side.substitute(6, 6), "a midfielder reserve may come on")
	equal(bench_side.home_lineup[6].faces[0], 2, "the reserve is now in the lineup")


func _test_entourage() -> void:
	var cheerleader := EntourageRecord.from_dict(1, {
		"Name": "Mary Hail",
		"Ability Name": "Ex-Cheerleader",
		"Position": "WAG",
		"Ability Trigger": EntourageRecord.Trig.TRIPLE,
		"In Game Description": "Adds +5 when you roll a triple.",
	})
	equal(cheerleader.effect["kind"], Abilities.Effect.ADD, "the cheerleader adds")
	equal(cheerleader.effect["amount"], 5, "she adds five")
	check(
		cheerleader.is_triggered({"own_faces": [4, 4, 4] as Array[int]}), "a triple wakes her up"
	)
	check(
		not cheerleader.is_triggered({"own_faces": [4, 4, 5] as Array[int]}),
		"a pair does not wake her up"
	)
	var mascot := EntourageRecord.from_dict(7, {
		"Name": "Dicey Dave",
		"Ability Name": "Team Mascot",
		"Ability Trigger": EntourageRecord.Trig.ENEMY_LEAD,
		"In Game Description": "If the enemy beats you by more than five, double one dice.",
	})
	equal(mascot.threshold, 5, "the mascot waits for a five-goal deficit on the roll")
	check(
		mascot.is_triggered({"own_total": 10, "enemy_total": 16, "own_faces": [] as Array[int]}),
		"a six point deficit wakes the mascot"
	)
	check(
		not mascot.is_triggered({"own_total": 10, "enemy_total": 15, "own_faces": [] as Array[int]}),
		"exactly five is not more than five"
	)
	var investor := EntourageRecord.from_dict(11, {
		"Name": "Sheikh Abdullah I",
		"Ability Trigger": 0,
		"In Game Description": "Increases reward for Victory by 10,000.",
	})
	equal(investor.win_bonus, 10000, "the investor pays for a win")
	check(not investor.affects_dice(), "the investor never touches the dice")

	var possessive := EntourageRecord.from_dict(2, {
		"Name": "Callie McLooney",
		"Ability Trigger": 0,
		"In Game Description": "Adds +1 to your total when your team has possession.",
	})
	var backed := MatchSim.new(
		_team([4]),
		_team([4]),
		{},
		{
			"seed": 9,
			"match_seconds": 600,
			"entourage": {MatchSim.Side.HOME: [possessive] as Array},
		}
	)
	var encounter := backed.start_encounter()
	equal(encounter.total(MatchSim.Side.HOME), 13, "the possessive girlfriend adds one")
	equal(backed.settle()["winner"], MatchSim.Side.HOME, "her point wins the encounter")


func _test_achievements() -> void:
	var promoted := AchievementRecord.from_dict(3, {
		"Name": "Promoted!",
		"InHouseDescription": "Qualify for Season 2",
		"PlayerReward": 41,
	})
	equal(promoted.kind, AchievementRecord.Kind.QUALIFY, "qualifying is a condition")
	equal(promoted.amount, 1, "Season 2 is season index 1")
	var champ := AchievementRecord.from_dict(2, {
		"Name": "Training Champ",
		"InHouseDescription": "Win All Matches in Training Season",
	})
	equal(champ.kind, AchievementRecord.Kind.WIN_ALL_LEAGUE, "winning a league is a condition")
	equal(champ.amount, 0, "the training season is season 0")
	var cup := AchievementRecord.from_dict(9, {
		"Name": "Big Game Hunter",
		"InHouseDescription": "Win Season 2 Tournament 1",
		"GoldReward": 500000,
	})
	equal(cup.kind, AchievementRecord.Kind.WIN_CUP, "winning a cup is a condition")
	equal(cup.amount, 2, "it is the second season")
	equal(cup.detail, SeasonRecord.CUPS[0], "and the first cup")
	var roll := AchievementRecord.from_dict(46, {
		"Name": "Roll for the Sole",
		"InHouseDescription": "Roll a 40+",
	})
	equal(roll.kind, AchievementRecord.Kind.ROLL, "a big roll is a condition")
	equal(roll.amount, 40, "of forty")
	var hoard := AchievementRecord.from_dict(34, {
		"Name": "Financially Stable",
		"InHouseDescription": "Hoard 10,000 in Bank",
	})
	equal(hoard.kind, AchievementRecord.Kind.HOARD, "hoarding is a condition")
	equal(hoard.amount, 10000, "of ten thousand")


func _test_data() -> void:
	var data := GameData.load_default()
	if not data.is_loaded():
		print("skipping data tests: run tools/extract_assets.py first")
		return
	check(data.players.size() > 80, "the full player table is loaded")
	check(data.abilities.size() > 50, "the ability table is loaded")
	equal(data.seasons.size(), 5, "all five seasons are loaded")
	equal(data.entourage.size(), 21, "the whole entourage is loaded")
	equal(data.achievements.size(), 72, "all 72 achievements are loaded")
	check(data.kits.size() > 20, "the kits are loaded")
	check(data.cash_packs.size() >= 7, "the coin shop is stocked")
	check(data.referee != null and data.referee.faces.size() == 6, "the referee die has six sides")
	equal(data.initial_roster.size(), Career.LINEUP_SIZE, "the opening roster is a full line-up")
	check(not data.initial_entourage.is_empty(), "the career starts with an entourage member")
	var keeper := data.player(data.initial_roster[0])
	check(
		keeper != null and keeper.position == PlayerRecord.GK,
		"the first roster slot is a goalkeeper"
	)
	check(not data.player(1).faces.is_empty(), "players have die faces")
	var first_opponent := data.season(0).teams(SeasonRecord.LEAGUE)[0]
	equal(first_opponent.team_name, "The Neighbours", "the first training fixture is The Neighbours")
	check(data.buyable_players().size() > 10, "the shop has stock")
	# Every ability of the original must resolve to something the engine applies.
	var unresolved: Array[String] = []
	for id: int in data.abilities:
		var record: AbilityRecord = data.abilities[id]
		if int(Abilities.parse_effect(record)["kind"]) == Abilities.Effect.NONE:
			unresolved.append("%d %s" % [id, record.ability_name])
	equal(unresolved, [] as Array[String], "every superpower parses into an effect")
	var unresolved_members: Array[String] = []
	for id: int in data.entourage:
		var member: EntourageRecord = data.entourage[id]
		if member.affects_dice() or member.win_bonus > 0 or member.draw_pays_win:
			continue
		if member.wager > 0 or member.clock_bonus > 0:
			continue
		unresolved_members.append("%d %s" % [id, member.member_name])
	equal(unresolved_members, [] as Array[String], "every entourage member does something")


func _test_career() -> void:
	var data := GameData.load_default()
	if not data.is_loaded():
		return
	var career := Career.new(data)
	check(career.lineup_is_valid(), "the opening line-up is legal")
	check(career.next_opponent() != null, "the first fixture exists")
	equal(career.entourage_slots.size(), 1, "the career starts with one member at the match")
	var fixtures := career.fixtures().size()
	var outcome := career.record_result(3, 0)
	check(int(outcome["payout"]) > 0, "a win pays out")
	equal(career.points, Career.STARS_WIN, "a win is worth three stars")
	equal(career.cash, int(outcome["payout"]), "the payout lands in the bank")
	while not career.season_finished():
		career.record_result(2, 0)
	equal(career.points, fixtures * Career.STARS_WIN, "every win counts")
	check(career.qualified(), "winning everything qualifies")
	check(career.achievement_unlocked(2), "winning the training season is an achievement")
	check(career.owns(16), "its reward player joins the squad")
	check(career.advance_season(), "qualifying opens the next season")
	equal(career.season_index, 1, "the next season is current")
	equal(career.points, 0, "stars reset each season")

	career.select_competition(SeasonRecord.CUPS[0])
	equal(career.competition, SeasonRecord.CUPS[0], "the first cup can be entered")
	career.record_result(0, 2)
	check(career.cup_eliminated(), "losing a cup match ends the run")
	check(not career.can_play(), "an eliminated cup offers no fixture")
	career.select_competition(SeasonRecord.LEAGUE)
	check(career.can_play(), "the league carries on")

	var goalkeeper_slot_taken := career.assign_to_slot(0, career.lineup[4])
	check(not goalkeeper_slot_taken, "a forward cannot keep goal")
	var member_id: int = data.entourage.keys().min()
	check(career.equip_entourage(member_id) or member_id in career.entourage_slots, "a member can be brought along")
	check(career.entourage_slots.size() <= Career.ENTOURAGE_SLOTS, "at most two members per match")
	career.delete_save()


func _ability(description: String, trigger: int, target := 1) -> AbilityRecord:
	return AbilityRecord.from_dict(1, {
		"Name": "Test",
		"AbilityDescription": description,
		"BuffTrigger": trigger,
		"BuffTarget": target,
	})


## Seven identical players, so totals are predictable. Any ability is given to
## the central forward alone, so it fires exactly once per encounter.
func _team(faces: Array, ability_id := 0) -> Array[PlayerRecord]:
	var players: Array[PlayerRecord] = []
	for slot in Career.LINEUP_SIZE:
		players.append(PlayerRecord.from_dict(slot, {
			"Name": "Player %d" % slot,
			"Position": PlayerRecord.GK if slot == 0 else PlayerRecord.MID,
			"DiceSides": ",".join(faces.map(func(face: int) -> String: return str(face))),
			"Superpower": ability_id if slot == MatchSim.CENTRAL_ATTACK_SLOT else 0,
		}))
	return players
