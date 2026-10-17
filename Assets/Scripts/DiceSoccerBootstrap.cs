using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DiceSoccer
{
    public sealed class DiceSoccerBootstrap : MonoBehaviour
    {
        private const float ReferenceWidth = 640f;
        private const float ReferenceHeight = 960f;
        private const float ShakeDeltaThreshold = 1.8f;
        private const float ShakeCooldownSeconds = 0.8f;
        private const float ShakeQuietSecondsToRearm = 0.25f;
        private const float EdgeTapWidth = 0.12f;
        private const string LineupPreferenceKey = "diceSoccer.lineup";
        private const string RosterPreferenceKey = "diceSoccer.roster";
        private static readonly Color Ink = new Color32(24, 35, 44, 255);
        private static readonly Color Paper = new Color32(244, 239, 220, 255);
        private static readonly Color Grass = new Color32(47, 125, 77, 255);
        private static readonly Color Gold = new Color32(238, 184, 70, 255);

        private Canvas _canvas;
        private RectTransform _safeArea;
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Text _matchScore;
        private Text _matchClock;
        private Text _matchReport;
        private Button _encounterButton;
        private Button _backButton;
        private Button _substitutionButton;
        private Image[] _homeDice;
        private Image[] _awayDice;
        private Image[] _homePlayers;
        private Image[] _awayPlayers;
        private Image _ballMarker;
        private Transform _field;
        private MatchSimulation _match;
        private GameCatalog _catalog;
        private FixtureData[] _seasonFixtures;
        private FixtureData _currentFixture;
        private string _competitionName = "League";
        private int[] _lineupIds;
        private List<int> _ownedPlayerIds;
        private int _selectedFixtureIndex;
        private int _selectedLineupSlot;
        private int _seasonIndex;
        private int _fixtureIndex;
        private int _careerCash;
        private HashSet<int> _unlockedShirtIds = new HashSet<int> { 1 };
        private HashSet<int> _unlockedPantsIds = new HashSet<int> { 1 };
        private int _currentShirtId = 1;
        private int _currentPantsId = 1;
        private HashSet<int> _unlockedAchievementIds = new HashSet<int>();
        private HashSet<int> _unlockedEntourageIds = new HashSet<int>();
        private List<int> _equippedEntourageIds = new List<int>();
        private int _shopTab;
        private int _shopPage;
        private int _trophyPage;
        private bool _currentResultRecorded;
        private bool _soundEnabled = true;
        private Vector3 _previousAcceleration;
        private bool _hasAccelerationSample;
        private bool _shakeArmed = true;
        private float _shakeQuietSince = -1f;
        private float _shakeRearmAfter;
        private RectTransform _overlayContainer;
        private Image _ballShadow;
        private bool _inEncounterAnimation;
        private bool _isKickoffPlayed;
        private int _lastSubstitutedSlot = -1;
        private int _remainingSubs = 3;
        private readonly Vector2[] _homeBasePositions = new Vector2[7];
        private readonly Vector2[] _awayBasePositions = new Vector2[7];
        private static readonly Dictionary<int, Sprite> DiceSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<string, Sprite> UiSprites =
            new Dictionary<string, Sprite>();
        private static Sprite DiceBackgroundSprite;
        private static readonly int[] StandardDiceFaces =
            { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 16, 17, 18, 24, 31, 32 };

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            _soundEnabled = PlayerPrefs.GetInt("diceSoccer.sound", 1) != 0;
            AudioManager.Instance.SoundEnabled = _soundEnabled;
            InitBasePositions();
            CreateCanvas();
            ApplySafeArea();
            LoadCareerProgress();
            ShowHome();
        }

        private void InitBasePositions()
        {
            // Verified tactical 1-3-3 formation for 7-a-side pitch
            // Home team (facing right): GK [0], DEF [1..3], MID/ATT [4..6]
            _homeBasePositions[0] = new Vector2(0.08f, 0.50f);
            _homeBasePositions[1] = new Vector2(0.18f, 0.76f);
            _homeBasePositions[2] = new Vector2(0.17f, 0.50f);
            _homeBasePositions[3] = new Vector2(0.18f, 0.24f);
            _homeBasePositions[4] = new Vector2(0.32f, 0.74f);
            _homeBasePositions[5] = new Vector2(0.36f, 0.50f);
            _homeBasePositions[6] = new Vector2(0.32f, 0.26f);

            // Away team (facing left, mirrored):
            _awayBasePositions[0] = new Vector2(0.92f, 0.50f);
            _awayBasePositions[1] = new Vector2(0.82f, 0.24f);
            _awayBasePositions[2] = new Vector2(0.83f, 0.50f);
            _awayBasePositions[3] = new Vector2(0.82f, 0.76f);
            _awayBasePositions[4] = new Vector2(0.68f, 0.26f);
            _awayBasePositions[5] = new Vector2(0.64f, 0.50f);
            _awayBasePositions[6] = new Vector2(0.68f, 0.74f);
        }

        private void Update()
        {
            if (_lastSafeArea != Screen.safeArea || _lastScreenWidth != Screen.width ||
                _lastScreenHeight != Screen.height)
                ApplySafeArea();

            DetectShake();
            DetectEdgeTap();
        }

        private void DetectShake()
        {
            var acceleration = Input.acceleration;
            if (!_hasAccelerationSample)
            {
                _previousAcceleration = acceleration;
                _hasAccelerationSample = true;
                return;
            }

            var accelerationDelta = (acceleration - _previousAcceleration).magnitude;
            _previousAcceleration = acceleration;

            if (_match == null || _match.IsFinished || _encounterButton == null ||
                !_encounterButton.interactable || _inEncounterAnimation)
                return;

            if (accelerationDelta >= ShakeDeltaThreshold)
            {
                _shakeQuietSince = -1f;
                if (_shakeArmed)
                {
                    _shakeArmed = false;
                    _shakeRearmAfter = Time.unscaledTime + ShakeCooldownSeconds;
                    PlayEncounter();
                }
                return;
            }

            if (_shakeArmed || Time.unscaledTime < _shakeRearmAfter)
                return;

            if (_shakeQuietSince < 0f)
                _shakeQuietSince = Time.unscaledTime;
            else if (Time.unscaledTime - _shakeQuietSince >= ShakeQuietSecondsToRearm)
                _shakeArmed = true;
        }

        private void DetectEdgeTap()
        {
            if (_match == null || _match.IsFinished || _encounterButton == null ||
                !_encounterButton.interactable || _inEncounterAnimation)
                return;

            var edgeWidth = Screen.width * EdgeTapWidth;
            foreach (var touch in Input.touches)
            {
                if (touch.phase == TouchPhase.Began &&
                    (touch.position.x <= edgeWidth || touch.position.x >= Screen.width - edgeWidth))
                {
                    PlayEncounter();
                    return;
                }
            }
        }

        private void CreateCanvas()
        {
            var canvasObject = new GameObject("Game Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var safeAreaObject = new GameObject("Safe Area", typeof(RectTransform));
            safeAreaObject.transform.SetParent(canvasObject.transform, false);
            _safeArea = safeAreaObject.GetComponent<RectTransform>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("Event System", typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }
        }

        private void ShowHome()
        {
            _match = null;
            _encounterButton = null;
            AudioManager.Instance.PlayBgm("MainTheme", 0.5f);
            AudioManager.Instance.StopCrowd();
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader($"SEASON {_seasonIndex}", null);
            AddMenuFooter();
            AddResourceImage("StartScreenLogo", "Original/images/StartScreenLogo",
                _safeArea, new Vector2(0.34f, 0.66f), new Vector2(0.66f, 0.9f));

            var trophyBtn = AddSpriteButton("TrophiesHome", "Original/sprites/TrophyBG/TrophyBG",
                "Original/sprites/TrophyBG/TrophyBGPressed", _safeArea,
                new Vector2(0.86f, 0.78f), new Vector2(0.18f, 0.12f), ShowTrophyRoom);
            var trophyIcon = AddResourceImage("TrophyHomeIcon", "Original/images/Trophy01", trophyBtn.transform,
                new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f));
            trophyIcon.preserveAspect = true;
            trophyIcon.raycastTarget = false;

            var catalogAvailable = Resources.Load<TextAsset>("Original/data/UnityCatalog") != null;
            var campaign = AddSpriteButton("Campaign", "Original/images/MainMenuButtons01",
                "Original/images/MainMenuButtons01Pressed", _safeArea,
                new Vector2(0.5f, 0.58f), new Vector2(0.72f, 0.14f), StartCareerMatch);
            campaign.interactable = catalogAvailable;
            var players = AddSpriteButton("Players", "Original/images/MainMenuButtons02",
                "Original/images/MainMenuButtons02Pressed", _safeArea,
                new Vector2(0.5f, 0.42f), new Vector2(0.72f, 0.14f), ShowManage);
            players.interactable = catalogAvailable;
            var manage = AddSpriteButton("Manage", "Original/images/MainMenuButtons03",
                "Original/images/MainMenuButtons03Pressed", _safeArea,
                new Vector2(0.5f, 0.26f), new Vector2(0.72f, 0.14f), ShowHiring);
            manage.interactable = catalogAvailable;
            if (!catalogAvailable)
            {
                AddText("Run Tools/extract_assets.py to import the original game data.",
                    _safeArea, 16, new Color32(255, 193, 133, 255),
                    new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.22f));
            }
        }

        private void ShowOptions()
        {
            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader("OPTIONS", ShowHome);
            AddText("SOUND", _safeArea, 20, Ink,
                new Vector2(0.1f, 0.67f), new Vector2(0.9f, 0.74f));
            AddButton(_soundEnabled ? "SOUND ON" : "SOUND OFF", _safeArea, Gold,
                new Vector2(0.5f, 0.59f), new Vector2(0.75f, 0.09f), ToggleSound);
            AddText("SHAKE OR TAP THE SCREEN EDGE TO ROLL",
                _safeArea, 15, Ink, new Vector2(0.08f, 0.43f), new Vector2(0.92f, 0.52f));
        }

        private void ToggleSound()
        {
            _soundEnabled = !_soundEnabled;
            AudioManager.Instance.SoundEnabled = _soundEnabled;
            PlayerPrefs.SetInt("diceSoccer.sound", _soundEnabled ? 1 : 0);
            PlayerPrefs.Save();
            ShowOptions();
        }

        private void StartCareerMatch()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            _seasonIndex = Mathf.Clamp(_seasonIndex, 0, _catalog.Data.seasons.Length - 1);
            _competitionName = "League";
            _seasonFixtures = _catalog.GetFixtures(_seasonIndex, _competitionName);
            if (_seasonFixtures.Length == 0)
                throw new InvalidDataException($"Season {_seasonIndex} has no League fixtures.");
            _fixtureIndex = LoadCompetitionProgress(_competitionName);
            ShowLeagueFixtures();
        }

        private void ShowLeagueFixtures()
        {
            if (_seasonFixtures == null || _seasonFixtures.Length == 0)
                throw new InvalidOperationException("League fixtures have not been loaded.");

            _match = null;
            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader(_catalog.Data.seasons[_seasonIndex].name, ShowHome);

            var competitions = _catalog.GetCompetitions(_seasonIndex);
            var availableCompetitions = new List<CompetitionData>();
            foreach (var competition in competitions)
            {
                if (competition.fixtures != null && competition.fixtures.Length > 0)
                    availableCompetitions.Add(competition);
            }
            var tabWidth = 0.88f / availableCompetitions.Count;
            for (var index = 0; index < availableCompetitions.Count; index++)
            {
                var competition = availableCompetitions[index];
                var capturedName = competition.name;
                var centerX = 0.06f + tabWidth * (index + 0.5f);
                var selected = competition.name == _competitionName;
                var tabPath = $"Original/sprites/MatchSelectionSS1/Campaign{competition.name}Tab";
                var tabButton = AddSpriteButton(competition.name,
                    tabPath + (selected ? "Selected" : string.Empty),
                    tabPath + (selected ? "Selected" : string.Empty), _safeArea,
                    new Vector2(centerX, 0.78f), new Vector2(tabWidth - 0.015f, 0.06f),
                    () => SelectCompetition(capturedName));
                tabButton.interactable = IsCompetitionAvailable(competition.name);
            }

            var status = _competitionName == "League"
                ? _fixtureIndex >= _seasonFixtures.Length
                    ? "LEAGUE COMPLETE - REPLAY ANY MATCH"
                    : $"LEAGUE MATCH {_fixtureIndex + 1} OF {_seasonFixtures.Length} AVAILABLE"
                : _fixtureIndex >= _seasonFixtures.Length
                    ? $"{_competitionName} COMPLETE - REPLAY ANY MATCH"
                    : $"{_competitionName} MATCH {_fixtureIndex + 1} OF {_seasonFixtures.Length} AVAILABLE";
            AddText(status, _safeArea, 14, Paper,
                new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.77f));

            var rowHeight = 96f;
            var content = CreateScrollList("League fixtures", _safeArea,
                new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.7f),
                _seasonFixtures.Length * rowHeight);
            for (var index = 0; index < _seasonFixtures.Length; index++)
            {
                var fixture = _seasonFixtures[index];
                var capturedIndex = index;
                var yMin = 1f - (index + 1) / (float)_seasonFixtures.Length;
                var yMax = 1f - index / (float)_seasonFixtures.Length;
                var tournament = _competitionName == "T1" || _competitionName == "T2";
                var locked = index > _fixtureIndex ||
                             (tournament && index == _fixtureIndex &&
                              _fixtureIndex > 0 && !IsPreviousTournamentFixtureWon());
                var label = locked
                    ? $"MATCH {index + 1}  —  LOCKED\nWin the previous tournament match"
                    : $"{_competitionName} {index + 1}  —  {fixture.teamName}\n" +
                      (tournament
                          ? index < _fixtureIndex ? "Replay tournament match" : "Win to unlock the next match"
                          : index < _fixtureIndex ? $"Replay  |  Win {fixture.cashWin:N0}  Draw {fixture.cashDraw:N0}  Loss {fixture.cashLose:N0}"
                          : $"Play  |  Win {fixture.cashWin:N0}  Draw {fixture.cashDraw:N0}  Loss {fixture.cashLose:N0}");
                var button = AddButton(label, content,
                    locked ? new Color32(49, 55, 58, 255) : new Color32(68, 79, 84, 255),
                    new Vector2(0.5f, (yMin + yMax) * 0.5f),
                    new Vector2(0.96f, yMax - yMin),
                    () => StartFixture(capturedIndex));
                button.interactable = !locked;
            }

            if (_fixtureIndex >= _seasonFixtures.Length && _competitionName == "League")
            {
                AddButton("SEASON SUMMARY", _safeArea, Grass,
                    new Vector2(0.5f, 0.08f), new Vector2(0.7f, 0.06f), ShowSeasonCompletion);
            }

            if (_seasonIndex == 0 && _fixtureIndex == 0 && PlayerPrefs.GetInt("diceSoccer.tutorial.5", 0) == 0)
            {
                ShowTutorialPopup(5, () =>
                {
                    if (PlayerPrefs.GetInt("diceSoccer.tutorial.24", 0) == 0)
                        ShowTutorialPopup(24);
                });
            }
        }

        private void SelectCompetition(string competitionName)
        {
            if (!IsCompetitionAvailable(competitionName))
                return;
            var fixtures = _catalog.GetFixtures(_seasonIndex, competitionName);
            if (fixtures.Length == 0)
                return;
            _competitionName = competitionName;
            _seasonFixtures = fixtures;
            _fixtureIndex = LoadCompetitionProgress(competitionName);
            ShowLeagueFixtures();
        }

        private bool IsCompetitionAvailable(string competitionName)
        {
            if (competitionName == "League")
                return true;
            return LoadCompetitionProgress("League") >= _catalog.GetFixtures(_seasonIndex, "League").Length;
        }

        private bool IsPreviousTournamentFixtureWon()
        {
            var previousIndex = _fixtureIndex - 1;
            var result = PlayerPrefs.GetInt(TournamentResultKey(_competitionName, previousIndex), 0);
            return result > 0;
        }

        private int LoadCompetitionProgress(string competitionName)
        {
            var key = CompetitionProgressKey(competitionName);
            var stored = PlayerPrefs.GetInt(key, -1);
            if (stored < 0 && competitionName == "League")
                stored = PlayerPrefs.GetInt("diceSoccer.fixture", 0);
            return Mathf.Clamp(stored < 0 ? 0 : stored, 0,
                _catalog.GetFixtures(_seasonIndex, competitionName).Length);
        }

        private string CompetitionProgressKey(string competitionName)
        {
            return $"diceSoccer.season.{_seasonIndex}.{competitionName}.fixture";
        }

        private string TournamentResultKey(string competitionName, int fixtureIndex)
        {
            return $"diceSoccer.season.{_seasonIndex}.{competitionName}.result.{fixtureIndex}";
        }

        private void StartFixture(int fixtureIndex)
        {
            if (fixtureIndex < 0 || fixtureIndex > _fixtureIndex ||
                fixtureIndex >= _seasonFixtures.Length)
                throw new InvalidOperationException("The selected competition fixture is still locked.");

            _selectedFixtureIndex = fixtureIndex;
            StartCurrentFixture();
        }

        private void ShowSeasonCompletion()
        {
            _match = null;
            _encounterButton = null;
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader("SEASON SUMMARY", ShowLeagueFixtures);
            AddText($"{_catalog.Data.seasons[_seasonIndex].name.ToUpperInvariant()} LEAGUE COMPLETE",
                _safeArea, 25, Gold,
                new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.7f));
            AddText($"{_careerCash:N0} CASH",
                _safeArea, 19, Paper, new Vector2(0.05f, 0.53f), new Vector2(0.95f, 0.59f));
            if (_seasonIndex > 0)
            {
                AddText("Season-star promotion rules are not reconstructed yet.",
                    _safeArea, 14, Paper, new Vector2(0.08f, 0.45f), new Vector2(0.92f, 0.51f));
            }
            AddButton(SeasonEndButtonLabel(), _safeArea, Grass,
                new Vector2(0.5f, 0.38f), new Vector2(0.82f, 0.085f), AdvanceCareer);
            AddButton("BACK TO FIXTURES", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.27f), new Vector2(0.65f, 0.07f), ShowLeagueFixtures);
        }

        private void StartCurrentFixture()
        {
            if (_catalog == null || _seasonFixtures == null ||
                _selectedFixtureIndex < 0 || _selectedFixtureIndex >= _seasonFixtures.Length)
                throw new InvalidOperationException("There is no current competition fixture to start.");

            _currentFixture = _seasonFixtures[_selectedFixtureIndex];
            _currentResultRecorded = false;
            _shakeArmed = true;
            _shakeQuietSince = -1f;
            _hasAccelerationSample = false;
            _isKickoffPlayed = false;
            _lastSubstitutedSlot = -1;
            _remainingSubs = 3;
            var homeLineup = _catalog.ResolveLineup(_lineupIds);
            _match = new MatchSimulation(
                homeLineup,
                _catalog.ResolveLineup(_currentFixture.lineup),
                _currentFixture.teamName,
                _seasonIndex,
                _equippedEntourageIds.ToArray(),
                _currentFixture.entourageLineup,
                0,
                _currentFixture.possessionBonus);

            if (_seasonIndex == 0 && _selectedFixtureIndex == 0 && PlayerPrefs.GetInt("diceSoccer.tutorial.11", 0) == 0)
            {
                ShowTutorialPopup(11, () => ShowMatchScreen(homeLineup));
            }
            else
            {
                ShowMatchScreen(homeLineup);
            }
        }

        private void ShowMatchScreen(PlayerData[] homeLineup)
        {
            AudioManager.Instance.PlayCrowdChant(0.35f);
            ClearCanvas();
            AddStretchPanel("Background", _canvas.transform, Ink);
            _safeArea.SetAsLastSibling();

            // 1. Top HUD: ScoreboardBG (640x150 -> [0, 0.84375] to [1, 1])
            var scoreboard = AddResourceImage("MatchScoreboard",
                "Original/images/scoreboardBG", _safeArea,
                new Vector2(0f, 0.84375f), new Vector2(1f, 1f));
            scoreboard.preserveAspect = false;
            scoreboard.raycastTarget = false;

            // Competition Header at top:
            AddText($"SEASON {_seasonIndex}  -  {_competitionName.ToUpperInvariant()} {_selectedFixtureIndex + 1}/{_seasonFixtures.Length}",
                _safeArea, 13, Ink,
                new Vector2(0.05f, 0.955f), new Vector2(0.95f, 0.99f));

            // Home player head:
            var homeCaptainId = homeLineup.Length > 0 && homeLineup[0].id >= 1 && homeLineup[0].id <= 96 ? homeLineup[0].id : 0;
            var homeHeadPath = homeCaptainId == 0 ? "Original/images/PlayerHead1012" : $"Original/images/PlayerHead{homeCaptainId:00}";
            var homeHead = AddResourceImage("HomeHead", homeHeadPath, _safeArea,
                new Vector2(0.02f, 0.865f), new Vector2(0.11f, 0.955f));
            homeHead.raycastTarget = false;
            AddText("YOUR TEAM", _safeArea, 13, Paper,
                new Vector2(0.12f, 0.88f), new Vector2(0.35f, 0.94f));

            // Away opponent head/crest:
            var awayLineup = _catalog.ResolveLineup(_currentFixture.lineup);
            var awayCaptainId = awayLineup.Length > 0 && awayLineup[0].id >= 1 && awayLineup[0].id <= 96 ? awayLineup[0].id : 0;
            var awayHeadPath = awayCaptainId == 0 ? "Original/images/PlayerHead1013" : $"Original/images/PlayerHead{awayCaptainId:00}";
            var awayHead = AddResourceImage("AwayHead", awayHeadPath, _safeArea,
                new Vector2(0.89f, 0.865f), new Vector2(0.98f, 0.955f));
            awayHead.raycastTarget = false;
            awayHead.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            AddText(_currentFixture.teamName.ToUpperInvariant(), _safeArea, 13, Paper,
                new Vector2(0.65f, 0.88f), new Vector2(0.88f, 0.94f));

            // Center Scoreboard Scores & Clock:
            _matchScore = AddText($"{_match.HomeScore}   -   {_match.AwayScore}", _safeArea, 34, Paper,
                new Vector2(0.35f, 0.89f), new Vector2(0.65f, 0.965f));
            _matchClock = AddText(_match.ClockText(), _safeArea, 18, Gold,
                new Vector2(0.38f, 0.852f), new Vector2(0.62f, 0.895f));

            // Pause button at top right:
            AddSpriteButton("Pause", "Original/images/PauseButton",
                "Original/images/PauseButtonPressed", _safeArea,
                new Vector2(0.935f, 0.965f), new Vector2(0.08f, 0.045f), ShowPauseMenu);

            // 2. Pitch Field (640x588 -> [0, 0.23125] to [1, 0.84375]):
            var field = AddResourceImage("Field", "Original/images/MultiplayerFieldBG",
                _safeArea, new Vector2(0f, 0.23125f), new Vector2(1f, 0.84375f));
            field.preserveAspect = false;
            _field = field.transform;

            // Soccer ball with shadow:
            var ballShadow = AddResourceImage("BallShadow", "Original/sprites/PlayerBody3/GameSoccerBallShadow",
                _field, Vector2.zero, Vector2.zero);
            ballShadow.rectTransform.sizeDelta = new Vector2(34f, 34f);
            ballShadow.raycastTarget = false;
            _ballShadow = ballShadow;

            var ball = AddResourceImage("Ball", "Original/sprites/PlayerBody3/GameSoccerBall",
                _field, Vector2.zero, Vector2.zero);
            ball.rectTransform.sizeDelta = new Vector2(34f, 34f);
            ball.raycastTarget = false;
            _ballMarker = ball;

            // Formation players:
            _homePlayers = AddFormationPlayers(homeLineup, _field, true);
            _awayPlayers = AddFormationPlayers(awayLineup, _field, false);
            UpdatePlayerPositions();
            SetBallPosition(_match.Location, _match.Possession, false);

            // 3. Buff Belt: (640x222 -> [0, 0] to [1, 0.23125])
            var belt = AddResourceImage("BuffBelt", "Original/images/BuffBelt",
                _safeArea, new Vector2(0f, 0f), new Vector2(1f, 0.23125f));
            belt.preserveAspect = false;
            belt.raycastTarget = false;

            // Dice rows inside BuffBelt:
            _homeDice = CreateDiceRowBelt(_safeArea, true);
            _awayDice = CreateDiceRowBelt(_safeArea, false);

            // Entourage medals inside BuffBelt:
            if (_equippedEntourageIds.Count > 0)
            {
                var ent1 = _catalog.GetEntourage(_equippedEntourageIds[0]);
                if (ent1 != null)
                {
                    var iconPath = ent1.abilityIcon > 0 && TryGetUiSprite($"Original/sprites/Buff6/BuffMedal{ent1.abilityIcon:00}") != null
                        ? $"Original/sprites/Buff6/BuffMedal{ent1.abilityIcon:00}"
                        : "Original/sprites/Buff6/BuffMedal16";
                    var entImg1 = AddResourceImage("HomeEntourage1", iconPath, _safeArea,
                        new Vector2(0.045f, 0.135f), new Vector2(0.125f, 0.195f));
                    entImg1.preserveAspect = true;
                }
            }
            if (_equippedEntourageIds.Count > 1)
            {
                var ent2 = _catalog.GetEntourage(_equippedEntourageIds[1]);
                if (ent2 != null)
                {
                    var iconPath = ent2.abilityIcon > 0 && TryGetUiSprite($"Original/sprites/Buff6/BuffMedal{ent2.abilityIcon:00}") != null
                        ? $"Original/sprites/Buff6/BuffMedal{ent2.abilityIcon:00}"
                        : "Original/sprites/Buff6/BuffMedal17";
                    var entImg2 = AddResourceImage("HomeEntourage2", iconPath, _safeArea,
                        new Vector2(0.485f, 0.135f), new Vector2(0.565f, 0.195f));
                    entImg2.preserveAspect = true;
                }
            }

            // Report text inside BuffBelt:
            _matchReport = AddText(GetEncounterPreviewText(), _safeArea,
                12, Paper, new Vector2(0.04f, 0.095f), new Vector2(0.96f, 0.135f));
            _matchReport.resizeTextForBestFit = true;
            _matchReport.resizeTextMinSize = 9;
            _matchReport.resizeTextMaxSize = 13;
            _matchReport.verticalOverflow = VerticalWrapMode.Truncate;

            // Buttons inside BuffBelt:
            _substitutionButton = AddSpriteButton("Substitute",
                "Original/images/SubstitutionButton",
                "Original/images/SubstitutionButtonPressed",
                _safeArea,
                new Vector2(0.26f, 0.048f), new Vector2(0.44f, 0.065f),
                ShowSubstitutionSlots);
            _substitutionButton.interactable = !_match.IsFinished && _ownedPlayerIds.Count > 7;

            _encounterButton = AddSpriteButton("RollButton",
                "Original/images/PlayOnButton",
                "Original/images/PlayOnButtonPressed",
                _safeArea,
                new Vector2(0.74f, 0.048f), new Vector2(0.44f, 0.065f),
                PlayEncounter);

            // Overlay container for choreographed animations:
            var overlayGo = new GameObject("OverlayContainer", typeof(RectTransform));
            overlayGo.transform.SetParent(_safeArea, false);
            _overlayContainer = overlayGo.GetComponent<RectTransform>();
            _overlayContainer.anchorMin = Vector2.zero;
            _overlayContainer.anchorMax = Vector2.one;
            _overlayContainer.offsetMin = _overlayContainer.offsetMax = Vector2.zero;

            // Kick-off banner sequence:
            if (!_isKickoffPlayed)
            {
                _isKickoffPlayed = true;
                StartCoroutine(PlayKickOffAnimation());
            }

            // Poof smoke if substituted:
            if (_lastSubstitutedSlot >= 0)
            {
                StartCoroutine(PlayPoofAnimation(_lastSubstitutedSlot));
                _lastSubstitutedSlot = -1;
            }
        }

        private static Color32 GetCardBorderColor(int playerTypeOrId)
        {
            // ARMv7 0x5a0b6 .. 0x5a130:
            // r2 <= 0x20 (32): 0x00fca757 -> (252, 167, 87) Bronze/Gold
            // r2 <= 0x30 (48): 0x003737fc -> (252, 55, 55) Red/Ruby
            // r2 <= 0x40 (64): 0x002dccfc -> (252, 204, 45) Yellow/Gold
            // r2 <= 0x50 (80): 0x00c9c9c9 -> (201, 201, 201) Silver
            // r2 > 80:         0x00ffffff -> (255, 255, 255) Platinum/White
            if (playerTypeOrId <= 32) return new Color32(252, 167, 87, 255);
            if (playerTypeOrId <= 48) return new Color32(252, 55, 55, 255);
            if (playerTypeOrId <= 64) return new Color32(252, 204, 45, 255);
            if (playerTypeOrId <= 80) return new Color32(201, 201, 201, 255);
            return new Color32(255, 255, 255, 255);
        }

        private static Color32 GetPositionColor(string position)
        {
            // ARMv7 0x5a420 .. 0x5a4f6:
            switch (position)
            {
                case "GK": return new Color32(40, 40, 40, 255);
                case "FWD": return new Color32(246, 50, 50, 255);
                case "DEF": return new Color32(0, 100, 255, 255);
                case "MID": return new Color32(50, 180, 50, 255);
                default: return new Color32(80, 80, 80, 255);
            }
        }

        private static string GetPlayerNumberSpritePath(int digit)
        {
            return digit <= 4
                ? $"Original/sprites/Card3/PlayerNumber{digit}"
                : $"Original/sprites/Card6/PlayerNumber{digit}";
        }

        private static string GetDiceSideSpritePath(int sideValue)
        {
            if (sideValue >= 16)
            {
                var abilityPath = $"Original/sprites/Card3/DiceFaceAbility{sideValue}";
                if (TryGetUiSprite(abilityPath) != null)
                    return abilityPath;
            }
            return $"Original/sprites/Card6/DiceNumber{Mathf.Clamp(sideValue, 1, 14):02}";
        }

        private static string GetSuperpowerIconPath(int superpowerId)
        {
            if (superpowerId <= 0) return null;
            var path = $"Original/sprites/Card3/DiceFaceAbility{superpowerId}";
            return TryGetUiSprite(path) != null ? path : null;
        }

        private static string GetPlayerFigurePath(int playerId)
        {
            var path = $"Original/sprites/PlayerFullFigureSS/PlayerFullFigure{playerId:000}";
            if (TryGetUiSprite(path) != null)
                return path;
            return "Original/sprites/PlayerFullFigureSS/PlayerFullFigure001";
        }

        private GameObject CreatePlayerCardFront(PlayerData player, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, bool isHire = false, bool isInteractive = true,
            UnityEngine.Events.UnityAction onClick = null)
        {
            var root = new GameObject($"CardFront_{player.id}", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var border = AddResourceImage("Border", "Original/sprites/PlayerCardBG/CardBorder", root.transform,
                Vector2.zero, Vector2.one);
            border.color = GetCardBorderColor(player.id);
            border.preserveAspect = false;

            var field = AddResourceImage("Field", "Original/sprites/PlayerCardBG/CardField", border.transform,
                new Vector2(0.045f, 0.035f), new Vector2(0.955f, 0.965f));
            field.preserveAspect = false;

            // Number badge
            var numBase = AddResourceImage("NumberBase", "Original/sprites/Card3/PlayerNumberBase", field.transform,
                new Vector2(0.06f, 0.77f), new Vector2(0.33f, 0.95f));
            numBase.color = GetPositionColor(player.position);
            numBase.preserveAspect = true;

            var numOutline = AddResourceImage("NumberOutline", "Original/sprites/Card3/PlayerNumberOutline", numBase.transform,
                Vector2.zero, Vector2.one);
            numOutline.preserveAspect = true;

            if (player.id < 10)
            {
                var dPath = GetPlayerNumberSpritePath(player.id % 10);
                AddResourceImage("Digit", dPath, numBase.transform,
                    new Vector2(0.25f, 0.2f), new Vector2(0.75f, 0.8f)).preserveAspect = true;
            }
            else
            {
                var d1Path = GetPlayerNumberSpritePath(player.id / 10);
                var d2Path = GetPlayerNumberSpritePath(player.id % 10);
                AddResourceImage("Digit1", d1Path, numBase.transform,
                    new Vector2(0.12f, 0.2f), new Vector2(0.50f, 0.8f)).preserveAspect = true;
                AddResourceImage("Digit2", d2Path, numBase.transform,
                    new Vector2(0.50f, 0.2f), new Vector2(0.88f, 0.8f)).preserveAspect = true;
            }

            // Position badge
            var posPath = $"Original/sprites/Card6/{player.position}Type";
            var posImg = AddResourceImage("PositionType", posPath, field.transform,
                new Vector2(0.70f, 0.81f), new Vector2(0.93f, 0.94f));
            posImg.preserveAspect = true;

            // Player figure & shadow
            var shadow = AddResourceImage("FigureShadow", "Original/sprites/PlayerFullFigureSS/PlayerFullFigureShadow", field.transform,
                new Vector2(0.20f, 0.32f), new Vector2(0.80f, 0.78f));
            shadow.preserveAspect = true;
            shadow.color = new Color(0, 0, 0, 0.45f);

            var figPath = GetPlayerFigurePath(player.id);
            var figure = AddResourceImage("Figure", figPath, field.transform,
                new Vector2(0.20f, 0.33f), new Vector2(0.80f, 0.79f));
            figure.preserveAspect = true;

            // Dice bar
            var diceBarColour = AddResourceImage("DiceBarColour", "Original/sprites/Card3/DiceBarColour", field.transform,
                new Vector2(0.06f, 0.17f), new Vector2(0.94f, 0.31f));
            diceBarColour.color = GetPositionColor(player.position);
            diceBarColour.preserveAspect = false;

            var diceBarBorder = AddResourceImage("DiceBarBorder", "Original/sprites/Card3/DiceBarBorder", diceBarColour.transform,
                Vector2.zero, Vector2.one);
            diceBarBorder.preserveAspect = false;

            for (var i = 0; i < 6; i++)
            {
                var w = 1f / 6f;
                var minX = i * w + 0.02f;
                var maxX = (i + 1) * w - 0.02f;
                var side = (player.diceSides != null && i < player.diceSides.Length) ? player.diceSides[i] : 1;
                var sidePath = GetDiceSideSpritePath(side);
                var sideImg = AddResourceImage($"DiceSide{i + 1}", sidePath, diceBarColour.transform,
                    new Vector2(minX, 0.16f), new Vector2(maxX, 0.84f));
                sideImg.preserveAspect = true;
            }

            // Superpower Box & Name
            var spBox = AddResourceImage("SPBox", "Original/sprites/Card6/SPBox", field.transform,
                new Vector2(0.07f, 0.04f), new Vector2(0.25f, 0.16f));
            spBox.preserveAspect = true;

            var spIconPath = GetSuperpowerIconPath(player.superpower);
            if (!string.IsNullOrEmpty(spIconPath))
            {
                var spIcon = AddResourceImage("SPIcon", spIconPath, spBox.transform,
                    new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
                spIcon.preserveAspect = true;
            }

            var nameText = AddText(player.name.ToUpperInvariant(), field.transform, 17, Ink,
                new Vector2(0.27f, 0.04f), new Vector2(0.93f, 0.16f));
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.fontStyle = FontStyle.Bold;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 10;
            nameText.resizeTextMaxSize = 17;

            if (isHire)
            {
                var hireImg = AddResourceImage("HireMe", "Original/sprites/PlayerCardBG/CardHireMe", border.transform,
                    Vector2.zero, Vector2.one);
                hireImg.preserveAspect = false;
                var costText = AddText($"{player.cost:N0}", hireImg.transform, 24, Paper,
                    new Vector2(0.2f, 0.42f), new Vector2(0.8f, 0.52f));
                costText.fontStyle = FontStyle.Bold;
            }

            if (isInteractive)
            {
                var btn = root.AddComponent<Button>();
                btn.onClick.AddListener(() => onClick?.Invoke());
            }

            return root;
        }

        private GameObject CreatePlayerCardBack(PlayerData player, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, bool isInteractive = true,
            UnityEngine.Events.UnityAction onClick = null)
        {
            var root = new GameObject($"CardBack_{player.id}", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var back = AddResourceImage("CardBack", "Original/sprites/PlayerCardBG/CardBack", root.transform,
                Vector2.zero, Vector2.one);
            back.preserveAspect = false;

            var nameText = AddText(player.name.ToUpperInvariant(), back.transform, 20, Ink,
                new Vector2(0.08f, 0.87f), new Vector2(0.92f, 0.94f));
            nameText.fontStyle = FontStyle.Bold;

            var roleText = AddText($"{player.position}  |  #{player.id}", back.transform, 14, Gold,
                new Vector2(0.1f, 0.81f), new Vector2(0.9f, 0.86f));
            roleText.fontStyle = FontStyle.Bold;

            // Primary Superpower
            var spBox = AddResourceImage("SPBox", "Original/sprites/Card6/SPBox", back.transform,
                new Vector2(0.08f, 0.65f), new Vector2(0.26f, 0.79f));
            spBox.preserveAspect = true;

            var spIconPath = GetSuperpowerIconPath(player.superpower);
            if (!string.IsNullOrEmpty(spIconPath))
            {
                var spIcon = AddResourceImage("SPIcon", spIconPath, spBox.transform,
                    new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
                spIcon.preserveAspect = true;
            }

            var ability = player.superpower > 0 ? _catalog.GetAbility(player.superpower) : null;
            var abName = ability != null ? ability.name : "Normal Player";
            var abDesc = ability != null ? ability.description : "No special superpower assigned to this player.";

            var title = AddText(abName.ToUpperInvariant(), back.transform, 15, Ink,
                new Vector2(0.28f, 0.72f), new Vector2(0.92f, 0.79f));
            title.alignment = TextAnchor.MiddleLeft;
            title.fontStyle = FontStyle.Bold;

            var desc = AddText(abDesc, back.transform, 11, Ink,
                new Vector2(0.28f, 0.58f), new Vector2(0.92f, 0.72f));
            desc.alignment = TextAnchor.UpperLeft;

            // Additional Abilities from dice sides
            var extraAbilities = new List<int>();
            if (player.diceSides != null)
            {
                foreach (var side in player.diceSides)
                {
                    if (side >= 16 && side != player.superpower && !extraAbilities.Contains(side))
                        extraAbilities.Add(side);
                }
            }

            var bioTop = 0.54f;
            if (extraAbilities.Count > 0)
            {
                var extraHeader = AddText("ADDITIONAL ABILITIES:", back.transform, 12, Gold,
                    new Vector2(0.08f, 0.49f), new Vector2(0.92f, 0.55f));
                extraHeader.alignment = TextAnchor.MiddleLeft;
                extraHeader.fontStyle = FontStyle.Bold;

                var extraAb = _catalog.GetAbility(extraAbilities[0]);
                if (extraAb != null)
                {
                    var extraText = AddText($"{extraAb.name}: {extraAb.description}", back.transform, 10, Ink,
                        new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.49f));
                    extraText.alignment = TextAnchor.UpperLeft;
                }
                bioTop = 0.38f;
            }

            // Player Bio
            var bioHeader = AddText("PLAYER BIO", back.transform, 13, Gold,
                new Vector2(0.08f, bioTop - 0.05f), new Vector2(0.92f, bioTop));
            bioHeader.alignment = TextAnchor.MiddleLeft;
            bioHeader.fontStyle = FontStyle.Bold;

            var bioContent = string.IsNullOrWhiteSpace(player.description)
                ? "No additional player biography available."
                : player.description;
            var bioText = AddText(bioContent, back.transform, 12, Ink,
                new Vector2(0.08f, 0.08f), new Vector2(0.92f, bioTop - 0.05f));
            bioText.alignment = TextAnchor.UpperLeft;

            if (isInteractive)
            {
                var btn = root.AddComponent<Button>();
                btn.onClick.AddListener(() => onClick?.Invoke());
            }

            return root;
        }

        private void ShowCardModal(PlayerData player, UnityEngine.Events.UnityAction onAction = null,
            string actionLabel = null, bool isHire = false)
        {
            var modalBackdrop = AddPanel("CardModalBackdrop", _safeArea,
                new Color(0f, 0f, 0f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.one);
            modalBackdrop.raycastTarget = true;

            var cardContainer = new GameObject("CardContainer", typeof(RectTransform));
            cardContainer.transform.SetParent(modalBackdrop.transform, false);
            var containerRect = cardContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.56f);
            containerRect.anchorMax = new Vector2(0.5f, 0.56f);
            containerRect.sizeDelta = new Vector2(340f, 510f); // 2:3 ratio

            GameObject frontObj = null;
            GameObject backObj = null;
            var isFlipping = false;
            var showingBack = false;

            UnityEngine.Events.UnityAction flipAction = () =>
            {
                if (isFlipping) return;
                StartCoroutine(FlipCardAnimation(containerRect, frontObj, backObj, !showingBack, () =>
                {
                    showingBack = !showingBack;
                    isFlipping = false;
                }));
                isFlipping = true;
            };

            frontObj = CreatePlayerCardFront(player, cardContainer.transform, Vector2.zero, Vector2.one,
                isHire, true, flipAction);
            backObj = CreatePlayerCardBack(player, cardContainer.transform, Vector2.zero, Vector2.one,
                true, flipAction);
            backObj.SetActive(false);

            AddText("TAP CARD TO FLIP", modalBackdrop.transform, 14, Gold,
                new Vector2(0.1f, 0.25f), new Vector2(0.9f, 0.29f));

            if (onAction != null && !string.IsNullOrEmpty(actionLabel))
            {
                AddButton(actionLabel, modalBackdrop.transform, Grass,
                    new Vector2(0.5f, 0.18f), new Vector2(0.78f, 0.07f), () =>
                    {
                        Destroy(modalBackdrop.gameObject);
                        onAction.Invoke();
                    });
            }

            AddButton("CLOSE", modalBackdrop.transform, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.09f), new Vector2(0.55f, 0.06f), () =>
                {
                    Destroy(modalBackdrop.gameObject);
                });
        }

        private IEnumerator FlipCardAnimation(RectTransform cardRect, GameObject frontObj, GameObject backObj,
            bool toBack, Action onComplete)
        {
            AudioManager.Instance.PlayTap();
            var duration = 0.14f;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                cardRect.localScale = new Vector3(Mathf.Lerp(1f, 0f, t), 1f, 1f);
                yield return null;
            }

            frontObj.SetActive(!toBack);
            backObj.SetActive(toBack);

            elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                cardRect.localScale = new Vector3(Mathf.Lerp(0f, 1f, t), 1f, 1f);
                yield return null;
            }

            cardRect.localScale = Vector3.one;
            onComplete?.Invoke();
        }

        private void AddPlayerCardRow(PlayerData player, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            string rightButtonLabel, Color rightButtonColor, bool buttonInteractable,
            UnityEngine.Events.UnityAction onButtonClick, UnityEngine.Events.UnityAction onCardClick,
            bool isHire = false, string headerPrefix = null)
        {
            var rowPanel = AddPanel($"PlayerRow_{player.id}", parent, new Color32(32, 42, 50, 240),
                (anchorMin + anchorMax) * 0.5f, anchorMax - anchorMin);

            // Left: Mini collectible card preview (aspect ratio 2:3)
            var cardContainer = new GameObject("MiniCardPreview", typeof(RectTransform));
            cardContainer.transform.SetParent(rowPanel.transform, false);
            var cardRect = cardContainer.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.015f, 0.06f);
            cardRect.anchorMax = new Vector2(0.19f, 0.94f);
            cardRect.offsetMin = cardRect.offsetMax = Vector2.zero;

            CreatePlayerCardFront(player, cardContainer.transform, Vector2.zero, Vector2.one, isHire, true, onCardClick);

            // Center details
            var centerObj = new GameObject("Details", typeof(RectTransform));
            centerObj.transform.SetParent(rowPanel.transform, false);
            var centerRect = centerObj.GetComponent<RectTransform>();
            centerRect.anchorMin = new Vector2(0.205f, 0.06f);
            centerRect.anchorMax = new Vector2(0.74f, 0.94f);
            centerRect.offsetMin = centerRect.offsetMax = Vector2.zero;

            var titleStr = string.IsNullOrEmpty(headerPrefix)
                ? $"{player.name} ({player.position})"
                : $"{headerPrefix}: {player.name} ({player.position})";
            var title = AddText(titleStr, centerObj.transform, 14, Paper,
                new Vector2(0f, 0.68f), new Vector2(1f, 1f));
            title.alignment = TextAnchor.MiddleLeft;
            title.fontStyle = FontStyle.Bold;

            // 6 Dice icons row in center
            var diceBarObj = new GameObject("MiniDiceRow", typeof(RectTransform));
            diceBarObj.transform.SetParent(centerObj.transform, false);
            var diceBarRect = diceBarObj.GetComponent<RectTransform>();
            diceBarRect.anchorMin = new Vector2(0f, 0.32f);
            diceBarRect.anchorMax = new Vector2(0.95f, 0.68f);
            diceBarRect.offsetMin = diceBarRect.offsetMax = Vector2.zero;

            if (player.diceSides != null)
            {
                var slotW = 1f / 6f;
                for (var i = 0; i < 6 && i < player.diceSides.Length; i++)
                {
                    var side = player.diceSides[i];
                    var sidePath = GetDiceSideSpritePath(side);
                    var diceImg = AddResourceImage($"Dice{i}", sidePath, diceBarObj.transform,
                        new Vector2(i * slotW + 0.01f, 0.05f), new Vector2((i + 1) * slotW - 0.01f, 0.95f));
                    diceImg.preserveAspect = true;
                }
            }

            // Superpower label
            var abilityName = player.superpower > 0 ? _catalog.GetAbility(player.superpower).name : "No ability";
            var abText = AddText(abilityName, centerObj.transform, 11, Gold,
                new Vector2(0f, 0f), new Vector2(1f, 0.32f));
            abText.alignment = TextAnchor.MiddleLeft;

            // Right button
            var button = AddButton(rightButtonLabel, rowPanel.transform, rightButtonColor,
                new Vector2(0.865f, 0.5f), new Vector2(0.23f, 0.72f), onButtonClick);
            button.interactable = buttonInteractable;
        }

        private void ShowSubstitutionSlots()
        {
            if (_match == null || !_match.CanBeginEncounter || _remainingSubs <= 0)
                return;

            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader($"SUBSTITUTE PLAYER ({_remainingSubs} LEFT)",
                () => ShowMatchScreen(_catalog.ResolveLineup(_lineupIds)));
            AddText("Choose the position to change.",
                _safeArea, 15, Paper, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.88f));
            var content = CreateScrollList("Substitution positions", _safeArea,
                new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.81f), 7 * 96f);
            var lineup = _catalog.ResolveLineup(_lineupIds);
            for (var slot = 0; slot < lineup.Length; slot++)
            {
                var capturedSlot = slot;
                var player = lineup[slot];
                var yMin = 1f - (slot + 1) / 7f;
                var yMax = 1f - slot / 7f;
                AddPlayerCardRow(player, content,
                    new Vector2(0.02f, yMin + 0.005f), new Vector2(0.98f, yMax - 0.005f),
                    "CHANGE", new Color32(68, 79, 84, 255), true,
                    () => ShowSubstitutionBench(capturedSlot),
                    () => ShowCardModal(player),
                    false, SlotName(slot));
            }
            AddButton("CANCEL", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.08f), new Vector2(0.65f, 0.06f),
                () => ShowMatchScreen(_catalog.ResolveLineup(_lineupIds)));
        }

        private void ShowSubstitutionBench(int slot)
        {
            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader($"BENCH — {SlotName(slot)}", ShowSubstitutionSlots);
            AddText("Select a role-eligible player to replace the current player.",
                _safeArea, 14, Paper, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.89f));

            var bench = new List<PlayerData>();
            foreach (var playerId in _ownedPlayerIds)
            {
                if (Array.IndexOf(_lineupIds, playerId) < 0)
                {
                    var player = _catalog.GetPlayer(playerId);
                    if (IsRoleAllowed(player, slot))
                        bench.Add(player);
                }
            }

            if (bench.Count == 0)
            {
                AddText("No eligible player is available on the bench.",
                    _safeArea, 16, Paper, new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.58f));
            }
            else
            {
                var content = CreateScrollList("Bench players", _safeArea,
                    new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.8f), bench.Count * 96f);
                for (var index = 0; index < bench.Count; index++)
                {
                    var player = bench[index];
                    var capturedPlayerId = player.id;
                    var yMin = 1f - (index + 1) / (float)bench.Count;
                    var yMax = 1f - index / (float)bench.Count;
                    AddPlayerCardRow(player, content,
                        new Vector2(0.02f, yMin + 0.005f), new Vector2(0.98f, yMax - 0.005f),
                        "SUB", Grass, true,
                        () => CompleteSubstitution(slot, capturedPlayerId),
                        () => ShowCardModal(player, () => CompleteSubstitution(slot, capturedPlayerId),
                            "SUBSTITUTE INTO MATCH"));
                }
            }

            AddButton("CANCEL", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.08f), new Vector2(0.65f, 0.06f),
                ShowSubstitutionSlots);
        }

        private void CompleteSubstitution(int slot, int playerId)
        {
            var replacement = _catalog.GetPlayer(playerId);
            _match.SubstituteHomePlayer(slot, replacement);
            _lineupIds[slot] = playerId;
            _remainingSubs = Mathf.Max(0, _remainingSubs - 1);
            PlayerPrefs.SetString(LineupPreferenceKey, string.Join(",", _lineupIds));
            PlayerPrefs.Save();
            _lastSubstitutedSlot = slot;
            AudioManager.Instance.PlayTransform();
            ShowMatchScreen(_catalog.ResolveLineup(_lineupIds));
        }

        private void ShowManage()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader("PLAYERS", ShowHome);
            AddText("Tap a card to inspect stats, or press Change to swap.",
                _safeArea, 15, Paper, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.88f));

            var content = CreateScrollList("Lineup slots", _safeArea,
                new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.82f), 7 * 96f);
            var lineup = _catalog.ResolveLineup(_lineupIds);
            for (var slot = 0; slot < lineup.Length; slot++)
            {
                var capturedSlot = slot;
                var player = lineup[slot];
                var yMin = 1f - (slot + 1) / 7f;
                var yMax = 1f - slot / 7f;
                AddPlayerCardRow(player, content,
                    new Vector2(0.02f, yMin + 0.005f), new Vector2(0.98f, yMax - 0.005f),
                    "CHANGE", slot == _selectedLineupSlot ? Gold : new Color32(68, 79, 84, 255), true,
                    () => ShowPlayerPicker(capturedSlot),
                    () => ShowCardModal(player),
                    false, SlotName(slot));
            }

            AddButton("HIRE PLAYERS", _safeArea, Grass,
                new Vector2(0.26f, 0.115f), new Vector2(0.44f, 0.052f), ShowHiring);
            AddButton("ENTOURAGE", _safeArea, new Color32(40, 110, 180, 255),
                new Vector2(0.74f, 0.115f), new Vector2(0.44f, 0.052f), ShowEntourage);
            AddButton("CUSTOMIZE KIT", _safeArea, new Color32(180, 80, 40, 255),
                new Vector2(0.26f, 0.055f), new Vector2(0.44f, 0.052f), ShowKitSelection);
            AddButton("TROPHY ROOM", _safeArea, Gold,
                new Vector2(0.74f, 0.055f), new Vector2(0.44f, 0.052f), ShowTrophyRoom);
        }

        private void ShowPlayerPicker(int slot)
        {
            _selectedLineupSlot = slot;
            var currentPlayer = _catalog.GetPlayer(_lineupIds[slot]);
            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader($"CHOOSE {SlotName(slot)}", ShowManage);
            AddText($"Current: {currentPlayer.name}  |  {currentPlayer.position}",
                _safeArea, 15, Paper, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.87f));

            var rosterContent = CreateScrollList("Owned roster", _safeArea,
                new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.80f),
                _ownedPlayerIds.Count * 96f);
            for (var rosterIndex = 0; rosterIndex < _ownedPlayerIds.Count; rosterIndex++)
            {
                var candidate = _catalog.GetPlayer(_ownedPlayerIds[rosterIndex]);
                var capturedPlayerId = candidate.id;
                var yMin = 1f - (rosterIndex + 1) / (float)_ownedPlayerIds.Count;
                var yMax = 1f - rosterIndex / (float)_ownedPlayerIds.Count;
                var canAssign = CanAssignPlayer(capturedPlayerId, slot);
                var isCurrent = _lineupIds[slot] == candidate.id;
                var btnLabel = isCurrent ? "CURRENT" : "EQUIP";
                var btnColor = isCurrent ? Gold : (canAssign ? Grass : (Color)new Color32(50, 55, 60, 255));
                AddPlayerCardRow(candidate, rosterContent,
                    new Vector2(0.02f, yMin + 0.005f), new Vector2(0.98f, yMax - 0.005f),
                    btnLabel, btnColor, canAssign && !isCurrent,
                    () => AssignPlayerToSlot(slot, capturedPlayerId),
                    () => ShowCardModal(candidate, canAssign && !isCurrent ? () => AssignPlayerToSlot(slot, capturedPlayerId) : null,
                        canAssign && !isCurrent ? $"EQUIP FOR {SlotName(slot)}" : null));
            }

            AddButton("BACK TO LINEUP", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.08f), new Vector2(0.7f, 0.06f), ShowManage);
        }

        private void ShowHiring()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader("MANAGE", ShowHome);
            AddText("Players available to hire",
                _safeArea, 14, Paper, new Vector2(0.04f, 0.83f), new Vector2(0.96f, 0.89f));

            var candidates = new List<PlayerData>();
            foreach (var player in _catalog.Data.players)
            {
                if (player.id > 0 && player.id < 1000 && player.cost > 0 &&
                    string.IsNullOrWhiteSpace(player.unlockCriteria) &&
                    !_ownedPlayerIds.Contains(player.id))
                    candidates.Add(player);
            }
            if (candidates.Count == 0)
            {
                AddText("No additional players are currently available to hire.",
                    _safeArea, 16, Paper, new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.58f));
            }
            else
            {
                var rowHeight = 98f;
                var content = CreateScrollList("Hire candidates", _safeArea,
                    new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.81f),
                    candidates.Count * rowHeight);
                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    var capturedPlayerId = candidate.id;
                    var yMin = 1f - (i + 1) / (float)candidates.Count;
                    var yMax = 1f - i / (float)candidates.Count;
                    var canAfford = _careerCash >= candidate.cost;
                    var btnColor = canAfford ? Grass : (Color)new Color32(50, 55, 60, 255);
                    AddPlayerCardRow(candidate, content,
                        new Vector2(0.02f, yMin + 0.005f), new Vector2(0.98f, yMax - 0.005f),
                        $"{candidate.cost:N0}", btnColor, canAfford,
                        () => HirePlayer(capturedPlayerId),
                        () => ShowCardModal(candidate, canAfford ? () => HirePlayer(capturedPlayerId) : null,
                            canAfford ? $"HIRE FOR {candidate.cost:N0} CASH" : $"NEED {candidate.cost:N0} CASH",
                            isHire: true),
                        isHire: true);
                }
            }

            AddButton("BACK TO TEAM", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.08f), new Vector2(0.7f, 0.06f), ShowManage);
        }

        private void HirePlayer(int playerId)
        {
            var player = _catalog.GetPlayer(playerId);
            if (player.id <= 0 || player.id >= 1000 || player.cost <= 0 ||
                !string.IsNullOrWhiteSpace(player.unlockCriteria))
                throw new InvalidOperationException(
                    $"Player {player.name} is not eligible for hiring from the available roster.");
            if (_ownedPlayerIds.Contains(player.id))
                throw new InvalidOperationException($"Player {player.name} is already owned.");
            if (_careerCash < player.cost)
                return;

            _careerCash -= player.cost;
            _ownedPlayerIds.Add(player.id);
            PlayerPrefs.SetInt("diceSoccer.cash", _careerCash);
            PlayerPrefs.SetString(RosterPreferenceKey, string.Join(",", _ownedPlayerIds));
            PlayerPrefs.Save();
            AudioManager.Instance.PlayBuy();
            ShowHiring();
        }

        private void AssignPlayerToSlot(int targetSlot, int playerId)
        {
            if (!CanAssignPlayer(playerId, targetSlot))
                return;

            var updatedLineup = (int[])_lineupIds.Clone();
            updatedLineup[targetSlot] = playerId;
            var resolved = _catalog.ResolveLineup(updatedLineup);
            ValidateLineupRoles(resolved);
            _lineupIds = updatedLineup;
            PlayerPrefs.SetString(LineupPreferenceKey, string.Join(",", _lineupIds));
            PlayerPrefs.Save();
            AudioManager.Instance.PlayTap();
            ShowManage();
        }

        private bool CanAssignPlayer(int playerId, int targetSlot)
        {
            if (_lineupIds == null || _ownedPlayerIds == null ||
                targetSlot < 0 || targetSlot >= _lineupIds.Length ||
                !_ownedPlayerIds.Contains(playerId))
                return false;

            var duplicateSlot = Array.IndexOf(_lineupIds, playerId);
            if (duplicateSlot >= 0 && duplicateSlot != targetSlot)
                return false;
            return IsRoleAllowed(_catalog.GetPlayer(playerId), targetSlot);
        }

        private static bool IsRoleAllowed(PlayerData player, int slot)
        {
            if (slot == 0)
                return player.position == "GK";
            if (player.position == "GK")
                return false;
            if (player.position == "MID")
                return true;
            return slot <= 3 ? player.position == "DEF" : player.position == "FWD";
        }

        private void ValidateLineupRoles(PlayerData[] lineup)
        {
            var playerIds = new HashSet<int>();
            for (var slot = 0; slot < lineup.Length; slot++)
            {
                if (!playerIds.Add(lineup[slot].id))
                    throw new InvalidOperationException(
                        $"Player {lineup[slot].name} cannot occupy more than one lineup slot.");
                if (!IsRoleAllowed(lineup[slot], slot))
                    throw new InvalidOperationException(
                        $"Player {lineup[slot].name} cannot fill {SlotName(slot)}.");
            }
        }

        private static string SlotName(int slot)
        {
            switch (slot)
            {
                case 0: return "Goalkeeper";
                case 1: return "Left Defender";
                case 2: return "Central Defender";
                case 3: return "Right Defender";
                case 4: return "Left Forward";
                case 5: return "Central Forward";
                case 6: return "Right Forward";
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private void EnsureLineupLoaded()
        {
            if (_ownedPlayerIds == null)
            {
                var savedRoster = PlayerPrefs.GetString(RosterPreferenceKey, string.Empty);
                _ownedPlayerIds = string.IsNullOrEmpty(savedRoster)
                    ? new List<int>(_catalog.Data.initialLineup)
                    : ParsePlayerIds(savedRoster, "saved roster");
                if (_ownedPlayerIds.Count == 0 || _ownedPlayerIds.Contains(0) ||
                    _ownedPlayerIds.Count != new HashSet<int>(_ownedPlayerIds).Count)
                    throw new InvalidDataException("The saved roster contains missing or duplicate player IDs.");
                foreach (var playerId in _ownedPlayerIds)
                    _catalog.GetPlayer(playerId);
            }

            if (_lineupIds == null)
            {
                var savedLineup = PlayerPrefs.GetString(LineupPreferenceKey, string.Empty);
                _lineupIds = string.IsNullOrEmpty(savedLineup)
                    ? (int[])_catalog.Data.initialLineup.Clone()
                    : ParsePlayerIds(savedLineup, "saved lineup").ToArray();
            }
            if (_lineupIds.Length != 7 || _lineupIds.Any(id => !_ownedPlayerIds.Contains(id)))
                throw new InvalidDataException("The saved lineup must contain seven owned player IDs.");
            ValidateLineupRoles(_catalog.ResolveLineup(_lineupIds));
        }

        private static List<int> ParsePlayerIds(string serialized, string source)
        {
            var ids = new List<int>();
            foreach (var value in serialized.Split(','))
            {
                if (!int.TryParse(value, out var playerId) || playerId <= 0)
                    throw new InvalidDataException($"The {source} contains an invalid player ID.");
                ids.Add(playerId);
            }
            return ids;
        }

        private static Transform CreateScrollList(string objectName, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, float contentHeight)
        {
            var viewportObject = new GameObject(objectName, typeof(RectTransform), typeof(Image),
                typeof(Mask), typeof(ScrollRect));
            viewportObject.transform.SetParent(parent, false);
            var viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = anchorMin;
            viewportRect.anchorMax = anchorMax;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.04f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, contentHeight);

            var scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return contentRect;
        }

        private void ShowShop()
        {
            _catalog ??= GameCatalog.Load();
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader("SHOP", ShowHome);
            AddMenuFooter();

            const float tabY = 0.88f;
            const float tabW = 0.28f;
            const float tabH = 0.055f;

            // Coins Tab
            var coinsSprite = _shopTab == 0 ? "Original/sprites/ShopSS/CoinsTab" : "Original/sprites/ShopSS/CoinsTabPressed";
            AddSpriteButton("CoinsTab", coinsSprite, "Original/sprites/ShopSS/CoinsTab", _safeArea,
                new Vector2(0.2f, tabY), new Vector2(tabW, tabH), () =>
                {
                    _shopTab = 0;
                    _shopPage = 0;
                    AudioManager.Instance.PlayTap();
                    ShowShop();
                });

            // Shirts Tab
            var shirtsSprite = _shopTab == 1 ? "Original/sprites/ShopSS/ShirtTab" : "Original/sprites/ShopSS/ShirtTabPressed";
            AddSpriteButton("ShirtTab", shirtsSprite, "Original/sprites/ShopSS/ShirtTab", _safeArea,
                new Vector2(0.5f, tabY), new Vector2(tabW, tabH), () =>
                {
                    _shopTab = 1;
                    _shopPage = 0;
                    AudioManager.Instance.PlayTap();
                    ShowShop();
                });

            // Shorts Tab
            var shortsSprite = _shopTab == 2 ? "Original/sprites/ShopSS/ShortsTab" : "Original/sprites/ShopSS/ShortsTabPressed";
            AddSpriteButton("ShortsTab", shortsSprite, "Original/sprites/ShopSS/ShortsTab", _safeArea,
                new Vector2(0.8f, tabY), new Vector2(tabW, tabH), () =>
                {
                    _shopTab = 2;
                    _shopPage = 0;
                    AudioManager.Instance.PlayTap();
                    ShowShop();
                });

            if (_shopTab == 0)
                RenderShopCoins();
            else if (_shopTab == 1)
                RenderShopShirts();
            else
                RenderShopShorts();
        }

        private void RenderShopCoins()
        {
            var cashList = _catalog.Data.shopCash ?? Array.Empty<ShopCashData>();
            const int perPage = 3;
            var maxPages = Mathf.Max(1, Mathf.CeilToInt(cashList.Length / (float)perPage));
            _shopPage = Mathf.Clamp(_shopPage, 0, maxPages - 1);

            var startIdx = _shopPage * perPage;
            var count = Mathf.Min(perPage, cashList.Length - startIdx);
            var boxYs = new[] { 0.71f, 0.51f, 0.31f };

            for (var i = 0; i < count; i++)
            {
                var option = cashList[startIdx + i];
                var boxY = boxYs[i];

                var box = AddResourceImage($"ShopBox_{i}", "Original/sprites/ShopSS/ShopBox", _safeArea,
                    new Vector2(0.06f, boxY - 0.085f), new Vector2(0.94f, boxY + 0.085f));
                box.preserveAspect = false;

                var iconPath = option.price >= 9.99f
                    ? "Original/sprites/ShopSS/ShopMoneyBar"
                    : "Original/sprites/ShopSS/ShopMoneyPouch";
                var icon = AddResourceImage("CoinIcon", iconPath, box.transform,
                    new Vector2(0.04f, 0.15f), new Vector2(0.32f, 0.85f));
                icon.preserveAspect = true;

                AddText($"{option.coins:N0}\nCOINS", box.transform, 18, Gold,
                    new Vector2(0.32f, 0.15f), new Vector2(0.70f, 0.85f));

                var capturedOption = option;
                AddButton($"${option.price:F2}", box.transform, Grass,
                    new Vector2(0.83f, 0.5f), new Vector2(0.24f, 0.48f), () =>
                    {
                        _careerCash += capturedOption.coins;
                        SaveCareerState();
                        AudioManager.Instance.PlayCash();
                        ShowShop();
                    });
            }

            RenderShopPagination(maxPages, () => ShowShop());
        }

        private void RenderShopShirts()
        {
            var shirts = _catalog.Data.shirts ?? Array.Empty<ShirtData>();
            const int perPage = 3;
            var maxPages = Mathf.Max(1, Mathf.CeilToInt(shirts.Length / (float)perPage));
            _shopPage = Mathf.Clamp(_shopPage, 0, maxPages - 1);

            var startIdx = _shopPage * perPage;
            var count = Mathf.Min(perPage, shirts.Length - startIdx);
            var boxYs = new[] { 0.71f, 0.51f, 0.31f };

            for (var i = 0; i < count; i++)
            {
                var shirt = shirts[startIdx + i];
                var boxY = boxYs[i];

                var box = AddResourceImage($"ShopBox_Shirt_{i}", "Original/sprites/ShopSS/ShopBox", _safeArea,
                    new Vector2(0.06f, boxY - 0.085f), new Vector2(0.94f, boxY + 0.085f));
                box.preserveAspect = false;

                var previewBox = new GameObject("KitPreview", typeof(RectTransform));
                previewBox.transform.SetParent(box.transform, false);
                var prevRect = previewBox.GetComponent<RectTransform>();
                prevRect.anchorMin = new Vector2(0.05f, 0.15f);
                prevRect.anchorMax = new Vector2(0.30f, 0.85f);
                prevRect.offsetMin = Vector2.zero;
                prevRect.offsetMax = Vector2.zero;

                var shirtBase = AddResourceImage("ShirtBase", "Original/sprites/KitSelectionSS/KitSelectionShirt", previewBox.transform,
                    Vector2.zero, Vector2.one);
                shirtBase.preserveAspect = true;
                shirtBase.color = _catalog.GetColourRgb(shirt.shirtColour);

                var sleeves = AddResourceImage("Sleeves", "Original/sprites/KitSelectionSS/KitSelectionSleeves", previewBox.transform,
                    Vector2.zero, Vector2.one);
                sleeves.preserveAspect = true;
                sleeves.color = _catalog.GetColourRgb(shirt.sleeveColour);

                if (shirt.shirtStyle > 0 && shirt.shirtStyle <= 7)
                {
                    var pattern = AddResourceImage("Pattern", $"Original/sprites/KitSelectionSS/KitSelectionDesign{shirt.shirtStyle}", previewBox.transform,
                        Vector2.zero, Vector2.one);
                    pattern.preserveAspect = true;
                    pattern.color = _catalog.GetColourRgb(shirt.styleColour);
                }

                AddText($"KIT #{shirt.id}\nStyle {shirt.shirtStyle}", box.transform, 17, Ink,
                    new Vector2(0.32f, 0.15f), new Vector2(0.68f, 0.85f));

                var capturedShirt = shirt;
                var owned = _unlockedShirtIds.Contains(shirt.id);
                if (owned)
                {
                    var soldOut = AddResourceImage("SoldOut", "Original/sprites/ShopSS/ShopSoldOut", box.transform,
                        new Vector2(0.72f, 0.25f), new Vector2(0.95f, 0.75f));
                    soldOut.preserveAspect = true;
                }
                else
                {
                    var cost = Mathf.RoundToInt(shirt.price * 50000f);
                    var canAfford = _careerCash >= cost;
                    AddButton(canAfford ? $"{cost:N0}" : $"${shirt.price:F2}", box.transform, canAfford ? Grass : new Color32(60, 60, 60, 255),
                        new Vector2(0.83f, 0.5f), new Vector2(0.24f, 0.48f), () =>
                        {
                            if (_careerCash >= cost)
                            {
                                _careerCash -= cost;
                                _unlockedShirtIds.Add(capturedShirt.id);
                                SaveCareerState();
                                AudioManager.Instance.PlayBuy();
                                ShowShop();
                            }
                        });
                }
            }

            RenderShopPagination(maxPages, () => ShowShop());
        }

        private void RenderShopShorts()
        {
            var colours = _catalog.Data.colours ?? Array.Empty<ColourData>();
            const int perPage = 3;
            var maxPages = Mathf.Max(1, Mathf.CeilToInt(colours.Length / (float)perPage));
            _shopPage = Mathf.Clamp(_shopPage, 0, maxPages - 1);

            var startIdx = _shopPage * perPage;
            var count = Mathf.Min(perPage, colours.Length - startIdx);
            var boxYs = new[] { 0.71f, 0.51f, 0.31f };

            for (var i = 0; i < count; i++)
            {
                var col = colours[startIdx + i];
                var boxY = boxYs[i];

                var box = AddResourceImage($"ShopBox_Shorts_{i}", "Original/sprites/ShopSS/ShopBox", _safeArea,
                    new Vector2(0.06f, boxY - 0.085f), new Vector2(0.94f, boxY + 0.085f));
                box.preserveAspect = false;

                var shorts = AddResourceImage("ShortsPreview", "Original/sprites/KitSelectionSS/KitSelectionShorts", box.transform,
                    new Vector2(0.05f, 0.15f), new Vector2(0.30f, 0.85f));
                shorts.preserveAspect = true;
                shorts.color = _catalog.GetColourRgb(col.id);

                AddText($"SHORTS #{col.id}", box.transform, 17, Ink,
                    new Vector2(0.32f, 0.15f), new Vector2(0.68f, 0.85f));

                var capturedCol = col;
                var owned = _unlockedPantsIds.Contains(col.id);
                if (owned)
                {
                    var soldOut = AddResourceImage("SoldOut", "Original/sprites/ShopSS/ShopSoldOut", box.transform,
                        new Vector2(0.72f, 0.25f), new Vector2(0.95f, 0.75f));
                    soldOut.preserveAspect = true;
                }
                else
                {
                    const int cost = 25000;
                    var canAfford = _careerCash >= cost;
                    AddButton(canAfford ? $"{cost:N0}" : "$0.99", box.transform, canAfford ? Grass : new Color32(60, 60, 60, 255),
                        new Vector2(0.83f, 0.5f), new Vector2(0.24f, 0.48f), () =>
                        {
                            if (_careerCash >= cost)
                            {
                                _careerCash -= cost;
                                _unlockedPantsIds.Add(capturedCol.id);
                                SaveCareerState();
                                AudioManager.Instance.PlayBuy();
                                ShowShop();
                            }
                        });
                }
            }

            RenderShopPagination(maxPages, () => ShowShop());
        }

        private void RenderShopPagination(int maxPages, UnityEngine.Events.UnityAction onRefresh)
        {
            if (maxPages <= 1) return;
            const float y = 0.175f;

            if (_shopPage > 0)
            {
                AddSpriteButton("LeftArrow", "Original/images/LeftArrow", "Original/images/LeftArrowSelected", _safeArea,
                    new Vector2(0.12f, y), new Vector2(0.12f, 0.055f), () =>
                    {
                        _shopPage--;
                        AudioManager.Instance.PlayTap();
                        onRefresh?.Invoke();
                    });
            }

            AddText($"PAGE {_shopPage + 1} / {maxPages}", _safeArea, 15, Paper,
                new Vector2(0.3f, y - 0.025f), new Vector2(0.7f, y + 0.025f));

            if (_shopPage < maxPages - 1)
            {
                AddSpriteButton("RightArrow", "Original/images/RightArrow", "Original/images/RightArrowSelected", _safeArea,
                    new Vector2(0.88f, y), new Vector2(0.12f, 0.055f), () =>
                    {
                        _shopPage++;
                        AudioManager.Instance.PlayTap();
                        onRefresh?.Invoke();
                    });
            }
        }

        private void ShowKitSelection()
        {
            _catalog ??= GameCatalog.Load();
            ClearCanvas();
            AddResourceImage("KitBG", "Original/images/KitSelectionBG", _safeArea,
                Vector2.zero, Vector2.one).preserveAspect = false;
            AddMenuHeader("KIT SELECTION", ShowManage);

            if (PlayerPrefs.GetInt("diceSoccer.tutorial.3", 0) == 0)
                ShowTutorialPopup(3);

            var currentShirt = _catalog.GetShirt(_currentShirtId) ?? _catalog.Data.shirts[0];

            var kitBox = new GameObject("CenterKitPreview", typeof(RectTransform));
            kitBox.transform.SetParent(_safeArea, false);
            var kitRect = kitBox.GetComponent<RectTransform>();
            kitRect.anchorMin = new Vector2(0.2f, 0.44f);
            kitRect.anchorMax = new Vector2(0.8f, 0.88f);
            kitRect.offsetMin = Vector2.zero;
            kitRect.offsetMax = Vector2.zero;

            var shorts = AddResourceImage("Shorts", "Original/sprites/KitSelectionSS/KitSelectionShorts", kitBox.transform,
                Vector2.zero, Vector2.one);
            shorts.preserveAspect = true;
            shorts.color = _catalog.GetColourRgb(_currentPantsId);

            var shirt = AddResourceImage("Shirt", "Original/sprites/KitSelectionSS/KitSelectionShirt", kitBox.transform,
                Vector2.zero, Vector2.one);
            shirt.preserveAspect = true;
            shirt.color = _catalog.GetColourRgb(currentShirt.shirtColour);

            var sleeves = AddResourceImage("Sleeves", "Original/sprites/KitSelectionSS/KitSelectionSleeves", kitBox.transform,
                Vector2.zero, Vector2.one);
            sleeves.preserveAspect = true;
            sleeves.color = _catalog.GetColourRgb(currentShirt.sleeveColour);

            if (currentShirt.shirtStyle > 0 && currentShirt.shirtStyle <= 7)
            {
                var pat = AddResourceImage("Pattern", $"Original/sprites/KitSelectionSS/KitSelectionDesign{currentShirt.shirtStyle}", kitBox.transform,
                    Vector2.zero, Vector2.one);
                pat.preserveAspect = true;
                pat.color = _catalog.GetColourRgb(currentShirt.styleColour);
            }

            var unlockedShirts = new List<int>(_unlockedShirtIds);
            unlockedShirts.Sort();
            var unlockedPants = new List<int>(_unlockedPantsIds);
            unlockedPants.Sort();

            const float shirtY = 0.35f;
            AddSpriteButton("ShirtLeft", "Original/images/LeftArrow", "Original/images/LeftArrowSelected", _safeArea,
                new Vector2(0.18f, shirtY), new Vector2(0.12f, 0.055f), () =>
                {
                    var idx = unlockedShirts.IndexOf(_currentShirtId);
                    idx = (idx - 1 + unlockedShirts.Count) % unlockedShirts.Count;
                    _currentShirtId = unlockedShirts[idx];
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowKitSelection();
                });

            AddText($"SHIRT KIT #{_currentShirtId}", _safeArea, 18, Paper,
                new Vector2(0.28f, shirtY - 0.025f), new Vector2(0.72f, shirtY + 0.025f));

            AddSpriteButton("ShirtRight", "Original/images/RightArrow", "Original/images/RightArrowSelected", _safeArea,
                new Vector2(0.82f, shirtY), new Vector2(0.12f, 0.055f), () =>
                {
                    var idx = unlockedShirts.IndexOf(_currentShirtId);
                    idx = (idx + 1) % unlockedShirts.Count;
                    _currentShirtId = unlockedShirts[idx];
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowKitSelection();
                });

            const float shortsY = 0.25f;
            AddSpriteButton("ShortsLeft", "Original/images/LeftArrow", "Original/images/LeftArrowSelected", _safeArea,
                new Vector2(0.18f, shortsY), new Vector2(0.12f, 0.055f), () =>
                {
                    var idx = unlockedPants.IndexOf(_currentPantsId);
                    idx = (idx - 1 + unlockedPants.Count) % unlockedPants.Count;
                    _currentPantsId = unlockedPants[idx];
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowKitSelection();
                });

            AddText($"SHORTS COLOR #{_currentPantsId}", _safeArea, 18, Paper,
                new Vector2(0.28f, shortsY - 0.025f), new Vector2(0.72f, shortsY + 0.025f));

            AddSpriteButton("ShortsRight", "Original/images/RightArrow", "Original/images/RightArrowSelected", _safeArea,
                new Vector2(0.82f, shortsY), new Vector2(0.12f, 0.055f), () =>
                {
                    var idx = unlockedPants.IndexOf(_currentPantsId);
                    idx = (idx + 1) % unlockedPants.Count;
                    _currentPantsId = unlockedPants[idx];
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowKitSelection();
                });

            AddSpriteButton("DoneKit", "Original/sprites/KitSelectionSS/DoneButton", "Original/sprites/KitSelectionSS/DoneButtonPressed", _safeArea,
                new Vector2(0.5f, 0.12f), new Vector2(0.48f, 0.075f), () =>
                {
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowManage();
                });
        }

        private void ShowTrophyRoom()
        {
            _catalog ??= GameCatalog.Load();
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader($"TROPHIES  {_unlockedAchievementIds.Count}/72", ShowHome);

            if (PlayerPrefs.GetInt("diceSoccer.tutorial.4", 0) == 0)
                ShowTutorialPopup(4);

            const int cols = 4;
            const int rows = 3;
            const int perPage = cols * rows;
            const int totalTrophies = 72;
            var maxPages = Mathf.CeilToInt(totalTrophies / (float)perPage);
            _trophyPage = Mathf.Clamp(_trophyPage, 0, maxPages - 1);

            var shelfYs = new[] { 0.70f, 0.48f, 0.26f };
            foreach (var sy in shelfYs)
            {
                var shelf = AddResourceImage("Shelf", "Original/sprites/TrophyRoomBGSS/WoodenShelfBG", _safeArea,
                    new Vector2(0.02f, sy - 0.08f), new Vector2(0.98f, sy - 0.05f));
                shelf.preserveAspect = false;
                shelf.raycastTarget = false;
            }

            var startIdx = _trophyPage * perPage;
            var colXs = new[] { 0.16f, 0.38f, 0.62f, 0.84f };

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var trophyId = startIdx + r * cols + c + 1;
                    if (trophyId > totalTrophies) continue;

                    var posX = colXs[c];
                    var posY = shelfYs[r] + 0.035f;
                    var unlocked = _unlockedAchievementIds.Contains(trophyId);

                    var baseSprite = unlocked ? "Original/sprites/TrophyBG/TrophyBG" : "Original/sprites/TrophyBG/TrophyBGLocked";
                    var capturedId = trophyId;
                    var slotBtn = AddSpriteButton($"TrophySlot_{capturedId}", baseSprite, baseSprite, _safeArea,
                        new Vector2(posX, posY), new Vector2(0.20f, 0.14f), () =>
                        {
                            AudioManager.Instance.PlayTap();
                            ShowTrophyDetails(capturedId);
                        });

                    var trophyImg = AddResourceImage("TrophyIcon", $"Original/images/Trophy{trophyId:02d}", slotBtn.transform,
                        new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
                    trophyImg.preserveAspect = true;
                    trophyImg.raycastTarget = false;
                    if (!unlocked)
                    {
                        trophyImg.color = new Color(0.15f, 0.15f, 0.15f, 0.6f);
                    }
                }
            }

            const float navY = 0.12f;
            if (_trophyPage > 0)
            {
                AddSpriteButton("LeftArrow", "Original/images/LeftArrow", "Original/images/LeftArrowSelected", _safeArea,
                    new Vector2(0.14f, navY), new Vector2(0.12f, 0.055f), () =>
                    {
                        _trophyPage--;
                        AudioManager.Instance.PlayTap();
                        ShowTrophyRoom();
                    });
            }

            AddText($"PAGE {_trophyPage + 1} OF {maxPages}", _safeArea, 16, Paper,
                new Vector2(0.3f, navY - 0.025f), new Vector2(0.7f, navY + 0.025f));

            if (_trophyPage < maxPages - 1)
            {
                AddSpriteButton("RightArrow", "Original/images/RightArrow", "Original/images/RightArrowSelected", _safeArea,
                    new Vector2(0.86f, navY), new Vector2(0.12f, 0.055f), () =>
                    {
                        _trophyPage++;
                        AudioManager.Instance.PlayTap();
                        ShowTrophyRoom();
                    });
            }

            AddSpriteButton("Refresh", "Original/sprites/TrophyRoomBGSS/RefreshButton", "Original/sprites/TrophyRoomBGSS/RefreshButtonPressed", _safeArea,
                new Vector2(0.92f, 0.96f), new Vector2(0.08f, 0.055f), () =>
                {
                    CheckAchievements(false, false);
                    AudioManager.Instance.PlayMedalCeremony();
                    ShowTrophyRoom();
                });
        }

        private void ShowTrophyDetails(int achievementId)
        {
            var ach = _catalog.GetAchievement(achievementId);
            if (ach == null) return;

            var modal = new GameObject("TrophyDetailsModal", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(_safeArea, false);
            var modalRect = modal.GetComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero;
            modalRect.anchorMax = Vector2.one;
            modalRect.offsetMin = Vector2.zero;
            modalRect.offsetMax = Vector2.zero;
            modal.GetComponent<Image>().color = new Color(0, 0, 0, 0.8f);

            var box = AddResourceImage("Box", "Original/sprites/AchievementsSS4/LongDialogBox", modal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.rectTransform.sizeDelta = new Vector2(560f, 440f);
            box.preserveAspect = false;

            var unlocked = _unlockedAchievementIds.Contains(achievementId);

            var trophy = AddResourceImage("Trophy", $"Original/images/Trophy{achievementId:02d}", box.transform,
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f));
            trophy.rectTransform.sizeDelta = new Vector2(110f, 130f);
            trophy.preserveAspect = true;
            if (!unlocked)
                trophy.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);

            var statusText = unlocked ? "STATUS: UNLOCKED" : "STATUS: LOCKED";
            var statusColor = unlocked ? Grass : (Color)new Color32(230, 80, 50, 255);
            AddText(statusText, box.transform, 17, statusColor,
                new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.97f));

            var desc = unlocked ? ach.unlockedDescription : ach.lockedDescription;
            if (string.IsNullOrEmpty(desc)) desc = ach.lockedDescription;
            AddText(desc, box.transform, 16, Paper,
                new Vector2(0.06f, 0.38f), new Vector2(0.94f, 0.54f));

            var rewardText = "";
            if (ach.goldReward > 0) rewardText += $"+{ach.goldReward:N0} Cash  ";
            if (ach.entourageReward > 0) rewardText += $"Unlocks Entourage #{ach.entourageReward}  ";
            if (ach.pantsReward > 0) rewardText += $"Unlocks Pants #{ach.pantsReward}";
            if (!string.IsNullOrEmpty(rewardText))
            {
                AddText($"Reward: {rewardText}", box.transform, 14, Gold,
                    new Vector2(0.06f, 0.26f), new Vector2(0.94f, 0.36f));
            }

            AddButton("DONE", box.transform, Gold,
                new Vector2(0.5f, 0.12f), new Vector2(0.44f, 0.12f), () =>
                {
                    AudioManager.Instance.PlayTap();
                    Destroy(modal);
                });
        }

        private void ShowEntourage()
        {
            _catalog ??= GameCatalog.Load();
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader("ENTOURAGE STAFF", ShowManage);

            if (PlayerPrefs.GetInt("diceSoccer.tutorial.49", 0) == 0)
                ShowTutorialPopup(49);

            AddText("EQUIPPED STAFF (MAX 2)", _safeArea, 15, Paper,
                new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.90f));

            var slot1Name = _equippedEntourageIds.Count > 0 ? (_catalog.GetEntourage(_equippedEntourageIds[0])?.name ?? "STAFF 1") : "[EMPTY SLOT 1]";
            var slot2Name = _equippedEntourageIds.Count > 1 ? (_catalog.GetEntourage(_equippedEntourageIds[1])?.name ?? "STAFF 2") : "[EMPTY SLOT 2]";

            var s1Color = _equippedEntourageIds.Count > 0 ? Grass : (Color)new Color32(60, 65, 70, 255);
            AddButton(slot1Name, _safeArea, s1Color,
                new Vector2(0.28f, 0.81f), new Vector2(0.46f, 0.055f), () =>
                {
                    if (_equippedEntourageIds.Count > 0)
                    {
                        _equippedEntourageIds.RemoveAt(0);
                        SaveCareerState();
                        AudioManager.Instance.PlayTap();
                        ShowEntourage();
                    }
                });

            var s2Color = _equippedEntourageIds.Count > 1 ? Grass : (Color)new Color32(60, 65, 70, 255);
            AddButton(slot2Name, _safeArea, s2Color,
                new Vector2(0.72f, 0.81f), new Vector2(0.46f, 0.055f), () =>
                {
                    if (_equippedEntourageIds.Count > 1)
                    {
                        _equippedEntourageIds.RemoveAt(1);
                        SaveCareerState();
                        AudioManager.Instance.PlayTap();
                        ShowEntourage();
                    }
                });

            var staffList = _catalog.Data.entourage ?? Array.Empty<EntourageData>();
            var content = CreateScrollList("EntourageList", _safeArea,
                new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.76f), staffList.Length * 115f);

            for (var i = 0; i < staffList.Length; i++)
            {
                var ent = staffList[i];
                var yMin = 1f - (i + 1) / (float)staffList.Length;
                var yMax = 1f - i / (float)staffList.Length;

                var card = AddResourceImage($"EntCard_{ent.id}", "Original/sprites/ShopSS/ShopBox", content,
                    new Vector2(0.02f, yMin + 0.005f), new Vector2(0.98f, yMax - 0.005f));
                card.preserveAspect = false;

                var figPath = GetEntourageFigurePath(ent.id);
                var fig = AddResourceImage("Figure", figPath, card.transform,
                    new Vector2(0.02f, 0.08f), new Vector2(0.22f, 0.92f));
                fig.preserveAspect = true;

                var roleTag = ent.position == "OFF" ? "[OFFICIAL]" : ent.position == "WAG" ? "[WAG]" : "[SPECIALIST]";
                var tagColor = ent.position == "OFF" ? new Color32(0, 100, 255, 255) : ent.position == "WAG" ? new Color32(255, 50, 150, 255) : Gold;

                AddText($"{roleTag} {ent.name.ToUpperInvariant()}", card.transform, 14, tagColor,
                    new Vector2(0.24f, 0.65f), new Vector2(0.72f, 0.95f));

                AddText($"{ent.abilityName}: {ent.inGameDescription}", card.transform, 12, Paper,
                    new Vector2(0.24f, 0.15f), new Vector2(0.72f, 0.65f));

                var isEquipped = _equippedEntourageIds.Contains(ent.id);
                var isHired = _unlockedEntourageIds.Contains(ent.id);
                var capturedEnt = ent;

                if (isEquipped)
                {
                    AddButton("EQUIPPED", card.transform, Grass,
                        new Vector2(0.85f, 0.5f), new Vector2(0.24f, 0.5f), () =>
                        {
                            _equippedEntourageIds.Remove(capturedEnt.id);
                            SaveCareerState();
                            AudioManager.Instance.PlayTap();
                            ShowEntourage();
                        });
                }
                else if (isHired)
                {
                    var canEquip = _equippedEntourageIds.Count < 2;
                    AddButton(canEquip ? "EQUIP" : "FULL", card.transform, canEquip ? Gold : new Color32(60, 65, 70, 255),
                        new Vector2(0.85f, 0.5f), new Vector2(0.24f, 0.5f), () =>
                        {
                            if (_equippedEntourageIds.Count < 2)
                            {
                                _equippedEntourageIds.Add(capturedEnt.id);
                                SaveCareerState();
                                AudioManager.Instance.PlayTap();
                                ShowEntourage();
                            }
                        });
                }
                else
                {
                    var canAfford = _careerCash >= ent.cost;
                    AddButton(canAfford ? $"{ent.cost:N0}" : $"NEED {ent.cost:N0}", card.transform, canAfford ? Grass : new Color32(60, 65, 70, 255),
                        new Vector2(0.85f, 0.5f), new Vector2(0.24f, 0.5f), () =>
                        {
                            if (_careerCash >= capturedEnt.cost)
                            {
                                _careerCash -= capturedEnt.cost;
                                _unlockedEntourageIds.Add(capturedEnt.id);
                                SaveCareerState();
                                AudioManager.Instance.PlayBuy();
                                ShowEntourage();
                            }
                        });
                }
            }

            AddButton("BACK TO PLAYERS", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.045f), new Vector2(0.6f, 0.055f), ShowManage);
        }

        private static string GetEntourageFigurePath(int id)
        {
            if (id == 1 || id == 7 || id == 15)
                return $"Original/sprites/EntourageFullFigure2/EntourageFullFigure{id:02d}";
            return $"Original/sprites/EntourageFullFigure1/EntourageFullFigure{id:02d}";
        }

        private void PlayEncounter()
        {
            if (_match == null || !_match.CanBeginEncounter || !_encounterButton.interactable || _inEncounterAnimation)
                return;

            _shakeArmed = false;
            _shakeQuietSince = -1f;
            _shakeRearmAfter = Time.unscaledTime + ShakeCooldownSeconds;
            _match.BeginEncounter();
            AudioManager.Instance.PlayDiceRoll();
            _encounterButton.interactable = false;
            _substitutionButton.interactable = false;
            if (_backButton != null) _backButton.interactable = false;
            var attackingSide = _match.Possession;
            StartCoroutine(AnimateEncounter(
                _match.LastHomeFaces, _match.LastAwayFaces, attackingSide));
        }

        private IEnumerator AnimateEncounter(int[] homeFaces, int[] awayFaces, int attackingSide)
        {
            _inEncounterAnimation = true;
            _matchReport.text = "Rolling the dice...";

            // 8 frames of dice shuffling and player bobbing (~0.48s)
            for (var frame = 0; frame < 8; frame++)
            {
                for (var die = 0; die < _homeDice.Length; die++)
                {
                    if (die < homeFaces.Length)
                        SetDieFace(_homeDice[die], StandardDiceFaces[UnityEngine.Random.Range(0, StandardDiceFaces.Length)]);
                }
                for (var die = 0; die < _awayDice.Length; die++)
                {
                    if (die < awayFaces.Length)
                        SetDieFace(_awayDice[die], StandardDiceFaces[UnityEngine.Random.Range(0, StandardDiceFaces.Length)]);
                }
                var runAmount = Mathf.Sin((frame + 1) / 8f * Mathf.PI);
                AnimateAttackingPlayers(attackingSide, runAmount);
                yield return new WaitForSeconds(0.06f);
            }

            AnimateAttackingPlayers(attackingSide, 0f);
            ShowFinalFaces(_homeDice, homeFaces);
            ShowFinalFaces(_awayDice, awayFaces);

            var oldHomeScore = _match.HomeScore;
            var oldAwayScore = _match.AwayScore;
            var oldLocation = _match.Location;

            var report = _match.CompleteEncounter();
            report = CompactMatchReport(report, _match.ClockText());
            _matchScore.text = $"{_match.HomeScore}   -   {_match.AwayScore}";
            _matchClock.text = _match.ClockText();
            _matchReport.text = report;

            var scored = _match.HomeScore > oldHomeScore || _match.AwayScore > oldAwayScore || report.Contains("GOAL");
            if (scored)
            {
                yield return StartCoroutine(PlayGoalAnimation(_match.HomeScore > oldHomeScore));
            }
            else if (report.Contains("SAVED"))
            {
                yield return StartCoroutine(PlaySaveAnimation());
            }
            else if (report.Contains("TIE") || report.Contains("Tie") || report.Contains("Win on Tie"))
            {
                yield return StartCoroutine(PlayTieAnimation());
            }
            else if (report.Contains("Through Pass"))
            {
                yield return StartCoroutine(PlayThroughPassAnimation(_match.Possession == 0));
            }
            else if (report.Contains("Tackled") || report.Contains("Dispossessed") || report.Contains("battle"))
            {
                AudioManager.Instance.PlayTap();
            }
            else
            {
                AudioManager.Instance.PlayDisappoint();
            }

            // Smoothly interpolate field and soccer ball
            yield return StartCoroutine(SmoothShiftFieldAndBall(oldLocation, _match.Location, _match.Possession));
            UpdatePlayerPositions();

            if (_match.IsFinished)
            {
                AudioManager.Instance.PlayTripleWhistle();
                if (!_currentResultRecorded)
                {
                    RecordFixtureResult();
                    _currentResultRecorded = true;
                }
                ShowGameOverModal();
            }
            else
            {
                _matchReport.text = GetEncounterPreviewText();
                _encounterButton.interactable = true;
                _substitutionButton.interactable = _ownedPlayerIds.Count > 7;
                if (_backButton != null) _backButton.interactable = true;
            }

            _inEncounterAnimation = false;
        }

        private IEnumerator PlayKickOffAnimation()
        {
            if (_overlayContainer == null) yield break;
            if (_encounterButton != null) _encounterButton.interactable = false;
            if (_substitutionButton != null) _substitutionButton.interactable = false;

            var banner = AddResourceImage("KickoffBanner",
                "Original/sprites/Prompt2/Kickoff", _overlayContainer,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
            banner.rectTransform.sizeDelta = new Vector2(460f, 300f);
            banner.preserveAspect = true;
            AudioManager.Instance.PlayWhistle();

            var elapsed = 0f;
            while (elapsed < 0.3f)
            {
                elapsed += Time.deltaTime;
                banner.rectTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 1.08f, elapsed / 0.3f);
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < 0.15f)
            {
                elapsed += Time.deltaTime;
                banner.rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.08f, Vector3.one, elapsed / 0.15f);
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);

            elapsed = 0f;
            var col = banner.color;
            while (elapsed < 0.4f)
            {
                elapsed += Time.deltaTime;
                banner.color = new Color(col.r, col.g, col.b, 1f - elapsed / 0.4f);
                yield return null;
            }
            if (banner != null) Destroy(banner.gameObject);

            if (_encounterButton != null) _encounterButton.interactable = !_match.IsFinished;
            if (_substitutionButton != null) _substitutionButton.interactable = !_match.IsFinished && _ownedPlayerIds.Count > 7;
        }

        private IEnumerator PlayGoalAnimation(bool homeScored)
        {
            if (_overlayContainer == null) yield break;
            AudioManager.Instance.PlayGoal();
            AudioManager.Instance.PlayHighlightReel();
            AudioManager.Instance.PlayWhistle();

            // Rotating sunburst background rays (GoalRotation.png from Cocos2d runGoalAnimation)
            var sunburst = AddResourceImage("GoalSunburst",
                "Original/sprites/GoalAnimationSS4/GoalRotation", _overlayContainer,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
            sunburst.rectTransform.sizeDelta = new Vector2(520f, 520f);
            sunburst.preserveAspect = true;
            sunburst.raycastTarget = false;

            var banner = AddResourceImage("GoalBanner",
                "Original/sprites/Prompt1/Goal", _overlayContainer,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
            banner.rectTransform.sizeDelta = new Vector2(480f, 320f);
            banner.preserveAspect = true;

            var confettiList = new List<Image>();
            for (var i = 0; i < 20; i++)
            {
                var cId = UnityEngine.Random.Range(1, 4);
                var x = UnityEngine.Random.Range(0.1f, 0.9f);
                var y = UnityEngine.Random.Range(0.65f, 0.95f);
                var cImg = AddResourceImage($"Confetti{i}",
                    $"Original/sprites/ConfettiSS/Confetti{cId}", _overlayContainer,
                    new Vector2(x, y), new Vector2(x, y));
                cImg.rectTransform.sizeDelta = new Vector2(24f, 24f);
                cImg.rectTransform.localEulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                confettiList.Add(cImg);
            }

            var runnerChoice = UnityEngine.Random.Range(0, 3);
            Image runnerImg = null;
            if (runnerChoice == 1)
            {
                runnerImg = AddResourceImage("PenguinRunner",
                    "Original/sprites/GoalAnimationSS4/PenguinBody", _overlayContainer,
                    new Vector2(0f, 0.36f), new Vector2(0f, 0.36f));
                runnerImg.rectTransform.sizeDelta = new Vector2(90f, 90f);
            }
            else if (runnerChoice == 2)
            {
                runnerImg = AddResourceImage("StreakerRunner",
                    "Original/sprites/GoalAnimationSS4/Streaker01", _overlayContainer,
                    new Vector2(0f, 0.36f), new Vector2(0f, 0.36f));
                runnerImg.rectTransform.sizeDelta = new Vector2(80f, 110f);
            }
            else
            {
                var figure = homeScored ? (_lineupIds.Length > 5 ? _lineupIds[5] : 1) : 1;
                figure = Mathf.Clamp(figure, 1, 96);
                runnerImg = AddResourceImage("ScorerRunner",
                    $"Original/sprites/PlayerFullFigureSS/PlayerFullFigure{figure:000}", _overlayContainer,
                    new Vector2(0f, 0.36f), new Vector2(0f, 0.36f));
                runnerImg.rectTransform.sizeDelta = new Vector2(80f, 130f);
            }
            if (runnerImg != null) runnerImg.preserveAspect = true;

            var elapsed = 0f;
            const float totalDuration = 1.6f;
            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / totalDuration);

                if (sunburst != null)
                    sunburst.rectTransform.Rotate(0f, 0f, 75f * Time.deltaTime);

                if (progress < 0.25f)
                    banner.rectTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 1.15f, progress / 0.25f);
                else if (progress < 0.40f)
                    banner.rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.15f, Vector3.one, (progress - 0.25f) / 0.15f);

                if (runnerImg != null)
                {
                    var runX = Mathf.Lerp(0.02f, 0.98f, progress);
                    var bob = Mathf.Abs(Mathf.Sin(elapsed * 12f)) * 0.035f;
                    runnerImg.rectTransform.anchorMin = runnerImg.rectTransform.anchorMax = new Vector2(runX, 0.36f + bob);
                    if (runnerChoice == 2)
                    {
                        var frame = Mathf.Clamp((int)(progress * 12f) + 1, 1, 12);
                        runnerImg.sprite = GetUiSprite($"Original/sprites/GoalAnimationSS4/Streaker{frame:00}");
                    }
                }

                for (var i = 0; i < confettiList.Count; i++)
                {
                    var c = confettiList[i];
                    if (c != null)
                    {
                        var p = c.rectTransform.anchorMin;
                        c.rectTransform.anchorMin = c.rectTransform.anchorMax = new Vector2(p.x, p.y - Time.deltaTime * 0.22f);
                        c.rectTransform.Rotate(0f, 0f, 150f * Time.deltaTime);
                    }
                }
                yield return null;
            }

            if (runnerImg != null) Destroy(runnerImg.gameObject);
            foreach (var c in confettiList) if (c != null) Destroy(c.gameObject);
            if (banner != null) Destroy(banner.gameObject);
            if (sunburst != null) Destroy(sunburst.gameObject);
        }

        private IEnumerator PlaySaveAnimation()
        {
            if (_overlayContainer == null) yield break;
            AudioManager.Instance.PlayDisappoint();
            var banner = AddResourceImage("SaveBanner",
                "Original/sprites/Prompt1/Save", _overlayContainer,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
            banner.rectTransform.sizeDelta = new Vector2(440f, 280f);
            banner.preserveAspect = true;

            var elapsed = 0f;
            while (elapsed < 0.35f)
            {
                elapsed += Time.deltaTime;
                banner.rectTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, elapsed / 0.35f);
                yield return null;
            }
            yield return new WaitForSeconds(0.45f);
            elapsed = 0f;
            var col = banner.color;
            while (elapsed < 0.35f)
            {
                elapsed += Time.deltaTime;
                banner.color = new Color(col.r, col.g, col.b, 1f - elapsed / 0.35f);
                yield return null;
            }
            if (banner != null) Destroy(banner.gameObject);
        }

        private IEnumerator PlayTieAnimation()
        {
            if (_overlayContainer == null) yield break;
            AudioManager.Instance.PlayCrowdApplause();
            AudioManager.Instance.PlayWhistle();
            var banner = AddResourceImage("TieBanner",
                "Original/sprites/Prompt1/Tie", _overlayContainer,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
            banner.rectTransform.sizeDelta = new Vector2(440f, 280f);
            banner.preserveAspect = true;

            var elapsed = 0f;
            while (elapsed < 0.35f)
            {
                elapsed += Time.deltaTime;
                banner.rectTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, elapsed / 0.35f);
                yield return null;
            }
            yield return new WaitForSeconds(0.45f);
            elapsed = 0f;
            var col = banner.color;
            while (elapsed < 0.3f)
            {
                elapsed += Time.deltaTime;
                banner.color = new Color(col.r, col.g, col.b, 1f - elapsed / 0.3f);
                yield return null;
            }
            if (banner != null) Destroy(banner.gameObject);
        }

        private IEnumerator PlayThroughPassAnimation(bool home)
        {
            if (_overlayContainer == null) yield break;
            AudioManager.Instance.PlayWhistle();
            var path = home
                ? "Original/sprites/BuffTrigger1/ThroughPassGreen"
                : "Original/sprites/BuffTrigger1/ThroughPassRed";
            var banner = AddResourceImage("ThroughPassBanner", path, _overlayContainer,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
            banner.rectTransform.sizeDelta = new Vector2(420f, 220f);
            banner.preserveAspect = true;

            var elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                var x = Mathf.Lerp(home ? 0.35f : 0.65f, 0.5f, elapsed / 0.5f);
                banner.rectTransform.anchorMin = banner.rectTransform.anchorMax = new Vector2(x, 0.55f);
                yield return null;
            }
            yield return new WaitForSeconds(0.35f);
            if (banner != null) Destroy(banner.gameObject);
        }

        private IEnumerator PlayPoofAnimation(int slot)
        {
            if (_homePlayers == null || slot < 0 || slot >= _homePlayers.Length) yield break;
            var target = _homePlayers[slot];
            var poofObj = new GameObject("PoofEffect", typeof(RectTransform), typeof(Image));
            poofObj.transform.SetParent(_field, false);
            var rect = poofObj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = target.rectTransform.anchorMin;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(100f, 100f);
            var img = poofObj.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;

            for (var f = 1; f <= 13; f++)
            {
                img.sprite = GetUiSprite($"Original/sprites/PlayerBody3/Poof{f:00}");
                yield return new WaitForSeconds(0.05f);
            }
            Destroy(poofObj);
        }

        private void ShowGameOverModal()
        {
            if (_overlayContainer == null) return;
            var won = _match.HomeScore > _match.AwayScore;
            var drew = _match.HomeScore == _match.AwayScore;

            if (won)
            {
                AudioManager.Instance.PlayMedalCeremonyLong();
                AudioManager.Instance.PlayEffect("KidsCheering", 1f, 1f);
            }
            else if (drew)
            {
                AudioManager.Instance.PlayEffect("ChildrenAaaah", 1f, 0.9f);
            }
            else
            {
                AudioManager.Instance.PlayKidsBooing();
            }

            if (_lastPayout > 0)
                AudioManager.Instance.PlayCash();

            var backdrop = AddPanel("GameOverBackdrop", _overlayContainer,
                new Color(0f, 0f, 0f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.one);
            backdrop.raycastTarget = true;

            var boxPath = won
                ? "Original/sprites/GameOver/GameOverVictoryBox"
                : drew ? "Original/sprites/GameOver/GameOverDrawBox" : "Original/sprites/GameOver/GameOverDefeatBox";
            var box = AddResourceImage("GameOverBox", boxPath, backdrop.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(390f, 560f);
            box.preserveAspect = true;

            // Authentic Cocos2d scale pop-in bounce animation
            StartCoroutine(PopInBounceAnimation(box.rectTransform));

            var titlePath = won
                ? "Original/sprites/GameOver/GameOverVictory"
                : drew ? "Original/sprites/GameOver/GameOverDraw" : "Original/sprites/GameOver/GameOverDefeat";
            var title = AddResourceImage("GameOverTitle", titlePath, box.transform,
                new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.88f));
            title.rectTransform.sizeDelta = new Vector2(300f, 75f);
            title.preserveAspect = true;

            AddText($"{_match.HomeScore}   -   {_match.AwayScore}", box.transform, 34, Paper,
                new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.78f));

            var stars = _match.CalculateStars();
            var starIndex = Mathf.Clamp(stars, 0, 3);
            var starImg = AddResourceImage("StarRating",
                $"Original/sprites/GameOver/Gold{starIndex}Star", box.transform,
                new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.53f));
            starImg.rectTransform.sizeDelta = new Vector2(260f, 105f);
            starImg.preserveAspect = true;

            AddText($"+{_lastPayout:N0} CASH", box.transform, 22, Gold,
                new Vector2(0.1f, 0.34f), new Vector2(0.9f, 0.44f));

            // Victory confetti shower:
            if (won)
            {
                for (var i = 0; i < 24; i++)
                {
                    var cId = UnityEngine.Random.Range(1, 4);
                    var x = UnityEngine.Random.Range(0.05f, 0.95f);
                    var y = UnityEngine.Random.Range(0.40f, 0.95f);
                    var cImg = AddResourceImage($"VictoryConfetti{i}",
                        $"Original/sprites/ConfettiSS/Confetti{cId}", backdrop.transform,
                        new Vector2(x, y), new Vector2(x, y));
                    cImg.rectTransform.sizeDelta = new Vector2(22f, 22f);
                    cImg.rectTransform.localEulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                    cImg.raycastTarget = false;
                }
            }

            AddSpriteButton("Continue", "Original/images/ContinueButton",
                "Original/images/ContinueButtonPressed", box.transform,
                new Vector2(0.5f, 0.16f), new Vector2(0.55f, 0.11f),
                () => {
                    // Check Jerry Hattrick post-match tutorial after Season 0 match 0
                    if (_seasonIndex == 0 && _selectedFixtureIndex == 0 && PlayerPrefs.GetInt("diceSoccer.tutorial.21", 0) == 0)
                    {
                        ShowTutorialPopup(21, ShowLeagueFixtures);
                    }
                    else
                    {
                        ShowLeagueFixtures();
                    }
                });
        }

        private IEnumerator PopInBounceAnimation(RectTransform targetRect)
        {
            var elapsed = 0f;
            const float scaleUpTime = 0.22f;
            while (elapsed < scaleUpTime)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / scaleUpTime);
                targetRect.localScale = Vector3.Lerp(Vector3.one * 0.4f, Vector3.one * 1.15f, t);
                yield return null;
            }
            elapsed = 0f;
            const float settleTime = 0.12f;
            while (elapsed < settleTime)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / settleTime);
                targetRect.localScale = Vector3.Lerp(Vector3.one * 1.15f, Vector3.one, t);
                yield return null;
            }
            targetRect.localScale = Vector3.one;
        }

        private void ShowPauseMenu()
        {
            if (_overlayContainer == null || _match == null || _match.IsFinished) return;
            var backdrop = AddPanel("PauseBackdrop", _overlayContainer,
                new Color(0f, 0f, 0f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.one);
            backdrop.raycastTarget = true;

            var box = AddResourceImage("PauseBox", "Original/images/LongDialogBox", backdrop.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(360f, 480f);
            box.preserveAspect = true;

            // Paused header banner:
            var header = AddResourceImage("PausedHeader", "Original/images/Paused", box.transform,
                new Vector2(0.5f, 0.87f), new Vector2(0.5f, 0.87f));
            header.rectTransform.sizeDelta = new Vector2(240f, 55f);
            header.preserveAspect = true;

            // 1. Resume Button:
            AddSpriteButton("Resume", "Original/images/ResumeButton",
                "Original/images/ResumeButtonPressed", box.transform,
                new Vector2(0.5f, 0.72f), new Vector2(0.65f, 0.12f),
                () => Destroy(backdrop.gameObject));

            // 2. Substitution Button + Count Badge:
            var subBtn = AddSpriteButton("Substitution", "Original/images/SubstitutionButton",
                "Original/images/SubstitutionButtonPressed", box.transform,
                new Vector2(0.5f, 0.56f), new Vector2(0.65f, 0.12f),
                () => {
                    if (_remainingSubs > 0 && _ownedPlayerIds.Count > 7)
                    {
                        Destroy(backdrop.gameObject);
                        ShowSubstitutionSlots();
                    }
                });
            if (_remainingSubs <= 0 || _ownedPlayerIds.Count <= 7)
                subBtn.interactable = false;

            var subBadge = AddResourceImage("SubCountBG", "Original/images/SubstitutionCountBG", box.transform,
                new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f));
            subBadge.rectTransform.sizeDelta = new Vector2(160f, 30f);
            subBadge.preserveAspect = true;

            AddText($"{_remainingSubs} SUBS LEFT", subBadge.transform, 11, Paper,
                new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f));

            // 3. Pause Options Button:
            AddSpriteButton("PauseOptions", "Original/images/PauseOptionsButton",
                "Original/images/PauseOptionsButtonPressed", box.transform,
                new Vector2(0.5f, 0.36f), new Vector2(0.65f, 0.12f),
                () => ShowOptionsDialog(backdrop.transform));

            // 4. Forfeit Button:
            AddSpriteButton("Forfeit", "Original/images/ForfeitButton",
                "Original/images/ForfeitButtonPressed", box.transform,
                new Vector2(0.5f, 0.20f), new Vector2(0.65f, 0.12f),
                () => ShowForfeitConfirmation(backdrop.transform));
        }

        private void ShowForfeitConfirmation(Transform parent)
        {
            var forfeitModal = AddPanel("ForfeitModal", parent,
                new Color(0f, 0f, 0f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.one);
            forfeitModal.raycastTarget = true;

            var box = AddResourceImage("ForfeitBox", "Original/images/DialogBox", forfeitModal.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(340f, 260f);
            box.preserveAspect = true;

            var prompt = AddText("ARE YOU SURE YOU WANT TO FORFEIT?", box.transform, 15, Ink,
                new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.86f));
            prompt.alignment = TextAnchor.MiddleCenter;

            // YES button
            AddSpriteButton("YesButton", "Original/images/YesButton",
                "Original/images/YesButtonPressed", box.transform,
                new Vector2(0.28f, 0.22f), new Vector2(0.38f, 0.22f), () =>
                {
                    Destroy(parent.gameObject);
                    _match.ForfeitMatch();
                    RecordFixtureResult();
                    ShowGameOverModal();
                });

            // NO button
            AddSpriteButton("NoButton", "Original/images/NoButton",
                "Original/images/NoButtonPressed", box.transform,
                new Vector2(0.72f, 0.22f), new Vector2(0.38f, 0.22f), () =>
                {
                    Destroy(forfeitModal.gameObject);
                });
        }

        private void ShowOptionsDialog(Transform parent)
        {
            var optionsBackdrop = AddPanel("OptionsBackdrop", parent,
                new Color(0f, 0f, 0f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.one);
            optionsBackdrop.raycastTarget = true;

            var box = AddResourceImage("OptionsBox", "Original/images/LongDialogBox", optionsBackdrop.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(360f, 440f);
            box.preserveAspect = true;

            var title = AddText("OPTIONS", box.transform, 22, Ink,
                new Vector2(0.1f, 0.84f), new Vector2(0.9f, 0.94f));
            title.fontStyle = FontStyle.Bold;

            // Music & Sound Checkbox
            var tickBox = AddResourceImage("SoundTickBox", "Original/sprites/OptionsSS/OptionsTickBox", box.transform,
                new Vector2(0.16f, 0.68f), new Vector2(0.16f, 0.68f));
            tickBox.rectTransform.sizeDelta = new Vector2(32f, 32f);
            tickBox.preserveAspect = true;

            var tickImg = AddResourceImage("SoundTick", "Original/sprites/OptionsSS/OptionsTick", tickBox.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            tickImg.rectTransform.sizeDelta = new Vector2(28f, 28f);
            tickImg.preserveAspect = true;
            tickImg.gameObject.SetActive(_soundEnabled);

            var soundLabel = AddText("SOUND & MUSIC", box.transform, 16, Ink,
                new Vector2(0.28f, 0.64f), new Vector2(0.85f, 0.72f));
            soundLabel.alignment = TextAnchor.MiddleLeft;

            var soundBtn = tickBox.gameObject.AddComponent<Button>();
            soundBtn.onClick.AddListener(() =>
            {
                ToggleSound();
                tickImg.gameObject.SetActive(_soundEnabled);
            });

            // Handbook button
            AddSpriteButton("Handbook", "Original/sprites/OptionsSS/HandbookButton",
                "Original/sprites/OptionsSS/HandbookButtonPressed", box.transform,
                new Vector2(0.5f, 0.50f), new Vector2(0.60f, 0.12f), () =>
                {
                    ShowHandbookDialog(optionsBackdrop.transform);
                });

            // Credits button
            AddSpriteButton("Credits", "Original/sprites/OptionsSS/CreditsButton",
                "Original/sprites/OptionsSS/CreditsButtonPressed", box.transform,
                new Vector2(0.5f, 0.35f), new Vector2(0.60f, 0.12f), () =>
                {
                    ShowCreditsDialog(optionsBackdrop.transform);
                });

            // Done button
            AddSpriteButton("Done", "Original/images/DoneButton",
                "Original/images/DoneButtonPressed", box.transform,
                new Vector2(0.5f, 0.14f), new Vector2(0.55f, 0.10f), () =>
                {
                    Destroy(optionsBackdrop.gameObject);
                });
        }

        private void ShowHandbookDialog(Transform parent)
        {
            var hbBackdrop = AddPanel("HandbookBackdrop", parent,
                new Color(0f, 0f, 0f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.one);
            hbBackdrop.raycastTarget = true;

            var box = AddResourceImage("HandbookBox", "Original/images/LongDialogBox", hbBackdrop.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(360f, 460f);
            box.preserveAspect = true;

            var title = AddText("HANDBOOK", box.transform, 20, Ink,
                new Vector2(0.1f, 0.85f), new Vector2(0.9f, 0.93f));
            title.fontStyle = FontStyle.Bold;

            var body = AddText(
                "DICE SOCCER RULES:\n\n" +
                "• Shake or tap to roll your team's dice.\n" +
                "• Highest total moves the ball forward toward the goal.\n" +
                "• CF in opposing penalty box shoots on goal!\n" +
                "• Match superpowers and dice triggers to turn the tide!\n" +
                "• Hire new talent and equip Entourage staff for permanent buffs.",
                box.transform, 13, Ink,
                new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.82f));
            body.alignment = TextAnchor.UpperLeft;

            AddSpriteButton("CloseHandbook", "Original/images/DoneButton",
                "Original/images/DoneButtonPressed", box.transform,
                new Vector2(0.5f, 0.11f), new Vector2(0.55f, 0.10f), () =>
                {
                    Destroy(hbBackdrop.gameObject);
                });
        }

        private void ShowCreditsDialog(Transform parent)
        {
            var crBackdrop = AddPanel("CreditsBackdrop", parent,
                new Color(0f, 0f, 0f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.one);
            crBackdrop.raycastTarget = true;

            var box = AddResourceImage("CreditsBox", "Original/images/LongDialogBox", crBackdrop.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(360f, 460f);
            box.preserveAspect = true;

            var title = AddText("CREDITS", box.transform, 20, Ink,
                new Vector2(0.1f, 0.85f), new Vector2(0.9f, 0.93f));
            title.fontStyle = FontStyle.Bold;

            var body = AddText(
                "DICE SOCCER (2012)\n\n" +
                "Original Game Design & Art:\n" +
                "Red Robot Labs & Foursaken Media\n\n" +
                "Sound & Music:\n" +
                "Original 2012 Audio Archive\n\n" +
                "100% Faithful Unity Remake\n" +
                "Preserving all ARMv7 rules,\n" +
                "assets, animations & typography.",
                box.transform, 13, Ink,
                new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.82f));
            body.alignment = TextAnchor.MiddleCenter;

            AddSpriteButton("CloseCredits", "Original/images/DoneButton",
                "Original/images/DoneButtonPressed", box.transform,
                new Vector2(0.5f, 0.11f), new Vector2(0.55f, 0.10f), () =>
                {
                    Destroy(crBackdrop.gameObject);
                });
        }

        private void ShowTutorialPopup(int tutorialId, Action onDone = null, bool force = false)
        {
            var prefKey = $"diceSoccer.tutorial.{tutorialId}";
            if (!force && PlayerPrefs.GetInt(prefKey, 0) != 0)
            {
                onDone?.Invoke();
                return;
            }

            var tut = _catalog.GetTutorial(tutorialId);
            if (tut == null || tut.popupText == null || tut.popupText.Length == 0)
            {
                onDone?.Invoke();
                return;
            }

            var currentTextIndex = 0;
            GameObject backdropGo = null;

            Action renderPage = null;
            renderPage = () =>
            {
                if (backdropGo != null) Destroy(backdropGo);

                backdropGo = new GameObject("TutorialBackdrop", typeof(RectTransform), typeof(Image));
                backdropGo.transform.SetParent(_safeArea, false);
                var bdRect = backdropGo.GetComponent<RectTransform>();
                bdRect.anchorMin = Vector2.zero;
                bdRect.anchorMax = Vector2.one;
                bdRect.offsetMin = bdRect.offsetMax = Vector2.zero;
                var bdImg = backdropGo.GetComponent<Image>();
                bdImg.color = new Color(0f, 0f, 0f, 0.78f);
                bdImg.raycastTarget = true;

                var box = AddResourceImage("TutorialBox", "Original/images/LongDialogBox", backdropGo.transform,
                    new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
                box.rectTransform.sizeDelta = new Vector2(360f, 440f);
                box.preserveAspect = true;

                // Speaker portrait: KidHead or Coach
                var headImg = AddResourceImage("SpeakerHead", "Original/sprites/AchievementsSS4/KidHead", box.transform,
                    new Vector2(0.20f, 0.85f), new Vector2(0.20f, 0.85f));
                headImg.rectTransform.sizeDelta = new Vector2(64f, 64f);
                headImg.preserveAspect = true;

                var title = AddText("COACH HATTRICK", box.transform, 17, Ink,
                    new Vector2(0.35f, 0.82f), new Vector2(0.92f, 0.89f));
                title.alignment = TextAnchor.MiddleLeft;
                title.fontStyle = FontStyle.Bold;

                // Optional custom illustration in center
                if (tut.customPopup != null && tut.customPopup.Length > 0)
                {
                    var cpId = tut.customPopup[Mathf.Min(currentTextIndex, tut.customPopup.Length - 1)];
                    var cpPath = (cpId == 4 || cpId == 7)
                        ? $"Original/sprites/CustomPopup2/Custom{cpId}"
                        : $"Original/sprites/CustomPopup1/Custom{cpId}";
                    var cpImg = AddResourceImage("CustomIllustration", cpPath, box.transform,
                        new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f));
                    cpImg.rectTransform.sizeDelta = new Vector2(240f, 160f);
                    cpImg.preserveAspect = true;
                }

                // Text
                var textContent = tut.popupText[currentTextIndex];
                var textYMin = (tut.customPopup != null && tut.customPopup.Length > 0) ? 0.20f : 0.25f;
                var textYMax = (tut.customPopup != null && tut.customPopup.Length > 0) ? 0.38f : 0.75f;
                var promptText = AddText(textContent, box.transform, 14, Ink,
                    new Vector2(0.08f, textYMin), new Vector2(0.92f, textYMax));
                promptText.alignment = TextAnchor.MiddleCenter;

                // Button: NEXT or DONE
                var isLast = currentTextIndex >= tut.popupText.Length - 1;
                AddSpriteButton("TutorialDone", "Original/images/DoneButton",
                    "Original/images/DoneButtonPressed", box.transform,
                    new Vector2(0.5f, 0.10f), new Vector2(0.55f, 0.09f), () =>
                    {
                        AudioManager.Instance.PlayTap();
                        if (isLast)
                        {
                            PlayerPrefs.SetInt(prefKey, 1);
                            PlayerPrefs.Save();
                            Destroy(backdropGo);
                            onDone?.Invoke();
                        }
                        else
                        {
                            currentTextIndex++;
                            renderPage();
                        }
                    });
            };

            renderPage();
        }

        private static string CompactMatchReport(string report, string clock)
        {
            var clockSuffix = $"  {clock}";
            if (report.EndsWith(clockSuffix, StringComparison.Ordinal))
                report = report.Substring(0, report.Length - clockSuffix.Length);

            if (report.IndexOf(" vs ", StringComparison.Ordinal) >= 0)
            {
                var resultStart = report.IndexOf(". ", StringComparison.Ordinal);
                if (resultStart >= 0)
                    report = report.Substring(resultStart + 2);
            }

            return report;
        }

        private int _lastPayout;

        private void RecordFixtureResult()
        {
            var won = _match.HomeScore > _match.AwayScore;
            var drew = _match.HomeScore == _match.AwayScore;
            _lastPayout = _competitionName == "League"
                ? won ? _currentFixture.cashWin
                    : drew ? _currentFixture.cashDraw
                    : _currentFixture.cashLose
                : 0;

            // Check Entourage bonuses:
            if (_equippedEntourageIds.Contains(11) && won) // Sheikh Abdullah: +10,000 cash for victory
            {
                _careerCash += 10000;
                _lastPayout += 10000;
            }
            if (_equippedEntourageIds.Contains(12) && drew) // Ronin Chelskovic: Draw awards win money
            {
                var extra = _currentFixture.cashWin - _currentFixture.cashDraw;
                _careerCash += extra;
                _lastPayout += extra;
            }
            if (_equippedEntourageIds.Contains(13)) // Sylvester Punter: +15k on win, -15k on loss
            {
                if (won) { _careerCash += 15000; _lastPayout += 15000; }
                else if (!drew) { _careerCash = Mathf.Max(0, _careerCash - 15000); _lastPayout -= 15000; }
            }
            if (_equippedEntourageIds.Contains(14)) // Hilly Will: +50k on win, -50k on loss
            {
                if (won) { _careerCash += 50000; _lastPayout += 50000; }
                else if (!drew) { _careerCash = Mathf.Max(0, _careerCash - 50000); _lastPayout -= 50000; }
            }

            _careerCash += _lastPayout;
            if (_competitionName == "League")
            {
                if (_selectedFixtureIndex == _fixtureIndex)
                    _fixtureIndex++;
                PlayerPrefs.SetInt("diceSoccer.fixture", _fixtureIndex);
            }
            else
            {
                PlayerPrefs.SetInt(TournamentResultKey(_competitionName, _selectedFixtureIndex),
                    won ? 1 : -1);
                if (won && _selectedFixtureIndex == _fixtureIndex)
                    _fixtureIndex++;
            }
            PlayerPrefs.SetInt(CompetitionProgressKey(_competitionName), _fixtureIndex);
            SaveCareerState();
            CheckAchievements(won, drew);
        }

        private string SeasonEndButtonLabel()
        {
            if (_seasonIndex == 0 && _catalog.Data.seasons.Length > 1)
                return $"UNLOCK {_catalog.Data.seasons[1].name.ToUpperInvariant()}";
            return "BACK TO COMPETITIONS";
        }

        private void AdvanceCareer()
        {
            if (_seasonIndex != 0 || _fixtureIndex < _seasonFixtures.Length ||
                _catalog.Data.seasons.Length < 2)
            {
                ShowLeagueFixtures();
                return;
            }

            _seasonIndex = 1;
            _competitionName = "League";
            _seasonFixtures = _catalog.GetFixtures(_seasonIndex, _competitionName);
            _fixtureIndex = 0;
            PlayerPrefs.SetInt("diceSoccer.season", _seasonIndex);
            PlayerPrefs.SetInt(CompetitionProgressKey(_competitionName), _fixtureIndex);
            PlayerPrefs.SetInt("diceSoccer.fixture", _fixtureIndex);
            SaveCareerState();
            ShowHome();
        }

        private void LoadCareerProgress()
        {
            _seasonIndex = Mathf.Max(0, PlayerPrefs.GetInt("diceSoccer.season", 0));
            _fixtureIndex = Mathf.Max(0, PlayerPrefs.GetInt("diceSoccer.fixture", 0));
            _careerCash = Mathf.Max(0, PlayerPrefs.GetInt("diceSoccer.cash", 0));
            _currentShirtId = Mathf.Max(1, PlayerPrefs.GetInt("diceSoccer.currentShirt", 1));
            _currentPantsId = Mathf.Max(1, PlayerPrefs.GetInt("diceSoccer.currentPants", 1));

            _unlockedShirtIds = LoadIntSet("diceSoccer.unlockedShirts", new[] { 1 });
            _unlockedPantsIds = LoadIntSet("diceSoccer.unlockedPants", new[] { 1 });
            _unlockedAchievementIds = LoadIntSet("diceSoccer.achievements", Array.Empty<int>());
            _unlockedEntourageIds = LoadIntSet("diceSoccer.unlockedEntourage", Array.Empty<int>());
            _equippedEntourageIds = new List<int>(LoadIntSet("diceSoccer.equippedEntourage", Array.Empty<int>()));
        }

        private static HashSet<int> LoadIntSet(string key, int[] defaultValues)
        {
            var raw = PlayerPrefs.GetString(key, null);
            if (string.IsNullOrEmpty(raw))
                return new HashSet<int>(defaultValues ?? Array.Empty<int>());
            var set = new HashSet<int>();
            foreach (var part in raw.Split(','))
            {
                if (int.TryParse(part.Trim(), out var val))
                    set.Add(val);
            }
            if (set.Count == 0 && defaultValues != null)
            {
                foreach (var v in defaultValues) set.Add(v);
            }
            return set;
        }

        private void SaveCareerState()
        {
            PlayerPrefs.SetInt("diceSoccer.cash", _careerCash);
            PlayerPrefs.SetInt("diceSoccer.currentShirt", _currentShirtId);
            PlayerPrefs.SetInt("diceSoccer.currentPants", _currentPantsId);
            PlayerPrefs.SetString("diceSoccer.unlockedShirts", string.Join(",", _unlockedShirtIds));
            PlayerPrefs.SetString("diceSoccer.unlockedPants", string.Join(",", _unlockedPantsIds));
            PlayerPrefs.SetString("diceSoccer.achievements", string.Join(",", _unlockedAchievementIds));
            PlayerPrefs.SetString("diceSoccer.unlockedEntourage", string.Join(",", _unlockedEntourageIds));
            PlayerPrefs.SetString("diceSoccer.equippedEntourage", string.Join(",", _equippedEntourageIds));
            PlayerPrefs.Save();
        }

        private void CheckAchievements(bool won, bool drew)
        {
            _catalog ??= GameCatalog.Load();
            var newAchievements = new List<AchievementData>();

            // ID 1: Completed All 3 Matches in Training Season
            if (_seasonIndex == 0 && _fixtureIndex >= 3 && !_unlockedAchievementIds.Contains(1))
            {
                var ach = _catalog.GetAchievement(1);
                if (ach != null) newAchievements.Add(ach);
            }

            // ID 2: Won All 3 Matches in Training Season
            if (_seasonIndex == 0 && _fixtureIndex >= 3 && won && !_unlockedAchievementIds.Contains(2))
            {
                var ach = _catalog.GetAchievement(2);
                if (ach != null) newAchievements.Add(ach);
            }

            // ID 3: Season 1 Qualification
            if (_seasonIndex >= 1 && !_unlockedAchievementIds.Contains(3))
            {
                var ach = _catalog.GetAchievement(3);
                if (ach != null) newAchievements.Add(ach);
            }

            // ID 4: Won All Games In Season 1 League
            if (_seasonIndex == 1 && _fixtureIndex >= 5 && won && !_unlockedAchievementIds.Contains(4))
            {
                var ach = _catalog.GetAchievement(4);
                if (ach != null) newAchievements.Add(ach);
            }

            // ID 54: Clean Sheet (AwayScore == 0)
            if (won && _match != null && _match.AwayScore == 0 && !_unlockedAchievementIds.Contains(54))
            {
                var ach = _catalog.GetAchievement(54);
                if (ach != null) newAchievements.Add(ach);
            }

            // ID 47: High Score (HomeScore >= 5)
            if (won && _match != null && _match.HomeScore >= 5 && !_unlockedAchievementIds.Contains(47))
            {
                var ach = _catalog.GetAchievement(47);
                if (ach != null) newAchievements.Add(ach);
            }

            foreach (var ach in newAchievements)
            {
                _unlockedAchievementIds.Add(ach.id);
                if (ach.goldReward > 0) _careerCash += ach.goldReward;
                if (ach.entourageReward > 0) _unlockedEntourageIds.Add(ach.entourageReward);
                if (ach.pantsReward > 0) _unlockedPantsIds.Add(ach.pantsReward);
            }

            if (newAchievements.Count > 0)
            {
                SaveCareerState();
                AudioManager.Instance.PlayMedalCeremony();
                ShowAchievementUnlockedModal(newAchievements[0]);
            }
        }

        private void ShowAchievementUnlockedModal(AchievementData ach)
        {
            if (_overlayContainer == null) return;
            var modal = new GameObject("AchievementUnlockedModal", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(_overlayContainer, false);
            var modalRect = modal.GetComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero;
            modalRect.anchorMax = Vector2.one;
            modal.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);

            var box = AddResourceImage("Box", "Original/sprites/AchievementsSS4/LongDialogBox", modal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.rectTransform.sizeDelta = new Vector2(560f, 420f);
            box.preserveAspect = false;

            var trophyId = Mathf.Clamp(ach.id, 1, 72);
            var trophy = AddResourceImage("Trophy", $"Original/images/Trophy{trophyId:02d}", box.transform,
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f));
            trophy.rectTransform.sizeDelta = new Vector2(110f, 130f);
            trophy.preserveAspect = true;

            AddText("ACHIEVEMENT UNLOCKED!", box.transform, 22, Gold,
                new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.96f));

            var desc = string.IsNullOrEmpty(ach.unlockedDescription) ? ach.lockedDescription : ach.unlockedDescription;
            AddText(desc, box.transform, 16, Paper,
                new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.54f));

            var rewardText = "";
            if (ach.goldReward > 0) rewardText += $"+{ach.goldReward:N0} Cash  ";
            if (ach.entourageReward > 0) rewardText += $"Unlocked Entourage #{ach.entourageReward}  ";
            if (ach.pantsReward > 0) rewardText += $"Unlocked Pants #{ach.pantsReward}";
            if (!string.IsNullOrEmpty(rewardText))
            {
                AddText($"Reward: {rewardText}", box.transform, 14, Grass,
                    new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.36f));
            }

            AddButton("DONE", box.transform, Gold,
                new Vector2(0.5f, 0.12f), new Vector2(0.44f, 0.12f), () => Destroy(modal));
        }

        private Image[] CreateDiceRowBelt(Transform parent, bool home)
        {
            var dice = new Image[3];
            var xs = home ? new[] { 0.16f, 0.27f, 0.38f } : new[] { 0.62f, 0.73f, 0.84f };
            const float y = 0.162f;

            for (var i = 0; i < 3; i++)
            {
                var background = AddPanel($"{(home ? "Home" : "Away")}Die{i + 1}", parent,
                    Color.white, new Vector2(xs[i], y), new Vector2(0.095f, 0.055f));

                if (DiceBackgroundSprite == null)
                {
                    var texture = Resources.Load<Texture2D>("Original/sprites/Buff6/DiceBackground");
                    if (texture == null)
                        throw new FileNotFoundException(
                            "Required original image is missing: Resources/Original/sprites/Buff6/DiceBackground.png");
                    DiceBackgroundSprite = Sprite.Create(texture,
                        new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
                background.sprite = DiceBackgroundSprite;
                background.preserveAspect = true;

                var faceObject = new GameObject("Face", typeof(RectTransform), typeof(Image));
                faceObject.transform.SetParent(background.transform, false);
                var faceRect = faceObject.GetComponent<RectTransform>();
                faceRect.anchorMin = Vector2.zero;
                faceRect.anchorMax = Vector2.one;
                faceRect.offsetMin = faceRect.offsetMax = Vector2.zero;
                dice[i] = faceObject.GetComponent<Image>();
                dice[i].preserveAspect = true;
            }

            return dice;
        }

        private static void ShowFinalFaces(Image[] dice, int[] faces)
        {
            for (var i = 0; i < dice.Length; i++)
            {
                if (i < faces.Length)
                    SetDieFace(dice[i], faces[i]);
                else
                    dice[i].enabled = false;
            }
        }

        private static void SetDieFace(Image image, int face)
        {
            var sprite = GetDiceSprite(face);
            image.enabled = true;
            image.sprite = sprite;
        }

        private static Sprite GetDiceSprite(int face)
        {
            if (DiceSprites.TryGetValue(face, out var sprite))
                return sprite;

            var path = $"Original/sprites/Buff6/DiceSide{face:00}";
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null)
                throw new FileNotFoundException($"Required original die face is missing: Resources/{path}.png");
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            DiceSprites.Add(face, sprite);
            return sprite;
        }

        private IEnumerator SmoothShiftFieldAndBall(int fromLoc, int toLoc, int possession)
        {
            var startPos = GetBallPosition(fromLoc, possession);
            var targetPos = GetBallPosition(toLoc, possession);
            var elapsed = 0f;
            const float duration = 0.5f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                var pos = Vector2.Lerp(startPos, targetPos, t);
                if (_ballMarker != null)
                {
                    _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = pos;
                }
                if (_ballShadow != null)
                {
                    _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax =
                        new Vector2(pos.x, pos.y - 0.015f);
                }
                yield return null;
            }
            if (_ballMarker != null)
            {
                _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = targetPos;
            }
            if (_ballShadow != null)
            {
                _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax =
                    new Vector2(targetPos.x, targetPos.y - 0.015f);
            }
        }

        private static Vector2 GetBallPosition(int location, int possession)
        {
            var sectorX = 0.5f + location * 0.18f;
            var offset = possession == 0 ? -0.035f : 0.035f;
            return new Vector2(Mathf.Clamp01(sectorX + offset), 0.50f);
        }

        private void SetBallPosition(int location, int possession, bool smooth)
        {
            var target = GetBallPosition(location, possession);
            if (_ballMarker != null)
            {
                _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = target;
                _ballMarker.rectTransform.anchoredPosition = Vector2.zero;
            }
            if (_ballShadow != null)
            {
                _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax =
                    new Vector2(target.x, target.y - 0.015f);
                _ballShadow.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        private string GetEncounterPreviewText()
        {
            if (_match == null) return "Shake or tap either screen edge to roll.";
            switch (_match.Location)
            {
                case 0:
                    return "MIDFIELD BATTLE — WIN POSSESSION & ADVANCE";
                case 1:
                    return _match.Possession == 0
                        ? "ATTACKING BLUE DEFENSE — BREAK THROUGH!"
                        : "DEFENDING MIDFIELD — BLUE COUNTER ATTACK";
                case 2:
                    return _match.Possession == 0
                        ? "SHOT ON GOAL! — YOUR CF vs BLUE GOALKEEPER"
                        : "BLUE CLEARANCE ATTEMPT";
                case -1:
                    return _match.Possession == 1
                        ? "DEFENDING RED GOAL — STOP THE ATTACK!"
                        : "CLEARING FROM RED DEFENSE";
                case -2:
                    return _match.Possession == 1
                        ? "GOAL THREAT! — BLUE CF vs YOUR GOALKEEPER"
                        : "RED GOALKEEPER CLEARANCE";
                default:
                    return "Shake or tap either screen edge to roll.";
            }
        }

        private Image[] AddFormationPlayers(PlayerData[] lineup, Transform parent, bool home)
        {
            var players = new Image[lineup.Length];
            var shirt = _catalog.GetShirt(home ? _currentShirtId : (_currentFixture != null ? _currentFixture.shirt : 1));
            var shirtRgb = _catalog.GetColourRgb(shirt != null ? shirt.shirtColour : (home ? 1 : 2));
            var pantsRgb = _catalog.GetColourRgb(home ? _currentPantsId : (_currentFixture != null ? _currentFixture.pants : 1));

            for (var slot = 0; slot < lineup.Length; slot++)
            {
                var figureId = lineup[slot].id >= 1 && lineup[slot].id <= 96
                    ? lineup[slot].id
                    : 0;
                var resourcePath = figureId == 0
                    ? (home ? "Original/images/PlayerHead1012" : "Original/images/PlayerHead1013")
                    : $"Original/sprites/PlayerFullFigureSS/PlayerFullFigure{figureId:000}";

                var player = AddResourceImage(
                    $"{(home ? "Home" : "Away")}Player{slot + 1}",
                    resourcePath, parent,
                    Vector2.zero, Vector2.zero);
                player.rectTransform.sizeDelta = new Vector2(56f, 96f);
                player.color = home ? Color.white : new Color32(200, 220, 245, 255);
                player.raycastTarget = false;
                player.preserveAspect = true;
                player.rectTransform.localScale = new Vector3(home ? 1f : -1f, 1f, 1f);

                // Add team kit jersey band to visually show the custom team kit:
                if (figureId > 0 && slot > 0)
                {
                    var kitBand = AddPanel("KitJersey", player.transform,
                        new Color(shirtRgb.r / 255f, shirtRgb.g / 255f, shirtRgb.b / 255f, 0.85f),
                        new Vector2(0.24f, 0.42f), new Vector2(0.76f, 0.58f));
                    kitBand.raycastTarget = false;

                    var pantsBand = AddPanel("KitShorts", player.transform,
                        new Color(pantsRgb.r / 255f, pantsRgb.g / 255f, pantsRgb.b / 255f, 0.85f),
                        new Vector2(0.28f, 0.28f), new Vector2(0.72f, 0.40f));
                    pantsBand.raycastTarget = false;
                }

                players[slot] = player;
            }

            return players;
        }

        private void UpdatePlayerPositions()
        {
            if (_homePlayers == null || _awayPlayers == null || _match == null) return;
            var loc = _match.Location;

            for (var slot = 0; slot < 7; slot++)
            {
                var isHomeDuel = (loc == -2 && slot == 0) ||
                                 (loc == -1 && slot >= 1 && slot <= 3) ||
                                 (loc == 0 && slot >= 4 && slot <= 6) ||
                                 (loc == 1 && slot >= 4 && slot <= 6) ||
                                 (loc == 2 && slot == 5);

                var isAwayDuel = (loc == -2 && slot == 5) ||
                                 (loc == -1 && slot >= 4 && slot <= 6) ||
                                 (loc == 0 && slot >= 4 && slot <= 6) ||
                                 (loc == 1 && slot >= 1 && slot <= 3) ||
                                 (loc == 2 && slot == 0);

                var homeBase = _homeBasePositions[slot];
                var awayBase = _awayBasePositions[slot];

                var homeX = isHomeDuel ? Mathf.Min(homeBase.x + 0.10f, 0.44f + (loc > 0 ? loc * 0.16f : 0f)) : homeBase.x;
                var awayX = isAwayDuel ? Mathf.Max(awayBase.x - 0.10f, 0.56f + (loc < 0 ? loc * 0.16f : 0f)) : awayBase.x;

                _homePlayers[slot].rectTransform.anchorMin = _homePlayers[slot].rectTransform.anchorMax =
                    new Vector2(homeX, homeBase.y);
                _homePlayers[slot].rectTransform.anchoredPosition = Vector2.zero;

                _awayPlayers[slot].rectTransform.anchorMin = _awayPlayers[slot].rectTransform.anchorMax =
                    new Vector2(awayX, awayBase.y);
                _awayPlayers[slot].rectTransform.anchoredPosition = Vector2.zero;

                _homePlayers[slot].rectTransform.localScale = new Vector3(isHomeDuel ? 1.15f : 1.0f, isHomeDuel ? 1.15f : 1.0f, 1f);
                _awayPlayers[slot].rectTransform.localScale = new Vector3(isAwayDuel ? -1.15f : -1.0f, isAwayDuel ? 1.15f : 1.0f, 1f);
            }
        }

        private void AnimateAttackingPlayers(int attackingSide, float amount)
        {
            var players = attackingSide == 0 ? _homePlayers : _awayPlayers;
            if (players == null) return;
            var direction = attackingSide == 0 ? 1f : -1f;

            for (var slot = 0; slot < players.Length; slot++)
            {
                var rect = players[slot].rectTransform;
                rect.anchoredPosition = new Vector2(direction * 14f * amount, 5f * amount);
            }
        }

        private void ClearCanvas()
        {
            for (var i = _canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = _canvas.transform.GetChild(i);
                if (child != _safeArea)
                    Destroy(child.gameObject);
            }
            for (var i = _safeArea.childCount - 1; i >= 0; i--)
                Destroy(_safeArea.GetChild(i).gameObject);
            _backButton = null;
            _substitutionButton = null;
            _encounterButton = null;
            _overlayContainer = null;
            _inEncounterAnimation = false;
        }

        private void ApplySafeArea()
        {
            if (_safeArea == null)
                return;

            var area = Screen.safeArea;
            _safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            _safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            _safeArea.offsetMin = _safeArea.offsetMax = Vector2.zero;
            _lastSafeArea = area;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }

        private void AddMenuBackdrop(bool showClouds = false)
        {
            AddStretchImage("MenuBackground",
                "Original/sprites/StartScreenSS/StartScreenBG", _canvas.transform);
            _safeArea.SetAsLastSibling();
            if (!showClouds)
                return;

            var clouds = AddResourceImage("MenuClouds",
                "Original/sprites/StartScreenSS/StartScreenClouds",
                _safeArea, new Vector2(0f, 0.7f), new Vector2(1f, 0.94f));
            clouds.raycastTarget = false;
        }

        private void AddMenuHeader(string title, UnityEngine.Events.UnityAction backAction)
        {
            var header = AddResourceImage("MenuHeader", "Original/images/MenuHeader",
                _safeArea, new Vector2(0f, 0.92f), Vector2.one);
            header.preserveAspect = false;
            header.raycastTarget = false;

            if (backAction != null)
                AddSpriteButton("Back", "Original/images/MenuHeaderBackButton",
                    "Original/images/MenuHeaderBackButtonPressed", _safeArea,
                    new Vector2(0.075f, 0.96f), new Vector2(0.13f, 0.085f), backAction);

            AddText(title.ToUpperInvariant(), _safeArea, 20, Ink,
                new Vector2(0.2f, 0.935f), new Vector2(0.72f, 0.99f));
            AddResourceImage("CareerCoin", "Original/images/CoinSmall", _safeArea,
                new Vector2(0.78f, 0.94f), new Vector2(0.84f, 0.99f)).raycastTarget = false;
            AddText(_careerCash.ToString("N0"), _safeArea, 16, Ink,
                new Vector2(0.83f, 0.935f), new Vector2(0.99f, 0.99f));
        }

        private void AddMenuFooter()
        {
            var footer = AddResourceImage("MenuFooter", "Original/images/FooterBG",
                _safeArea, new Vector2(0f, 0f), new Vector2(1f, 0.14f));
            footer.preserveAspect = false;
            footer.raycastTarget = false;

            AddSpriteButton("Shop", "Original/images/BottomTab01",
                "Original/images/BottomTab01Pressed", _safeArea,
                new Vector2(0.23f, 0.075f), new Vector2(0.25f, 0.11f), ShowShop);
            AddSpriteButton("Campaigns", "Original/images/BottomTab02",
                "Original/images/BottomTab02Pressed", _safeArea,
                new Vector2(0.5f, 0.075f), new Vector2(0.25f, 0.11f), StartCareerMatch);
            AddSpriteButton("Options", "Original/images/BottomTab03",
                "Original/images/BottomTab03Pressed", _safeArea,
                new Vector2(0.77f, 0.075f), new Vector2(0.25f, 0.11f), ShowOptions);
        }

        private static Button AddSpriteButton(string objectName, string resourcePath,
            string pressedSpritePath, Transform parent, Vector2 anchor, Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var image = AddResourceImage(objectName, resourcePath, parent,
                anchor - size * 0.5f, anchor + size * 0.5f);
            image.preserveAspect = false;
            image.color = Color.white;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance.PlayTap();
                onClick?.Invoke();
            });
            button.transition = Selectable.Transition.SpriteSwap;
            var spriteState = button.spriteState;
            spriteState.highlightedSprite = GetUiSprite(pressedSpritePath);
            spriteState.pressedSprite = GetUiSprite(pressedSpritePath);
            button.spriteState = spriteState;
            return button;
        }

        private static Image AddPanel(string objectName, Transform parent, Color color,
            Vector2 anchor, Vector2 size)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor - size * 0.5f;
            rect.anchorMax = anchor + size * 0.5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Image AddStretchPanel(string objectName, Transform parent, Color color)
        {
            var image = AddPanel(objectName, parent, color, Vector2.zero, Vector2.zero);
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return image;
        }

        private static Image AddStretchImage(string objectName, string resourcePath, Transform parent)
        {
            var image = AddResourceImage(objectName, resourcePath, parent, Vector2.zero, Vector2.one);
            image.preserveAspect = false;
            var rect = image.rectTransform;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return image;
        }

        private static Image AddResourceImage(string objectName, string resourcePath, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var gameObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = gameObject.GetComponent<Image>();
            var sprite = TryGetUiSprite(resourcePath);
            if (sprite != null)
                image.sprite = sprite;
            else
                image.color = new Color(0, 0, 0, 0);
            image.preserveAspect = true;
            return image;
        }

        private static Sprite TryGetUiSprite(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return null;
            if (UiSprites.TryGetValue(resourcePath, out var sprite))
                return sprite;

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
                return null;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            UiSprites.Add(resourcePath, sprite);
            return sprite;
        }

        private static Sprite GetUiSprite(string resourcePath)
        {
            var sprite = TryGetUiSprite(resourcePath);
            if (sprite != null)
                return sprite;
            throw new FileNotFoundException(
                $"Required original image is missing: Resources/{resourcePath}.png");
        }

        private static Text AddText(string value, Transform parent, int fontSize, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var gameObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var text = gameObject.GetComponent<Text>();
            text.text = value;
            text.font = GetBuiltinFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button AddButton(string label, Transform parent, Color color,
            Vector2 anchor, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var image = AddPanel(label, parent, Color.white, anchor, size);
            image.sprite = GetUiSprite("Original/images/BlankMainMenuButton");
            image.type = Image.Type.Simple;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance.PlayTap();
                onClick?.Invoke();
            });
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color32(255, 220, 135, 255);
            colors.disabledColor = new Color32(160, 155, 142, 255);
            button.colors = colors;
            var text = AddText(label, image.transform, 18, Ink, Vector2.zero, Vector2.one);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = 18;
            return button;
        }

        private static Button AddPlayerButton(string label, int playerId, Transform parent,
            Color color, Vector2 anchor, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var button = AddButton(label, parent, color, anchor, size, onClick);
            var textRect = button.GetComponentInChildren<Text>().rectTransform;
            textRect.anchorMin = new Vector2(0.22f, 0f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var portrait = AddResourceImage("PlayerHead",
                $"Original/images/PlayerHead{playerId:00}", button.transform,
                new Vector2(0.025f, 0.12f), new Vector2(0.195f, 0.88f));
            portrait.raycastTarget = false;
            return button;
        }

        private static Font GetBuiltinFont()
        {
            var originalFont = Resources.Load<Font>("Original/fonts/Krungthep");
            return originalFont != null
                ? originalFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
