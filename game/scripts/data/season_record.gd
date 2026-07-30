class_name SeasonRecord
extends RefCounted
## One of the five seasons: a league plus its two cups.

const LEAGUE := "League"
const CUPS := ["T1", "T2"]

var id: int
var season_name: String
## Competition name -> teams, in fixture order.
var competitions: Dictionary = {}


static func from_dict(id_: int, raw: Dictionary) -> SeasonRecord:
	var season := SeasonRecord.new()
	season.id = id_
	season.season_name = String(raw.get("Name", ""))
	if season.season_name.is_empty():
		season.season_name = "Training Season" if id_ == 0 else "Season %d" % (id_ + 1)
	for competition in [LEAGUE] + CUPS:
		var teams: Array[TeamRecord] = []
		var raw_competition: Dictionary = raw.get(competition, {})
		var fixture_ids: Array = []
		for key in raw_competition:
			if String(key).is_valid_int():
				fixture_ids.append(int(key))
		fixture_ids.sort()
		for fixture_id in fixture_ids:
			teams.append(TeamRecord.from_dict(fixture_id, raw_competition[str(fixture_id)]))
		if not teams.is_empty():
			season.competitions[competition] = teams
	return season


func teams(competition: String) -> Array[TeamRecord]:
	return competitions.get(competition, [] as Array[TeamRecord])


func has_competition(competition: String) -> bool:
	return competitions.has(competition)


func competition_names() -> Array:
	return competitions.keys()
