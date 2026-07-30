class_name Encounter
extends RefCounted
## One roll-off between two trios of dice, played exactly like the original:
## both sides roll, abilities fire on the combos they watch for, the coach picks
## the targets of his own abilities, spends the re-rolls his dice earned, and
## then PLAY ON settles the totals.

enum Phase { CHOICE, REROLL, RESOLVED }

var attacker: int
var is_shot := false
var phase: int = Phase.REROLL
var dice := {}  ## side -> Array[DieState]
var triggered: Array[Dictionary] = []
var pending: Array[Dictionary] = []
var choice_rerolls := {}  ## side -> re-rolls that may be spent on any own die
var wins_ties := {}  ## side -> bool
var through_pass := {}  ## side -> bool
var referee_due := false
var totals := {}
var winner: Variant = null
var rolls_used := 0
var team_bonus := {}  ## side -> flat bonus that entourage members add to the total
var entourage := {}  ## side -> Array[EntourageRecord] of the two at the match
var clock_bonus := 0  ## Seconds the Traffic Stopper puts back on the clock.

var _abilities: Dictionary
var _rng: RandomNumberGenerator
var _player_side: int
var _fired: Dictionary = {}  ## "side:slot:generation" -> true
var _generation := 0
var _context: Dictionary = {}  ## Match state the entourage triggers look at.


func _init(
	attacker_: int,
	home_dice: Array,
	away_dice: Array,
	abilities: Dictionary,
	rng: RandomNumberGenerator,
	options: Dictionary = {}
) -> void:
	attacker = attacker_
	is_shot = bool(options.get("shot", false))
	_player_side = int(options.get("player_side", MatchSim.Side.HOME))
	_abilities = abilities
	_rng = rng
	dice = {MatchSim.Side.HOME: home_dice, MatchSim.Side.AWAY: away_dice}
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		choice_rerolls[side] = 0
		wins_ties[side] = false
		through_pass[side] = false
		team_bonus[side] = 0
		entourage[side] = []
	_context = options.get("context", {})
	var members: Dictionary = options.get("entourage", {})
	for side: int in members:
		entourage[side] = members[side]
	_apply_passive_entourage()
	_roll_all()


func faces(side: int) -> Array[int]:
	var out: Array[int] = []
	for die: DieState in dice[side]:
		out.append(die.face)
	return out


func total(side: int) -> int:
	var sum := int(team_bonus[side])
	for die: DieState in dice[side]:
		sum += die.value()
	return maxi(sum, 0)


func rerolls_left(side: int) -> int:
	var left := int(choice_rerolls[side])
	for die: DieState in dice[side]:
		left += die.rerolls
	return left


func can_reroll(side: int, index: int) -> bool:
	if phase != Phase.REROLL or index >= dice[side].size():
		return false
	var die: DieState = dice[side][index]
	return die.rerolls > 0 or int(choice_rerolls[side]) > 0


## Spends one re-roll on the die, its own first, then the shared ones.
func use_reroll(side: int, index: int) -> void:
	if not can_reroll(side, index):
		return
	var die: DieState = dice[side][index]
	if die.rerolls > 0:
		die.rerolls -= 1
	else:
		choice_rerolls[side] = int(choice_rerolls[side]) - 1
	die.roll(_rng)
	rolls_used += 1
	_evaluate_triggers()


## The ability at the head of the queue, waiting for the coach to pick a die.
func current_choice() -> Dictionary:
	return pending[0] if not pending.is_empty() else {}


## Valid targets of the pending ability, as [side, index] pairs.
func choice_candidates() -> Array:
	var choice := current_choice()
	if choice.is_empty():
		return []
	var side: int = choice["target_side"]
	var out: Array = []
	for index in dice[side].size():
		var die: DieState = dice[side][index]
		if die.immune and bool(choice["effect"]["on_enemy"]):
			continue
		out.append([side, index])
	return out


func choose_target(side: int, index: int) -> bool:
	var choice := current_choice()
	if choice.is_empty() or side != int(choice["target_side"]):
		return false
	if not [side, index] in choice_candidates():
		return false
	pending.remove_at(0)
	_apply(choice["effect"], choice["side"], [dice[side][index]])
	if pending.is_empty():
		phase = Phase.REROLL
	return true


## Settles the encounter; the caller moves the ball.
func play_on() -> void:
	if phase == Phase.RESOLVED:
		return
	# Anything the coach left unanswered is aimed for him.
	while not pending.is_empty():
		var choice: Dictionary = pending[0]
		var candidates := choice_candidates()
		if candidates.is_empty():
			pending.remove_at(0)
			continue
		var pick: Array = _auto_pick(choice, candidates)
		choose_target(pick[0], pick[1])
	phase = Phase.RESOLVED
	totals = {
		MatchSim.Side.HOME: total(MatchSim.Side.HOME),
		MatchSim.Side.AWAY: total(MatchSim.Side.AWAY),
	}
	var home: int = totals[MatchSim.Side.HOME]
	var away: int = totals[MatchSim.Side.AWAY]
	if home > away:
		winner = MatchSim.Side.HOME
	elif away > home:
		winner = MatchSim.Side.AWAY
	elif bool(wins_ties[MatchSim.Side.HOME]) != bool(wins_ties[MatchSim.Side.AWAY]):
		winner = MatchSim.Side.HOME if bool(wins_ties[MatchSim.Side.HOME]) else MatchSim.Side.AWAY


## Plays the opponent's turn: its abilities are aimed and its re-rolls spent.
func run_opponent() -> void:
	var side := MatchSim.Side.AWAY if _player_side == MatchSim.Side.HOME else MatchSim.Side.HOME
	var guard := 12
	while rerolls_left(side) > 0 and guard > 0:
		guard -= 1
		var worst := _weakest(side)
		if worst < 0:
			break
		use_reroll(side, worst)


func _roll_all() -> void:
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		for die: DieState in dice[side]:
			die.roll(_rng)
	rolls_used += 1
	_evaluate_triggers()


## An entourage member without a combo simply helps all match long, e.g. the
## Possessive GF adding one to the total of whoever holds the ball.
func _apply_passive_entourage() -> void:
	for side: int in entourage:
		for member: EntourageRecord in entourage[side]:
			if member.trigger != EntourageRecord.Trig.NONE or not member.affects_dice():
				continue
			if int(member.effect["kind"]) != Abilities.Effect.ADD:
				continue
			if side != attacker:
				continue  ## "when your team has possession"
			team_bonus[side] = int(team_bonus[side]) + int(member.effect["amount"])


## Fires every ability whose combo is on the table. Each die may fire once per
## roll, so a re-roll gives the abilities a fresh chance, as in the original.
func _evaluate_triggers() -> void:
	_generation += 1
	for side: int in [MatchSim.Side.HOME, MatchSim.Side.AWAY]:
		var side_faces := faces(side)
		for die: DieState in dice[side]:
			var ability := _ability_of(die)
			if ability == null:
				continue
			var key := "%d:%d:%d" % [side, die.slot, _generation]
			if _fired.has(key):
				continue
			if ability.trigger == Abilities.Trigger.WIN_TIES:
				if not bool(wins_ties[side]):
					wins_ties[side] = true
					_fired[key] = true
					triggered.append(_entry(side, die, ability))
				continue
			if not Abilities.is_triggered(ability.trigger, side_faces):
				continue
			_fired[key] = true
			var effect := Abilities.parse_effect(ability)
			triggered.append(_entry(side, die, ability))
			# LocalReroll marks the abilities that roll their own die again on
			# top of their effect, as Cross does.
			if ability.local_reroll > 0:
				die.rerolls += ability.local_reroll
			_queue_or_apply(side, effect, die)
		_evaluate_entourage(side, side_faces)
	phase = Phase.CHOICE if not pending.is_empty() else Phase.REROLL


## The two members brought to the match watch the state of the roll on their own
## terms: a pair, an empty bench, a lead of more than five, the clock.
func _evaluate_entourage(side: int, side_faces: Array[int]) -> void:
	var members: Array = entourage[side]
	if dice[side].is_empty():
		return
	var other := MatchSim.Side.AWAY if side == MatchSim.Side.HOME else MatchSim.Side.HOME
	var context := _context.duplicate()
	context["own_faces"] = side_faces
	context["own_total"] = total(side)
	context["enemy_total"] = total(other)
	context["enemy_best"] = _highest(other)
	context["own_multiplier"] = _has_multiplier(side)
	if side != MatchSim.Side.HOME:
		context["goals_for"] = int(_context.get("goals_against", 0))
		context["goals_against"] = int(_context.get("goals_for", 0))
	for index in members.size():
		var member: EntourageRecord = members[index]
		var key := "e%d:%d:%d" % [side, index, _generation]
		if _fired.has(key) or not member.is_triggered(context):
			continue
		_fired[key] = true
		if member.clock_bonus > 0:
			clock_bonus += member.clock_bonus
		triggered.append({
			"side": side,
			"slot": -1 - index,
			"player": null,
			"ability": member,
			"effect": member.effect,
		})
		if member.affects_dice():
			_queue_or_apply(side, member.effect, dice[side][0])


func _highest(side: int) -> int:
	var best := 0
	for die: DieState in dice[side]:
		best = maxi(best, die.value())
	return best


func _has_multiplier(side: int) -> bool:
	for die: DieState in dice[side]:
		if die.multiplier > 1.0:
			return true
	return false


func _entry(side: int, die: DieState, ability: AbilityRecord) -> Dictionary:
	return {
		"side": side,
		"slot": die.slot,
		"player": die.player,
		"ability": ability,
		"effect": Abilities.parse_effect(ability),
	}


func _ability_of(die: DieState) -> AbilityRecord:
	if die.player == null or die.player.ability_id == 0:
		return null
	return _abilities.get(die.player.ability_id)


func _queue_or_apply(side: int, effect: Dictionary, source: DieState) -> void:
	var target_side := _target_side(side, effect)
	if Abilities.needs_target(effect):
		var choice := {
			"side": side,
			"target_side": target_side,
			"source": source,
			"effect": effect,
		}
		if side == _player_side:
			pending.append(choice)
		else:
			var candidates := _candidates_of(choice)
			if not candidates.is_empty():
				var pick: Array = _auto_pick(choice, candidates)
				_apply(effect, side, [dice[pick[0]][pick[1]]])
		return
	_apply(effect, side, _scope_dice(side, effect, source))


func _target_side(side: int, effect: Dictionary) -> int:
	var other := MatchSim.Side.AWAY if side == MatchSim.Side.HOME else MatchSim.Side.HOME
	return other if bool(effect["on_enemy"]) else side


func _scope_dice(side: int, effect: Dictionary, source: DieState) -> Array:
	match int(effect["scope"]):
		Abilities.Target.TEAM, Abilities.Target.WHOLE_TEAM:
			return dice[_target_side(side, effect)]
		Abilities.Target.ONE:
			return [source]
	return []


func _candidates_of(choice: Dictionary) -> Array:
	var side: int = choice["target_side"]
	var out: Array = []
	for index in dice[side].size():
		var die: DieState = dice[side][index]
		if die.immune and bool(choice["effect"]["on_enemy"]):
			continue
		out.append([side, index])
	return out


## Buffs go on the strongest die and penalties on the enemy's strongest die, so
## an automatic pick is as damaging as the original's suggested target.
func _auto_pick(choice: Dictionary, candidates: Array) -> Array:
	var best: Array = candidates[0]
	var best_value := -1
	for pair: Array in candidates:
		var die: DieState = dice[pair[0]][pair[1]]
		var value := die.value()
		if int(choice["effect"]["kind"]) == Abilities.Effect.REROLL:
			value = -value  # a re-roll is worth most on the weakest die
		if value > best_value:
			best_value = value
			best = pair
	return best


func _weakest(side: int) -> int:
	var chosen := -1
	var lowest := 1 << 30
	for index in dice[side].size():
		var die: DieState = dice[side][index]
		if not can_reroll(side, index):
			continue
		if die.value() < lowest:
			lowest = die.value()
			chosen = index
	return chosen


func _apply(effect: Dictionary, side: int, targets: Array) -> void:
	var kind := int(effect["kind"])
	var amount := int(effect["amount"])
	match kind:
		Abilities.Effect.THROUGH_PASS:
			through_pass[side] = true
			return
		Abilities.Effect.REFEREE:
			referee_due = true
			return
		Abilities.Effect.WIN_TIES:
			wins_ties[side] = true
			return
		Abilities.Effect.REROLL:
			choice_rerolls[side] = int(choice_rerolls[side]) + maxi(amount, 1)
			return
	for target in targets:
		var die: DieState = target
		if die.immune:
			continue
		match kind:
			Abilities.Effect.ADD:
				die.plus += amount
			Abilities.Effect.SUBTRACT:
				die.minus += amount
			Abilities.Effect.MULTIPLY:
				die.multiplier *= float(amount)
			Abilities.Effect.DIVIDE:
				die.multiplier /= float(maxi(amount, 2))
			Abilities.Effect.STRIP_BONUSES:
				die.strip_bonuses()
			Abilities.Effect.CLEAR_REDUCTIONS:
				die.clear_reductions()
			Abilities.Effect.IMMUNE:
				die.immune = true
			Abilities.Effect.REMOVE_REROLL:
				die.rerolls = 0 if amount == 0 else maxi(die.rerolls - amount, 0)
			Abilities.Effect.SWAP_BONUSES:
				die.swap_bonuses()
			Abilities.Effect.STEAL_BONUS:
				_steal(die)


## Feint: take the facing enemy die's bonus and hand it your own reduction.
func _steal(die: DieState) -> void:
	var other := MatchSim.Side.AWAY if die.side == MatchSim.Side.HOME else MatchSim.Side.HOME
	var index: int = dice[die.side].find(die)
	if index < 0 or index >= dice[other].size():
		return
	var opposite: DieState = dice[other][index]
	if opposite.immune:
		return
	die.plus += opposite.plus
	opposite.plus = 0
	opposite.minus += die.minus
	die.minus = 0
