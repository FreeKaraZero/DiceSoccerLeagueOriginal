using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiceSoccer
{
    public enum GoalCelebrationType
    {
        Player = 0,
        Penguin = 1,
        Streaker = 2
    }

    public enum EncounterType
    {
        PenaltyShot,
        OutfieldBattle,
        MidfieldDuel
    }

    public sealed class MatchSimulation
    {
        public const int GoalkeeperSlot = 0;
        public const int CentralForwardSlot = 5;
        public const int MinLocation = -2;
        public const int MaxLocation = 2;
        public const int CentreZone = 2;
        public const int LastZone = 4;
        public const int MaximumExtraRollsPerDie = 5;

        private static readonly int[] AttackSlots = { 4, 5, 6 };
        private static readonly int[] DefenceSlots = { 1, 2, 3 };

        // Season match durations verified in GameFieldLayer.initWithParent: (0x06b8e0)
        private static readonly int[] SeasonDurationsSeconds =
        {
            15 * 60, // Season 0 (Training Season): 15:00 (0x0f minutes)
            20 * 60, // Season 1: 20:00 (0x14 minutes)
            25 * 60, // Season 2: 25:00 (0x19 minutes)
            30 * 60, // Season 3: 30:00 (0x1e minutes)
            35 * 60  // Season 4: 35:00 (0x23 minutes)
        };

        private readonly PlayerData[] _home;
        private readonly PlayerData[] _away;
        private readonly int _seasonIndex;
        private readonly int[] _homeEntourages;
        private readonly int[] _awayEntourages;
        private readonly bool[] _homeYellowCards = new bool[7];
        private readonly bool[] _homeRedCards = new bool[7];
        private readonly bool[] _awayYellowCards = new bool[7];
        private readonly bool[] _awayRedCards = new bool[7];

        private int _location; // -2 to +2 (0 = midfield)
        private int _possession; // 0 = Home (Red), 1 = Away (Blue)
        private PendingEncounter _pendingEncounter;

        public int HomeScore { get; private set; }
        public int AwayScore { get; private set; }
        public int SecondsRemaining { get; private set; }
        public int Possession => _possession;
        public int BallHolderSlotIndex { get; private set; } = 1; // 0 = bottom, 1 = center, 2 = top
        public bool IsFinished => SecondsRemaining <= 0;

        // BallZone 0..4 maps location (-2..+2) + 2 for 100% backward compatibility with HUD
        public int BallZone => _location + 2;
        public int Location => _location;

        public string HomeName => "Dice Soccer High";
        public string AwayName { get; }
        public int[] LastHomeFaces { get; private set; } = Array.Empty<int>();
        public int[] LastAwayFaces { get; private set; } = Array.Empty<int>();
        public int LastHomeTotal { get; private set; }
        public int LastAwayTotal { get; private set; }
        public bool CanBeginEncounter => !IsFinished && _pendingEncounter == null;
        public EncounterType CurrentEncounterType { get; private set; } = EncounterType.MidfieldDuel;
        public GoalCelebrationType LastGoalCelebration { get; private set; } = GoalCelebrationType.Player;
        public bool HomeWinOnTie { get; set; }
        public bool AwayWinOnTie { get; set; }
        public int HomeExtraBonus { get; set; }
        public int AwayExtraBonus { get; set; }

        public void AdjustHomeTotal(int delta)
        {
            if (_pendingEncounter != null)
            {
                _pendingEncounter.HomeTotal = Math.Max(0, _pendingEncounter.HomeTotal + delta);
                LastHomeTotal = _pendingEncounter.HomeTotal;
            }
        }

        public void AdjustAwayTotal(int delta)
        {
            if (_pendingEncounter != null)
            {
                _pendingEncounter.AwayTotal = Math.Max(0, _pendingEncounter.AwayTotal + delta);
                LastAwayTotal = _pendingEncounter.AwayTotal;
            }
        }

        public MatchSimulation(
            PlayerData[] home,
            PlayerData[] away,
            string awayName,
            int seasonIndex = 0,
            int[] homeEntourages = null,
            int[] awayEntourages = null,
            int homePossessionBonus = 0,
            int awayPossessionBonus = 0)
        {
            ValidateLineup(home, "home");
            ValidateLineup(away, "away");
            _home = (PlayerData[])home.Clone();
            _away = (PlayerData[])away.Clone();
            AwayName = string.IsNullOrWhiteSpace(awayName) ? "Opponents" : awayName;
            _seasonIndex = Mathf.Clamp(seasonIndex, 0, SeasonDurationsSeconds.Length - 1);
            SecondsRemaining = SeasonDurationsSeconds[_seasonIndex];
            _location = 0; // Starts at midfield
            _possession = 0; // Home kicks off
            _homeEntourages = homeEntourages ?? Array.Empty<int>();
            _awayEntourages = awayEntourages ?? Array.Empty<int>();
            HomeExtraBonus = homePossessionBonus;
            AwayExtraBonus = awayPossessionBonus;

            // Check Entourage 16 (Flashy Spice - Win on tie)
            if (Array.IndexOf(_homeEntourages, 16) >= 0) HomeWinOnTie = true;
            if (Array.IndexOf(_awayEntourages, 16) >= 0) AwayWinOnTie = true;
        }

        public void SubstituteHomePlayer(int slot, PlayerData replacement)
        {
            if (IsFinished || _pendingEncounter != null)
                throw new InvalidOperationException("Players can only be substituted between encounters.");
            if (slot < 0 || slot >= _home.Length)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (replacement == null || replacement.diceSides == null || replacement.diceSides.Length == 0)
                throw new ArgumentException("The replacement player has no valid die sides.", nameof(replacement));
            if (Array.Exists(_home, player => player.id == replacement.id))
                throw new InvalidOperationException("The replacement player is already on the field.");
            if (slot == GoalkeeperSlot
                ? replacement.position != "GK"
                : replacement.position == "GK" ||
                  (replacement.position != "MID" &&
                   replacement.position != (slot <= 3 ? "DEF" : "FWD")))
                throw new InvalidOperationException(
                    $"Player {replacement.name} cannot fill lineup slot {slot}.");

            _home[slot] = replacement;
            _homeYellowCards[slot] = false;
            _homeRedCards[slot] = false;
        }

        public void ForfeitMatch()
        {
            if (IsFinished) return;
            SecondsRemaining = 0;
            if (HomeScore >= AwayScore)
                AwayScore = HomeScore + 1;
            _pendingEncounter = null;
        }

        public void BeginEncounter()
        {
            if (IsFinished)
                throw new InvalidOperationException("The match has already ended.");
            if (_pendingEncounter != null)
                throw new InvalidOperationException("The current encounter has not been resolved.");

            // Verified 5-location dispatch from rollDice (0x06fbf4, tbh 0x06fdaa):
            int[] homeSlots;
            int[] awaySlots;
            bool isShot = false;

            switch (_location)
            {
                case -2: // Red Penalty Box (Shot on Red Goal)
                    homeSlots = new[] { GoalkeeperSlot }; // Red GK [0]
                    awaySlots = new[] { CentralForwardSlot }; // Blue CF [5]
                    CurrentEncounterType = EncounterType.PenaltyShot;
                    isShot = true;
                    break;
                case -1: // Red Defensive Half
                    homeSlots = DefenceSlots; // Red Defenders [1, 2, 3]
                    awaySlots = AttackSlots; // Blue Attackers [4, 5, 6]
                    CurrentEncounterType = EncounterType.OutfieldBattle;
                    break;
                case 0: // Midfield Duel
                    homeSlots = AttackSlots; // Red Midfielders [4, 5, 6]
                    awaySlots = AttackSlots; // Blue Midfielders [4, 5, 6]
                    CurrentEncounterType = EncounterType.MidfieldDuel;
                    break;
                case 1: // Blue Defensive Half
                    homeSlots = AttackSlots; // Red Attackers [4, 5, 6]
                    awaySlots = DefenceSlots; // Blue Defenders [1, 2, 3]
                    CurrentEncounterType = EncounterType.OutfieldBattle;
                    break;
                case 2: // Blue Penalty Box (Shot on Blue Goal)
                    homeSlots = new[] { CentralForwardSlot }; // Red CF [5]
                    awaySlots = new[] { GoalkeeperSlot }; // Blue GK [0]
                    CurrentEncounterType = EncounterType.PenaltyShot;
                    isShot = true;
                    break;
                default:
                    homeSlots = AttackSlots;
                    awaySlots = AttackSlots;
                    CurrentEncounterType = EncounterType.MidfieldDuel;
                    break;
            }

            var homeRoll = RollSlots(_home, homeSlots, _homeRedCards, _possession == 0 ? HomeExtraBonus : 0);
            var awayRoll = RollSlots(_away, awaySlots, _awayRedCards, _possession == 1 ? AwayExtraBonus : 0);

            SetLastRolls(0, (homeRoll.Rolls, homeRoll.Total, homeRoll.Faces),
                         1, (awayRoll.Rolls, awayRoll.Total, awayRoll.Faces));

            _pendingEncounter = new PendingEncounter(
                isShot,
                _possession,
                homeRoll.Total,
                awayRoll.Total,
                homeRoll.Faces,
                awayRoll.Faces,
                homeSlots,
                awaySlots,
                FinalFaces(homeRoll.Rolls),
                FinalFaces(awayRoll.Rolls));
        }

        public string CompleteEncounter()
        {
            if (_pendingEncounter == null)
                throw new InvalidOperationException("There is no encounter waiting to be resolved.");

            var encounter = _pendingEncounter;
            _pendingEncounter = null;

            string report;
            var homeTotal = encounter.HomeTotal;
            var awayTotal = encounter.AwayTotal;

            // Check tie breaker abilities
            bool homeWinsTie = HomeWinOnTie && !AwayWinOnTie;
            bool awayWinsTie = AwayWinOnTie && !HomeWinOnTie;

            if (encounter.IsShot)
            {
                report = ResolveShotEncounter(encounter, homeWinsTie, awayWinsTie);
            }
            else
            {
                report = ResolveOutfieldEncounter(encounter, homeWinsTie, awayWinsTie);
            }

            // Time deduction verified in GameFieldLayer.redPlayerFinishRolling: (0x070ca0..0x070d0a):
            // dt = 30 + (arc4random() % 30) seconds (range: 30 to 59 seconds elapsed per turn)
            var elapsedSeconds = 30 + UnityEngine.Random.Range(0, 30);
            SecondsRemaining = Math.Max(0, SecondsRemaining - elapsedSeconds);

            return report + $"  {ClockText()}";
        }

        public string ClockText()
        {
            var seconds = Math.Max(0, SecondsRemaining);
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public int CalculateStars()
        {
            // Verified in StatsManager.updateStatsAtEndOfMatch: (0x07d160)
            if (HomeScore < AwayScore) return 1; // Loss: 1 star
            if (HomeScore == AwayScore) return 2; // Draw: 2 stars
            return 3; // Win: 3 stars UI
        }

        public int InternalStars()
        {
            // Verified: Win by >= 2 goals awards 4 internal stars (status = 5)
            if (HomeScore < AwayScore) return 1;
            if (HomeScore == AwayScore) return 2;
            return (HomeScore - AwayScore >= 2) ? 4 : 3;
        }

        public static bool CanPromoteToSeason(int currentSeason, int accumulatedStars)
        {
            // Verified thresholds: S0 -> S1 (Training finished), S1 -> S2 (20 stars), S2 -> S3 (30 stars), S3 -> S4 (30 stars)
            return currentSeason switch
            {
                0 => true,
                1 => accumulatedStars >= 20,
                2 => accumulatedStars >= 30,
                3 => accumulatedStars >= 30,
                _ => true
            };
        }

        private string ResolveShotEncounter(PendingEncounter encounter, bool homeWinsTie, bool awayWinsTie)
        {
            if (_location == 2) // Home CF shooting on Away GK
            {
                bool homeScored = encounter.HomeTotal > encounter.AwayTotal ||
                                 (encounter.HomeTotal == encounter.AwayTotal && homeWinsTie);

                if (homeScored)
                {
                    HomeScore++;
                    _location = 0; // Reset to midfield
                    _possession = 1; // Away kickoff
                    BallHolderSlotIndex = 1; // Centered for kickoff
                    LastGoalCelebration = (GoalCelebrationType)UnityEngine.Random.Range(0, 3);
                    return $"GOAL! {HomeName} striker scores with {encounter.HomeTotal} " +
                           $"against {AwayName} goalkeeper's {encounter.AwayTotal}!";
                }

                // Goalkeeper save (runSaveAnimation & goalieToDefenderAnimation)
                _possession = 1; // Away GK takes ball
                _location = 1; // Cleared to defending half
                BallHolderSlotIndex = UnityEngine.Random.Range(0, 3); // Cleared out to defender
                return $"SAVED! {AwayName}'s goalkeeper denies the shot ({encounter.AwayTotal} vs {encounter.HomeTotal}). Ball cleared.";
            }
            else // _location == -2: Away CF shooting on Home GK
            {
                bool awayScored = encounter.AwayTotal > encounter.HomeTotal ||
                                 (encounter.AwayTotal == encounter.HomeTotal && awayWinsTie);

                if (awayScored)
                {
                    AwayScore++;
                    _location = 0; // Reset to midfield
                    _possession = 0; // Home kickoff
                    BallHolderSlotIndex = 1; // Centered for kickoff
                    LastGoalCelebration = (GoalCelebrationType)UnityEngine.Random.Range(0, 3);
                    return $"GOAL! {AwayName} score with {encounter.AwayTotal} " +
                           $"against {HomeName} goalkeeper's {encounter.HomeTotal}!";
                }

                // Goalkeeper save
                _possession = 0; // Home GK takes ball
                _location = -1; // Cleared to defending half
                BallHolderSlotIndex = UnityEngine.Random.Range(0, 3); // Cleared out to defender
                return $"SAVED! {HomeName}'s goalkeeper pulls off a brilliant save ({encounter.HomeTotal} vs {encounter.AwayTotal}).";
            }
        }

        private string ResolveOutfieldEncounter(PendingEncounter encounter, bool homeWinsTie, bool awayWinsTie)
        {
            var homeTotal = encounter.HomeTotal;
            var awayTotal = encounter.AwayTotal;

            string header = $"{HomeName} {homeTotal} ({encounter.HomeFaces}) vs " +
                           $"{AwayName} {awayTotal} ({encounter.AwayFaces}).";

            if (homeTotal > awayTotal || (homeTotal == awayTotal && homeWinsTie))
            {
                BallHolderSlotIndex = GetBestRollIndex(encounter.HomeRollFaces);
                if (_possession == 0)
                {
                    // Home had ball: advance field (shiftFieldRight, location++)
                    _location = Math.Min(MaxLocation, _location + 1);
                    return $"{header} {HomeName} win the battle and push the attack forward.";
                }
                else
                {
                    // Away had ball: turnover tackle (changePossessionAnimation1)
                    _possession = 0;
                    return $"{header} Tackled! {HomeName} dispossess the opponent and win the ball.";
                }
            }
            else if (awayTotal > homeTotal || (homeTotal == awayTotal && awayWinsTie))
            {
                BallHolderSlotIndex = GetBestRollIndex(encounter.AwayRollFaces);
                if (_possession == 1)
                {
                    // Away had ball: advance field (shiftFieldLeft, location--)
                    _location = Math.Max(MinLocation, _location - 1);
                    return $"{header} {AwayName} break through and advance into dangerous territory.";
                }
                else
                {
                    // Home had ball: turnover tackle
                    _possession = 1;
                    return $"{header} Dispossessed! {AwayName} win the ball on a tackle.";
                }
            }
            else
            {
                // Tie (runTieAnimation): ball stays put, current possession retained
                BallHolderSlotIndex = (BallHolderSlotIndex + 1) % 3;
                return $"{header} Both teams cancel each other out in a hard-fought draw. Possession holds.";
            }
        }

        private void SetLastRolls(
            int firstSide,
            (DieRoll[] Rolls, int Total, string Faces) first,
            int secondSide,
            (DieRoll[] Rolls, int Total, string Faces) second)
        {
            if (firstSide == 0)
            {
                LastHomeFaces = FinalFaces(first.Rolls);
                LastHomeTotal = first.Total;
            }
            else
            {
                LastAwayFaces = FinalFaces(first.Rolls);
                LastAwayTotal = first.Total;
            }

            if (secondSide == 0)
            {
                LastHomeFaces = FinalFaces(second.Rolls);
                LastHomeTotal = second.Total;
            }
            else
            {
                LastAwayFaces = FinalFaces(second.Rolls);
                LastAwayTotal = second.Total;
            }
        }

        private static int[] FinalFaces(DieRoll[] rolls)
        {
            var faces = new int[rolls.Length];
            for (var i = 0; i < rolls.Length; i++)
                faces[i] = rolls[i].Face;
            return faces;
        }

        private static (DieRoll[] Rolls, int Total, string Faces) RollSlots(
            PlayerData[] team,
            int[] slots,
            bool[] redCards,
            int extraTeamBonus)
        {
            var rolls = new DieRoll[slots.Length];
            var faces = new List<string>(slots.Length);
            var teamTotal = 0;

            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                var player = team[slot];
                bool isRedCarded = redCards != null && redCards[slot];

                var die = RollPlayerDie(player, isRedCarded);
                rolls[i] = die;
                teamTotal += die.Total;
                faces.Add($"{player.name}: {die.Faces} = {die.Total}");
            }

            teamTotal += extraTeamBonus;
            if (extraTeamBonus > 0)
                faces.Add($"+{extraTeamBonus} team possession bonus");

            return (rolls, teamTotal, string.Join(" | ", faces));
        }

        private static DieRoll RollPlayerDie(PlayerData player, bool isRedCarded)
        {
            if (isRedCarded)
            {
                return new DieRoll(0, 0, "RED CARD (0)");
            }

            var total = 0;
            var faces = new List<string>();
            var finalFace = 0;
            var extraRolls = 0;

            while (true)
            {
                var face = player.diceSides[UnityEngine.Random.Range(0, player.diceSides.Length)];
                finalFace = face;
                faces.Add(FaceLabel(face));

                // Faces >= 101: referee flag + subtract 100
                if (face >= 101)
                {
                    face -= 100;
                }

                // Standard faces 1..10
                if (face >= 1 && face <= 10)
                {
                    total += face;
                    break;
                }

                // Dribble faces 16..18: adds face - 15 and triggers an extra roll
                if (face >= 16 && face <= 18)
                {
                    total += face - 15;
                    if (extraRolls++ < MaximumExtraRollsPerDie)
                        continue;
                    break;
                }

                // Cross / Assist face 24: awards extra roll
                if (face == 24)
                {
                    total += 3;
                    if (extraRolls++ < MaximumExtraRollsPerDie)
                        continue;
                    break;
                }

                // Wild face 31: acts as 6 and combo wildcard
                if (face == 31)
                {
                    total += 6;
                    break;
                }

                // Whistle / referee clamped face 32: clamped to 0 points (actualFinalValue = 0)
                if (face == 32)
                {
                    total += 0;
                    break;
                }

                // Fallback for any other custom face
                total += Math.Min(face, 6);
                break;
            }

            return new DieRoll(total, finalFace, string.Join(" → ", faces));
        }

        private static string FaceLabel(int face)
        {
            if (face >= 101)
                return $"REF-{face - 100}";
            if (face >= 16 && face <= 18)
                return $"+{face - 15} Dribble";
            if (face == 24)
                return "CROSS";
            if (face == 31)
                return "WILD";
            if (face == 32)
                return "WHISTLE (0)";
            return face.ToString();
        }

        private static void ValidateLineup(PlayerData[] lineup, string side)
        {
            if (lineup == null || lineup.Length != 7)
                throw new ArgumentException($"The {side} lineup must contain exactly seven players.");
            foreach (var player in lineup)
            {
                if (player == null || player.diceSides == null || player.diceSides.Length == 0)
                    throw new ArgumentException($"The {side} lineup contains a player without die sides.");
            }
            if (lineup[GoalkeeperSlot].position != "GK")
                throw new ArgumentException($"The {side} lineup must place its goalkeeper in slot 1.");
        }

        private sealed class PendingEncounter
        {
            public readonly bool IsShot;
            public readonly int Attacker;
            public int HomeTotal { get; set; }
            public int AwayTotal { get; set; }
            public readonly string HomeFaces;
            public readonly string AwayFaces;
            public readonly int[] HomeSlots;
            public readonly int[] AwaySlots;
            public readonly int[] HomeRollFaces;
            public readonly int[] AwayRollFaces;

            public PendingEncounter(
                bool isShot,
                int attacker,
                int homeTotal,
                int awayTotal,
                string homeFaces,
                string awayFaces,
                int[] homeSlots,
                int[] awaySlots,
                int[] homeRollFaces = null,
                int[] awayRollFaces = null)
            {
                IsShot = isShot;
                Attacker = attacker;
                HomeTotal = homeTotal;
                AwayTotal = awayTotal;
                HomeFaces = homeFaces;
                AwayFaces = awayFaces;
                HomeSlots = homeSlots;
                AwaySlots = awaySlots;
                HomeRollFaces = homeRollFaces ?? Array.Empty<int>();
                AwayRollFaces = awayRollFaces ?? Array.Empty<int>();
            }
        }

        private static int GetBestRollIndex(int[] faces)
        {
            if (faces == null || faces.Length == 0) return 1;
            int bestIdx = 1;
            int bestVal = -1;
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i] > bestVal)
                {
                    bestVal = faces[i];
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private readonly struct DieRoll
        {
            public readonly int Total;
            public readonly int Face;
            public readonly string Faces;

            public DieRoll(int total, int face, string faces)
            {
                Total = total;
                Face = face;
                Faces = faces;
            }
        }
    }
}
