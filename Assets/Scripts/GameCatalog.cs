using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DiceSoccer
{
    [Serializable]
    public sealed class GameCatalogData
    {
        public PlayerData[] players;
        public AbilityData[] abilities;
        public SeasonData[] seasons;
        public int[] initialLineup;
        public string[] tips;
        public ShopCashData[] shopCash;
        public ShirtData[] shirts;
        public ColourData[] colours;
        public AchievementData[] achievements;
        public EntourageData[] entourage;
        public TutorialItemData[] tutorials;
        public TutorialItemData[] persistentTutorials;
    }

    [Serializable]
    public sealed class TutorialItemData
    {
        public int id;
        public string[] popupText;
        public int[] customPopup;
    }

    [Serializable]
    public sealed class ShopCashData
    {
        public int id;
        public float price;
        public int coins;
    }

    [Serializable]
    public sealed class ShirtData
    {
        public int id;
        public int shirtColour;
        public int sleeveColour;
        public int shirtStyle;
        public int styleColour;
        public float price;
    }

    [Serializable]
    public sealed class ColourData
    {
        public int id;
        public int r;
        public int g;
        public int b;
    }

    [Serializable]
    public sealed class AchievementData
    {
        public int id;
        public string unlockedDescription;
        public string lockedDescription;
        public int goldReward;
        public int pantsReward;
        public int entourageReward;
    }

    [Serializable]
    public sealed class EntourageData
    {
        public int id;
        public string name;
        public string position;
        public int cost;
        public int abilityTrigger;
        public int abilityIcon;
        public string abilityName;
        public string abilityDescription;
        public string playerDescription;
        public string inGameDescription;
        public string unlockCriteria;
        public int sellingPrice;
        public float inappPrice;
    }

    [Serializable]
    public sealed class PlayerData
    {
        public int id;
        public string name;
        public string position;
        public int cost;
        public int sellingPrice;
        public int imageId;
        public int skinColour;
        public string description;
        public string unlockCriteria;
        public int superpower;
        public int gender;
        public int[] diceSides;
    }

    [Serializable]
    public sealed class AbilityData
    {
        public int id;
        public string name;
        public string description;
        public int trigger;
        public int type;
        public int target;
        public int slot;
        public int localReroll;
    }

    [Serializable]
    public sealed class SeasonData
    {
        public int id;
        public string name;
        public FieldObjectsData fieldObjects;
        public CompetitionData[] competitions;
    }

    [Serializable]
    public sealed class FieldObjectsData
    {
        public string[] Normal;
        public string[] iPad;
    }

    [Serializable]
    public sealed class CompetitionData
    {
        public string name;
        public int matchCount;
        public FixtureData[] fixtures;
    }

    [Serializable]
    public sealed class FixtureData
    {
        public int id;
        public string teamName;
        public int[] lineup;
        public int cashWin;
        public int cashDraw;
        public int cashLose;
        public int shirt;
        public int pants;
        public int shirtPatternColor;
        public int shirtPatternStyle;
        public int sleevesSock;
        public int emblem;
        public int[] entourageLineup;
        public int possessionBonus;
        public string eventName;
    }

    public sealed class GameCatalog
    {
        private readonly Dictionary<int, PlayerData> _players;
        private readonly Dictionary<int, AbilityData> _abilities;
        private readonly Dictionary<int, ShirtData> _shirts;
        private readonly Dictionary<int, ColourData> _colours;
        private readonly Dictionary<int, AchievementData> _achievements;
        private readonly Dictionary<int, EntourageData> _entourage;
        private readonly Dictionary<int, TutorialItemData> _tutorials;
        private readonly Dictionary<int, TutorialItemData> _persistentTutorials;

        public GameCatalogData Data { get; }

        private GameCatalog(GameCatalogData data)
        {
            Data = data;
            _players = new Dictionary<int, PlayerData>();
            foreach (var player in data.players)
            {
                if (player == null || player.diceSides == null || player.diceSides.Length == 0)
                    throw new InvalidDataException("The extracted player catalog contains an invalid player record.");
                if (!_players.TryAdd(player.id, player))
                    throw new InvalidDataException($"The extracted player catalog repeats player ID {player.id}.");
            }

            _abilities = new Dictionary<int, AbilityData>();
            foreach (var ability in data.abilities ?? Array.Empty<AbilityData>())
            {
                if (ability == null)
                    throw new InvalidDataException("The extracted ability catalog contains an invalid record.");
                if (!_abilities.TryAdd(ability.id, ability))
                    throw new InvalidDataException($"The extracted ability catalog repeats ability ID {ability.id}.");
            }

            _shirts = new Dictionary<int, ShirtData>();
            foreach (var shirt in data.shirts ?? Array.Empty<ShirtData>())
            {
                if (shirt != null)
                    _shirts.TryAdd(shirt.id, shirt);
            }

            _colours = new Dictionary<int, ColourData>();
            foreach (var colour in data.colours ?? Array.Empty<ColourData>())
            {
                if (colour != null)
                    _colours.TryAdd(colour.id, colour);
            }

            _achievements = new Dictionary<int, AchievementData>();
            foreach (var ach in data.achievements ?? Array.Empty<AchievementData>())
            {
                if (ach != null)
                    _achievements.TryAdd(ach.id, ach);
            }

            _entourage = new Dictionary<int, EntourageData>();
            foreach (var ent in data.entourage ?? Array.Empty<EntourageData>())
            {
                if (ent != null)
                    _entourage.TryAdd(ent.id, ent);
            }

            _tutorials = new Dictionary<int, TutorialItemData>();
            foreach (var tut in data.tutorials ?? Array.Empty<TutorialItemData>())
            {
                if (tut != null)
                    _tutorials.TryAdd(tut.id, tut);
            }

            _persistentTutorials = new Dictionary<int, TutorialItemData>();
            foreach (var pt in data.persistentTutorials ?? Array.Empty<TutorialItemData>())
            {
                if (pt != null)
                    _persistentTutorials.TryAdd(pt.id, pt);
            }

            if (data.initialLineup == null || data.initialLineup.Length != 7)
                throw new InvalidDataException("The extracted starting lineup must contain seven players.");
            ResolveLineup(data.initialLineup);
        }

        public static GameCatalog Load()
        {
            var asset = Resources.Load<TextAsset>("Original/data/UnityCatalog");
            if (asset == null)
            {
                throw new FileNotFoundException(
                    "Unity game data is missing. Run Tools/extract_assets.py from the Unity project folder, then let Unity import the generated Resources folder.");
            }

            var data = JsonUtility.FromJson<GameCatalogData>(asset.text);
            if (data == null || data.players == null || data.seasons == null || data.seasons.Length == 0)
                throw new InvalidDataException("The extracted game catalog is empty or malformed.");
            return new GameCatalog(data);
        }

        public PlayerData GetPlayer(int id)
        {
            if (_players.TryGetValue(id, out var player))
                return player;
            throw new KeyNotFoundException($"The original game data references missing player ID {id}.");
        }

        public AbilityData GetAbility(int id)
        {
            if (_abilities.TryGetValue(id, out var ability))
                return ability;
            throw new KeyNotFoundException($"The original game data references missing ability ID {id}.");
        }

        public AbilityData TryGetAbility(int id)
        {
            return _abilities != null && _abilities.TryGetValue(id, out var ability) ? ability : null;
        }

        public ShirtData GetShirt(int id)
        {
            return _shirts.TryGetValue(id, out var shirt) ? shirt : null;
        }

        public ColourData GetColour(int id)
        {
            return _colours.TryGetValue(id, out var colour) ? colour : null;
        }

        public AchievementData GetAchievement(int id)
        {
            return _achievements.TryGetValue(id, out var ach) ? ach : null;
        }

        public EntourageData GetEntourage(int id)
        {
            return _entourage.TryGetValue(id, out var ent) ? ent : null;
        }

        public TutorialItemData GetTutorial(int id)
        {
            return _tutorials.TryGetValue(id, out var tut) ? tut : null;
        }

        public TutorialItemData GetPersistentTutorial(int id)
        {
            return _persistentTutorials.TryGetValue(id, out var pt) ? pt : null;
        }

        public Color32 GetColourRgb(int id, byte alpha = 255)
        {
            if (_colours.TryGetValue(id, out var c))
                return new Color32((byte)c.r, (byte)c.g, (byte)c.b, alpha);
            return new Color32(255, 255, 255, alpha);
        }

        public FixtureData GetFirstFixture()
        {
            foreach (var season in Data.seasons)
            {
                foreach (var competition in season.competitions)
                {
                    if (competition.name == "League" && competition.fixtures.Length > 0)
                        return competition.fixtures[0];
                }
            }

            throw new InvalidDataException("The extracted game data contains no league fixtures.");
        }

        public FixtureData[] GetLeagueFixtures(int seasonId)
        {
            return GetFixtures(seasonId, "League");
        }

        public CompetitionData[] GetCompetitions(int seasonId)
        {
            foreach (var season in Data.seasons)
            {
                if (season.id != seasonId)
                    continue;
                return season.competitions ?? Array.Empty<CompetitionData>();
            }

            throw new KeyNotFoundException($"The original game data has no season {seasonId}.");
        }

        public FixtureData[] GetFixtures(int seasonId, string competitionName)
        {
            foreach (var competition in GetCompetitions(seasonId))
            {
                if (competition.name == competitionName)
                    return competition.fixtures ?? Array.Empty<FixtureData>();
            }

            throw new KeyNotFoundException(
                $"The original game data has no {competitionName} competition in season {seasonId}.");
        }

        public PlayerData[] ResolveLineup(int[] playerIds)
        {
            if (playerIds == null || playerIds.Length != 7)
                throw new InvalidDataException("A match lineup must contain seven player IDs.");

            var lineup = new PlayerData[playerIds.Length];
            for (var i = 0; i < playerIds.Length; i++)
                lineup[i] = GetPlayer(playerIds[i]);
            return lineup;
        }
    }
}
