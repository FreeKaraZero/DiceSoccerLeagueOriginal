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
        private GameObject[] _homeDieRoots;
        private GameObject[] _awayDieRoots;
        private Image[] _homePlayers;
        private Image[] _awayPlayers;
        private float[] _homePlayerScales;
        private float[] _awayPlayerScales;
        private Image _ballMarker;
        private Transform _field;
        private RectTransform _fieldPitchRoot;
        private Image _shakePrompt;
        private Text _homeTotalText;
        private Text _awayTotalText;
        private GameObject _buffBeltGo;
        private Text _buffBeltText;
        private Image _buffBeltMedal;
        private MatchSimulation _match;
        private GameCatalog _catalog;
        private FixtureData[] _seasonFixtures;
        private FixtureData _currentFixture;
        private string _competitionName = "League";
        private int[] _lineupIds;
        private List<int> _ownedPlayerIds;
        private int _selectedFixtureIndex;
        private int _hireCandidateIndex;
        private int _selectedLineupSlot;
        private int _seasonIndex;
        private int _fixtureIndex;
        private int _careerCash;
        private HashSet<int> _unlockedShirtIds = new HashSet<int> { 1 };
        private HashSet<int> _unlockedPantsIds = new HashSet<int> { 1 };
        private int _currentShirtId = 1;
        private int _currentPantsId = 1;
        private int _currentCrestId = 1;
        private int _currentPatternStyle = 0;
        private int _currentShirtColorId = 1;
        private int _currentPatternColorId = 10;
        private int _currentSleeveColorId = 1;
        private int _deckCategoryFilter = -1; // -1 = Auto/Target slot role, 0 = ALL, 1 = GK, 2 = DEF, 3 = MID, 4 = FWD
        private int _deckPage = 0;
        private int _kitEditTab = 0; // 0 = Jersey, 1 = Pattern, 2 = Shorts, 3 = Sleeves
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
        private Coroutine _idleBobbingCoroutine;
        private bool _isLocalMultiplayer;
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

            if (_shakePrompt != null && _shakePrompt.gameObject.activeSelf)
            {
                float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 6f);
                _shakePrompt.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            }

            if (_homePlayers != null && _awayPlayers != null && !_inEncounterAnimation)
            {
                var bob = 1f + 0.025f * Mathf.Sin(Time.unscaledTime * 3.5f);
                for (var i = 0; i < _homePlayers.Length; i++)
                {
                    if (_homePlayers[i] != null && _homePlayers[i].gameObject.activeSelf)
                    {
                        float baseScale = (_homePlayerScales != null && i < _homePlayerScales.Length) ? _homePlayerScales[i] : 1f;
                        _homePlayers[i].rectTransform.localScale = new Vector3(-baseScale, baseScale * bob, 1f); // Home faces right towards opponent
                    }
                    if (_awayPlayers[i] != null && _awayPlayers[i].gameObject.activeSelf)
                    {
                        float baseScale = (_awayPlayerScales != null && i < _awayPlayerScales.Length) ? _awayPlayerScales[i] : 1f;
                        _awayPlayers[i].rectTransform.localScale = new Vector3(baseScale, baseScale * bob, 1f); // Away faces left towards home
                    }
                }
            }

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

            if (_isLocalMultiplayer)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    PlayEncounter();
                    return;
                }
                foreach (var touch in Input.touches)
                {
                    if (touch.phase == TouchPhase.Began)
                    {
                        PlayEncounter();
                        return;
                    }
                }
                return;
            }

            var edgeWidth = Screen.width * EdgeTapWidth;
            if (Input.GetMouseButtonDown(0) &&
                (Input.mousePosition.x <= edgeWidth || Input.mousePosition.x >= Screen.width - edgeWidth))
            {
                PlayEncounter();
                return;
            }

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
            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = Color.black;
            }

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
            scaler.matchWidthOrHeight = 0f;

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
            _isLocalMultiplayer = false;
            AudioManager.Instance.PlayBgm("MainTheme", 0.5f);
            AudioManager.Instance.StopCrowd();
            ClearCanvas();
            AddMenuBackdrop(true);
            AddMenuHeader($"SEASON {_seasonIndex}", null);
            AddMenuFooter();

            // Authentic hero logo centered and prominent matching original iOS frames
            var logo = AddResourceImage("StartScreenLogo", "Original/images/StartScreenLogo",
                _safeArea, new Vector2(0.18f, 0.55f), new Vector2(0.82f, 0.89f));
            logo.preserveAspect = true;
            logo.raycastTarget = false;

            // Signature metallic crystal shine overlay from original iOS title
            var logoShine = AddResourceImage("StartScreenLogoShine", "Original/sprites/StartScreenLogoAnimation/LogoShine01",
                logo.transform, Vector2.zero, Vector2.one);
            logoShine.preserveAspect = true;
            logoShine.raycastTarget = false;
            logoShine.enabled = false;
            StartCoroutine(AnimateLogoShine(logoShine));

            var catalogAvailable = Resources.Load<TextAsset>("Original/data/UnityCatalog") != null;
            var campaign = AddSpriteButton("Campaign", "Original/images/MainMenuButtons01",
                "Original/images/MainMenuButtons01Pressed", _safeArea,
                new Vector2(0.5f, 0.44f), new Vector2(0.70f, 0.11f), StartCareerMatch);
            campaign.interactable = catalogAvailable;

            var mp = AddSpriteButton("Multiplayer", "Original/images/MainMenuButtons02",
                "Original/images/MainMenuButtons02Pressed", _safeArea,
                new Vector2(0.5f, 0.32f), new Vector2(0.70f, 0.11f), ShowMultiplayer);
            mp.interactable = catalogAvailable;

            var manage = AddSpriteButton("Manage", "Original/images/MainMenuButtons03",
                "Original/images/MainMenuButtons03Pressed", _safeArea,
                new Vector2(0.5f, 0.20f), new Vector2(0.70f, 0.11f), ShowManage);
            manage.interactable = catalogAvailable;

            if (!catalogAvailable)
            {
                AddText("Run Tools/extract_assets.py to import the original game data.",
                    _safeArea, 16, new Color32(255, 193, 133, 255),
                    new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.19f));
            }
        }

        private void ShowOptions()
        {
            ClearCanvas();
            AddMenuBackdrop();
            AddMenuHeader("OPTIONS", ShowHome);

            AddText("GAME SETTINGS", _safeArea, 18, Ink,
                new Vector2(0.1f, 0.74f), new Vector2(0.9f, 0.80f));
            AddButton(_soundEnabled ? "SOUND: ON" : "SOUND: OFF", _safeArea, Gold,
                new Vector2(0.5f, 0.67f), new Vector2(0.75f, 0.07f), ToggleSound);

            bool fullScreen = PlayerPrefs.GetInt("diceSoccer.fullscreen", 0) == 1;
            AddButton(fullScreen ? "DISPLAY: FILL SCREEN" : "DISPLAY: CLASSIC 2:3", _safeArea, new Color32(50, 120, 180, 255),
                new Vector2(0.5f, 0.58f), new Vector2(0.75f, 0.07f), ToggleDisplayMode);

            AddButton("★ UNLOCK EVERYTHING FREE", _safeArea, Grass,
                new Vector2(0.5f, 0.48f), new Vector2(0.75f, 0.07f), () =>
                {
                    UnlockAllContent(false);
                    ShowDonationModal();
                });

            AddButton("☕ BUY A COFFEE (PAYPAL)", _safeArea, new Color32(0, 112, 186, 255),
                new Vector2(0.5f, 0.38f), new Vector2(0.75f, 0.07f), ShowDonationModal);

            AddText("SHAKE OR TAP THE SCREEN EDGE TO ROLL\n100% FREE FAN REMASTER — NO ADS",
                _safeArea, 13, Ink, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.32f));

            AddButton("BACK TO MENU", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.12f), new Vector2(0.55f, 0.06f), ShowHome);
        }

        private void ToggleDisplayMode()
        {
            int current = PlayerPrefs.GetInt("diceSoccer.fullscreen", 0);
            PlayerPrefs.SetInt("diceSoccer.fullscreen", current == 1 ? 0 : 1);
            PlayerPrefs.Save();
            ApplySafeArea();
            ShowOptions();
        }

        private void ToggleSound()
        {
            _soundEnabled = !_soundEnabled;
            AudioManager.Instance.SoundEnabled = _soundEnabled;
            PlayerPrefs.SetInt("diceSoccer.sound", _soundEnabled ? 1 : 0);
            PlayerPrefs.Save();
            ShowOptions();
        }

        private void UnlockAllContent(bool showNotice = true)
        {
            _catalog ??= GameCatalog.Load();
            _ownedPlayerIds = new List<int>();
            foreach (var p in _catalog.Data.players)
            {
                if (p.id > 0 && p.id < 1000 && !_ownedPlayerIds.Contains(p.id))
                    _ownedPlayerIds.Add(p.id);
            }
            _careerCash = 999999;

            if (_catalog.Data.shirts != null)
            {
                foreach (var s in _catalog.Data.shirts)
                {
                    if (s != null && s.id > 0) _unlockedShirtIds.Add(s.id);
                }
            }
            if (_catalog.Data.colours != null)
            {
                foreach (var c in _catalog.Data.colours)
                {
                    if (c != null && c.id > 0) _unlockedPantsIds.Add(c.id);
                }
            }
            if (_catalog.Data.achievements != null)
            {
                foreach (var a in _catalog.Data.achievements)
                {
                    if (a != null && a.id > 0) _unlockedAchievementIds.Add(a.id);
                }
            }
            if (_catalog.Data.entourage != null)
            {
                foreach (var e in _catalog.Data.entourage)
                {
                    if (e != null && e.id > 0) _unlockedEntourageIds.Add(e.id);
                }
            }

            PlayerPrefs.SetString(RosterPreferenceKey, string.Join(",", _ownedPlayerIds));
            PlayerPrefs.SetInt("diceSoccer.cash", _careerCash);
            PlayerPrefs.SetString("diceSoccer.unlockedShirts", string.Join(",", _unlockedShirtIds));
            PlayerPrefs.SetString("diceSoccer.unlockedPants", string.Join(",", _unlockedPantsIds));
            PlayerPrefs.SetString("diceSoccer.achievements", string.Join(",", _unlockedAchievementIds));
            PlayerPrefs.SetString("diceSoccer.unlockedEntourage", string.Join(",", _unlockedEntourageIds));
            PlayerPrefs.SetInt("diceSoccer.allUnlocked", 1);
            PlayerPrefs.Save();
            if (showNotice) AudioManager.Instance.PlayCash();
        }

        private void ShowDonationModal()
        {
            var backdrop = AddPanel("DonationBackdrop", _safeArea,
                new Color(0f, 0f, 0f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.one);
            backdrop.raycastTarget = true;

            var box = AddResourceImage("DonationBox", "Original/sprites/AchievementsSS4/LongDialogBox", backdrop.transform,
                new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
            box.rectTransform.sizeDelta = new Vector2(380f, 520f);
            box.preserveAspect = false;

            var title = AddText("DICE SOCCER REMASTER", box.transform, 20, Ink,
                new Vector2(0.08f, 0.83f), new Vector2(0.92f, 0.91f));
            title.fontStyle = FontStyle.Bold;

            var desc = AddText(
                "Dice Soccer Remaster is a 100% free non-profit fan project preserving this 2012 iOS classic on modern Android.\n\n" +
                "All 96 authentic players, kits, coins and seasons are fully unlocked and free for everyone!\n\n" +
                "If you enjoy the remaster and wish to support the work, feel free to buy a coffee via PayPal:",
                box.transform, 13, Ink,
                new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.82f));
            desc.alignment = TextAnchor.UpperCenter;

            // PayPal Button
            AddButton("☕ BUY A COFFEE (PAYPAL)", box.transform, new Color32(0, 112, 186, 255),
                new Vector2(0.5f, 0.28f), new Vector2(0.80f, 0.08f), () =>
                {
                    Application.OpenURL("https://paypal.me/Freekara3");
                });

            // Re-unlock Button
            AddButton("★ UNLOCK EVERYTHING FREE", box.transform, Grass,
                new Vector2(0.5f, 0.18f), new Vector2(0.80f, 0.08f), () =>
                {
                    UnlockAllContent(false);
                    Destroy(backdrop.gameObject);
                });

            // Close Button
            AddButton("CLOSE", box.transform, new Color32(68, 79, 84, 255),
                new Vector2(0.5f, 0.08f), new Vector2(0.50f, 0.06f), () =>
                {
                    Destroy(backdrop.gameObject);
                });
        }

        private void ShowMultiplayer()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            ClearCanvas();
            AddMenuBackdrop(false);

            var sunGlow = AddResourceImage("SunGlow", "Original/sprites/MultiplayerBGSS/MultiplayerSunGlow",
                _canvas.transform, new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.70f));
            sunGlow.rectTransform.sizeDelta = new Vector2(600f, 400f);
            sunGlow.preserveAspect = true;
            sunGlow.raycastTarget = false;

            _safeArea.SetAsLastSibling();
            AddMenuHeader("1 VS 1", ShowHome);

            var subTitle = AddText("1 VS 1 PASS & PLAY ON THIS DEVICE", _safeArea, 14, Ink,
                new Vector2(0.05f, 0.81f), new Vector2(0.95f, 0.86f));
            subTitle.fontStyle = FontStyle.Bold;

            // Player 1 Team Box (Left)
            var p1Box = AddResourceImage("P1Box", "Original/sprites/ShopSS/ShopBox", _safeArea,
                new Vector2(0.06f, 0.48f), new Vector2(0.48f, 0.78f));
            p1Box.preserveAspect = false;

            var p1Badge = AddResourceImage("P1Badge", "Original/images/CustomCrest1", p1Box.transform,
                new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.70f));
            p1Badge.rectTransform.sizeDelta = new Vector2(64f, 64f);
            p1Badge.preserveAspect = true;

            var p1Title = AddText("PLAYER 1\n(RED TEAM)", p1Box.transform, 15, Ink,
                new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.56f));
            p1Title.fontStyle = FontStyle.Bold;

            AddText("Tap Left Edge to Roll", p1Box.transform, 12, new Color32(80, 80, 80, 255),
                new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.28f));

            // VS Label
            var vsText = AddText("VS", _safeArea, 34, Ink,
                new Vector2(0.44f, 0.58f), new Vector2(0.56f, 0.68f));
            vsText.fontStyle = FontStyle.Bold;

            // Player 2 Team Box (Right)
            var p2Box = AddResourceImage("P2Box", "Original/sprites/ShopSS/ShopBox", _safeArea,
                new Vector2(0.52f, 0.48f), new Vector2(0.94f, 0.78f));
            p2Box.preserveAspect = false;

            var p2Badge = AddResourceImage("P2Badge", "Original/images/CustomCrest2", p2Box.transform,
                new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.70f));
            p2Badge.rectTransform.sizeDelta = new Vector2(64f, 64f);
            p2Badge.preserveAspect = true;

            var p2Title = AddText("PLAYER 2\n(BLUE TEAM)", p2Box.transform, 15, Ink,
                new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.56f));
            p2Title.fontStyle = FontStyle.Bold;

            AddText("Tap Right Edge to Roll", p2Box.transform, 12, new Color32(80, 80, 80, 255),
                new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.28f));

            // Instructions Box
            var infoBox = AddResourceImage("InfoBox", "Original/sprites/AchievementsSS4/LongDialogBox", _safeArea,
                new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.44f));
            infoBox.preserveAspect = false;

            var infoText = AddText(
                "Challenge a friend on this device!\n\n" +
                "• P1 taps the left half of the screen to roll Red dice\n" +
                "• P2 taps the right half of the screen to roll Blue dice\n" +
                "• No server required — play anywhere completely offline!",
                infoBox.transform, 12, Ink,
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.88f));
            infoText.alignment = TextAnchor.MiddleCenter;

            // Start Match Button
            AddSpriteButton("Start1v1Btn", "Original/sprites/EnemyLineupSS1/FormationPlayButton",
                "Original/sprites/EnemyLineupSS1/FormationPlayButtonPressed", _safeArea,
                new Vector2(0.50f, 0.16f), new Vector2(0.38f, 0.09f), StartLocalMultiplayerMatch);

            AddButton("BACK TO MENU", _safeArea, new Color32(68, 79, 84, 255),
                new Vector2(0.50f, 0.06f), new Vector2(0.55f, 0.055f), ShowHome);
        }

        private void StartLocalMultiplayerMatch()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            _isLocalMultiplayer = true;

            var p1Lineup = _catalog.ResolveLineup(_lineupIds);

            // Rival team for Player 2
            var fixtures = _catalog.GetFixtures(0, "League");
            var p2Lineup = (fixtures != null && fixtures.Length > 2)
                ? _catalog.ResolveLineup(fixtures[2].lineup)
                : _catalog.ResolveLineup(new int[] { 8, 9, 10, 11, 12, 13, 14 });

            _currentFixture = new FixtureData
            {
                id = 999,
                teamName = "PLAYER 2",
                lineup = p2Lineup.Select(p => p.id).ToArray(),
                cashWin = 1000,
                cashDraw = 500,
                cashLose = 200,
                shirt = 2,
                pants = 2
            };

            _currentResultRecorded = false;
            _shakeArmed = true;
            _shakeQuietSince = -1f;
            _hasAccelerationSample = false;
            _isKickoffPlayed = false;
            _lastSubstitutedSlot = -1;
            _remainingSubs = 3;
            _match = new MatchSimulation(
                p1Lineup,
                p2Lineup,
                "PLAYER 2",
                0,
                Array.Empty<int>(),
                Array.Empty<int>(),
                0,
                0);

            ShowMatchScreen(p1Lineup);
        }

        private void StartCareerMatch()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            _isLocalMultiplayer = false;
            _seasonIndex = Mathf.Clamp(_seasonIndex, 0, _catalog.Data.seasons.Length - 1);
            _competitionName = "League";
            _seasonFixtures = _catalog.GetFixtures(_seasonIndex, _competitionName);
            if (_seasonFixtures.Length == 0)
                throw new InvalidDataException($"Season {_seasonIndex} has no League fixtures.");
            _fixtureIndex = LoadCompetitionProgress(_competitionName);
            _selectedFixtureIndex = Mathf.Clamp(_fixtureIndex, 0, _seasonFixtures.Length - 1);
            ShowLeagueFixtures();
        }

        private string GetMatchNoSpritePath(int number)
        {
            if (number == 11 || number == 12 || number == 14)
                return $"Original/sprites/MatchSelectionSS2/MatchNo{number}";
            return $"Original/sprites/MatchSelectionSS1/MatchNo{number}";
        }

        private void ShowLeagueFixtures()
        {
            if (_seasonFixtures == null || _seasonFixtures.Length == 0)
                throw new InvalidOperationException("League fixtures have not been loaded.");

            _match = null;
            ClearCanvas();
            AddMenuBackdrop(false);
            AddMenuHeader(null, ShowHome);

            _selectedFixtureIndex = Mathf.Clamp(_selectedFixtureIndex, 0, _seasonFixtures.Length - 1);
            var fixture = _seasonFixtures[_selectedFixtureIndex];
            var tournament = _competitionName == "T1" || _competitionName == "T2";
            var locked = _selectedFixtureIndex > _fixtureIndex ||
                         (tournament && _selectedFixtureIndex == _fixtureIndex &&
                          _fixtureIndex > 0 && !IsPreviousTournamentFixtureWon());
            var isCompleted = _selectedFixtureIndex < _fixtureIndex;

            // Match Title & Opponent in the sky
            var matchTitle = AddText($"MATCH {_selectedFixtureIndex + 1}", _safeArea, 28, Ink,
                new Vector2(0.1f, 0.83f), new Vector2(0.9f, 0.89f));
            matchTitle.alignment = TextAnchor.MiddleCenter;
            matchTitle.fontStyle = FontStyle.Bold;

            var vsText = AddText($"vs. {fixture.teamName.ToUpperInvariant()}", _safeArea, 20, Ink,
                new Vector2(0.1f, 0.77f), new Vector2(0.9f, 0.83f));
            vsText.alignment = TextAnchor.MiddleCenter;
            vsText.fontStyle = FontStyle.Bold;

            var statusStr = locked
                ? "LOCKED"
                : (isCompleted
                    ? "WIN  •  REPLAY AVAILABLE"
                    : $"WIN {fixture.cashWin:N0}   DRAW {fixture.cashDraw:N0}   LOSS {fixture.cashLose:N0}");
            Color statusColor = locked ? (Color)new Color32(100, 100, 100, 255) : (isCompleted ? Grass : Ink);
            var infoText = AddText(statusStr, _safeArea, 15, statusColor,
                new Vector2(0.05f, 0.71f), new Vector2(0.95f, 0.77f));
            infoText.alignment = TextAnchor.MiddleCenter;
            infoText.fontStyle = FontStyle.Bold;

            // Giant Soccer Ball in Center (Left half + Flipped Right half meeting seamlessly at container center)
            var ballSprite = locked
                ? "Original/sprites/MatchSelectionSS1/SoccerBallButtonLocked"
                : "Original/sprites/MatchSelectionSS1/SoccerBallButton";

            var ballContainer = new GameObject("SoccerBallContainer", typeof(RectTransform));
            ballContainer.transform.SetParent(_safeArea, false);
            var containerRect = ballContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.52f);
            containerRect.anchorMax = new Vector2(0.5f, 0.52f);
            containerRect.sizeDelta = new Vector2(300f, 300f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            StartCoroutine(AnimatePulse(containerRect));

            float halfW = 300f * (96f / 190f); // 151.6f
            float halfH = 300f;

            var ballLeft = AddResourceImage("BallLeft", ballSprite, ballContainer.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ballLeft.preserveAspect = false;
            ballLeft.rectTransform.pivot = new Vector2(1f, 0.5f);
            ballLeft.rectTransform.anchoredPosition = Vector2.zero;
            ballLeft.rectTransform.sizeDelta = new Vector2(halfW, halfH);

            var ballRight = AddResourceImage("BallRight", ballSprite, ballContainer.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ballRight.preserveAspect = false;
            ballRight.rectTransform.pivot = new Vector2(1f, 0.5f);
            ballRight.rectTransform.anchoredPosition = Vector2.zero;
            ballRight.rectTransform.sizeDelta = new Vector2(halfW, halfH);
            ballRight.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            // Match Number on the ball
            var matchNum = _selectedFixtureIndex + 1;
            var matchNumPath = GetMatchNoSpritePath(matchNum);
            var matchNumImg = AddResourceImage("MatchNumber", matchNumPath, ballContainer.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            if (matchNumImg != null && matchNumImg.sprite != null)
            {
                matchNumImg.rectTransform.sizeDelta = new Vector2(80f, 80f);
                matchNumImg.preserveAspect = true;
            }
            else
            {
                if (matchNumImg != null) Destroy(matchNumImg.gameObject);
                var numTxt = AddText(matchNum.ToString(), ballContainer.transform, 44, Color.black,
                    new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f));
                numTxt.alignment = TextAnchor.MiddleCenter;
                numTxt.fontStyle = FontStyle.Bold;
            }

            // Left & Right Navigation Chevrons
            var canPrev = _selectedFixtureIndex > 0;
            var leftArrow = AddSpriteButton("LeftArrow",
                canPrev ? "Original/images/LeftArrow" : "Original/images/LeftArrowDisabled",
                canPrev ? "Original/images/LeftArrowSelected" : "Original/images/LeftArrowDisabled",
                _safeArea, new Vector2(0.12f, 0.52f), new Vector2(0.10f, 0.12f),
                canPrev ? () => { _selectedFixtureIndex--; ShowLeagueFixtures(); } : null);
            leftArrow.interactable = canPrev;

            var canNext = _selectedFixtureIndex < _seasonFixtures.Length - 1;
            var rightArrow = AddSpriteButton("RightArrow",
                canNext ? "Original/images/RightArrow" : "Original/images/RightArrowDisabled",
                canNext ? "Original/images/RightArrowSelected" : "Original/images/RightArrowDisabled",
                _safeArea, new Vector2(0.88f, 0.52f), new Vector2(0.10f, 0.12f),
                canNext ? () => { _selectedFixtureIndex++; ShowLeagueFixtures(); } : null);
            rightArrow.interactable = canNext;

            // 3 Stars below ball overlapping bottom rim
            var starSprite = isCompleted
                ? "Original/sprites/MatchSelectionSS1/GoldStar"
                : "Original/sprites/MatchSelectionSS1/EmptyStar";

            var starLeft = AddResourceImage("StarLeft", starSprite, ballContainer.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            starLeft.rectTransform.anchoredPosition = new Vector2(-55f, -115f);
            starLeft.rectTransform.sizeDelta = new Vector2(70f, 70f);
            starLeft.preserveAspect = true;

            var starRight = AddResourceImage("StarRight", starSprite, ballContainer.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            starRight.rectTransform.anchoredPosition = new Vector2(55f, -115f);
            starRight.rectTransform.sizeDelta = new Vector2(70f, 70f);
            starRight.preserveAspect = true;

            var starMid = AddResourceImage("StarMid", starSprite, ballContainer.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            starMid.rectTransform.anchoredPosition = new Vector2(0f, -125f);
            starMid.rectTransform.sizeDelta = new Vector2(85f, 85f);
            starMid.preserveAspect = true;
            starMid.transform.SetAsLastSibling();

            // Action Buttons: MANAGE and PLAY!
            AddSpriteButton("ManageBtn", "Original/sprites/EnemyLineupSS1/ManageButton",
                "Original/sprites/EnemyLineupSS1/ManageButtonPressed", _safeArea,
                new Vector2(0.35f, 0.24f), new Vector2(0.28f, 0.085f), ShowManage);

            var playNormal = locked
                ? "Original/sprites/MatchSelectionSS2/ManagePlayButtonDisabled"
                : "Original/sprites/MatchSelectionSS1/ManagePlayButton";
            var playPressed = locked
                ? "Original/sprites/MatchSelectionSS2/ManagePlayButtonDisabled"
                : "Original/sprites/MatchSelectionSS1/ManagePlayButtonPressed";
            var capturedMatchIdx = _selectedFixtureIndex;
            var playBtn = AddSpriteButton("PlayBtn", playNormal, playPressed, _safeArea,
                new Vector2(0.65f, 0.24f), new Vector2(0.28f, 0.085f),
                locked ? null : () => StartFixture(capturedMatchIdx));
            playBtn.interactable = !locked;

            // Bottom yellow bar & competition tabs
            var footerBar = AddResourceImage("FixtureFooter", "Original/images/FooterBG", _safeArea,
                new Vector2(0f, 0f), new Vector2(1f, 0.08f));
            footerBar.preserveAspect = false;
            footerBar.raycastTarget = false;

            var competitions = _catalog.GetCompetitions(_seasonIndex);
            var availableComps = new List<CompetitionData>();
            foreach (var comp in competitions)
            {
                if (comp.fixtures != null && comp.fixtures.Length > 0)
                    availableComps.Add(comp);
            }

            var tabWidth = 0.22f;
            for (var i = 0; i < availableComps.Count; i++)
            {
                var comp = availableComps[i];
                var cName = comp.name;
                var isSel = cName == _competitionName;
                var tabPath = $"Original/sprites/MatchSelectionSS1/Campaign{cName}Tab" + (isSel ? "Selected" : string.Empty);
                var xCenter = 0.15f + i * 0.24f;
                var tabBtn = AddSpriteButton($"Tab_{cName}", tabPath, tabPath, _safeArea,
                    new Vector2(xCenter, 0.045f), new Vector2(tabWidth, 0.08f),
                    () => SelectCompetition(cName));
                tabBtn.interactable = IsCompetitionAvailable(cName);
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
            _selectedFixtureIndex = Mathf.Clamp(_fixtureIndex, 0, _seasonFixtures.Length - 1);
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

            // Solid black letterbox background behind safe area
            var bgObj = AddPanel("MatchLetterboxBG", _canvas.transform, Color.black, Vector2.zero, Vector2.one);
            bgObj.raycastTarget = false;
            _safeArea.SetAsLastSibling();

            // 1. Pitch Field (fills full 640x960 safe area from top to bottom):
            var fieldGo = new GameObject("FieldContainer", typeof(RectTransform), typeof(RectMask2D));
            fieldGo.transform.SetParent(_safeArea, false);
            var fieldRect = fieldGo.GetComponent<RectTransform>();
            fieldRect.anchorMin = Vector2.zero;
            fieldRect.anchorMax = Vector2.one;
            fieldRect.offsetMin = fieldRect.offsetMax = Vector2.zero;
            _field = fieldRect;

            // Pitch Root for horizontal camera panning:
            var pitchRootGo = new GameObject("FieldPitchRoot", typeof(RectTransform));
            pitchRootGo.transform.SetParent(_field, false);
            _fieldPitchRoot = pitchRootGo.GetComponent<RectTransform>();
            _fieldPitchRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _fieldPitchRoot.anchorMax = new Vector2(0.5f, 0.5f);
            float fieldPitchHeight = 960f;
            if (_safeArea != null && _safeArea.rect.height > 960f)
                fieldPitchHeight = _safeArea.rect.height;
            float pitchScale = fieldPitchHeight / 960f;
            _fieldPitchRoot.sizeDelta = new Vector2(1920f * pitchScale, fieldPitchHeight);
            _fieldPitchRoot.anchoredPosition = new Vector2(GetPitchOffsetForLocation(_match.Location) * pitchScale, 0f);

            // Left half of pitch (goal on left, center circle on right):
            var leftFieldGo = new GameObject("FieldLeft", typeof(RectTransform), typeof(Image));
            leftFieldGo.transform.SetParent(_fieldPitchRoot, false);
            var leftRect = leftFieldGo.GetComponent<RectTransform>();
            leftRect.anchorMin = leftRect.anchorMax = new Vector2(0.5f, 0.5f);
            leftRect.pivot = new Vector2(1f, 0.5f);
            leftRect.sizeDelta = new Vector2(fieldPitchHeight, fieldPitchHeight);
            leftRect.anchoredPosition = Vector2.zero;
            var leftImg = leftFieldGo.GetComponent<Image>();
            leftImg.sprite = GetUiSprite("Original/sprites/Season0Field2(1.1)/Field");
            leftImg.preserveAspect = false;
            leftImg.raycastTarget = false;

            // Right half of pitch (flipped horizontally to form full pitch):
            var rightFieldGo = new GameObject("FieldRight", typeof(RectTransform), typeof(Image));
            rightFieldGo.transform.SetParent(_fieldPitchRoot, false);
            var rightRect = rightFieldGo.GetComponent<RectTransform>();
            rightRect.anchorMin = rightRect.anchorMax = new Vector2(0.5f, 0.5f);
            rightRect.pivot = new Vector2(1f, 0.5f);
            rightRect.sizeDelta = new Vector2(fieldPitchHeight, fieldPitchHeight);
            rightRect.anchoredPosition = Vector2.zero;
            rightRect.localScale = new Vector3(-1f, 1f, 1f);
            var rightImg = rightFieldGo.GetComponent<Image>();
            rightImg.sprite = GetUiSprite("Original/sprites/Season0Field2(1.1)/Field");
            rightImg.preserveAspect = false;
            rightImg.raycastTarget = false;

            // Field decorations (Season 0 authentic props):
            // Center circle props:
            var signDeco = AddResourceImage("SignDeco", "Original/sprites/Season0Field1(1.1)/Object9",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            signDeco.rectTransform.anchoredPosition = new Vector2(105f, 225f);
            signDeco.rectTransform.sizeDelta = new Vector2(75f, 70f);
            signDeco.preserveAspect = true;
            signDeco.raycastTarget = false;

            var grassDeco = AddResourceImage("CenterGrassDeco", "Original/sprites/Season0Field1(1.1)/Object8",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            grassDeco.rectTransform.anchoredPosition = new Vector2(-25f, -80f);
            grassDeco.rectTransform.sizeDelta = new Vector2(45f, 32f);
            grassDeco.preserveAspect = true;
            grassDeco.raycastTarget = false;

            var boneDeco = AddResourceImage("CenterBoneDeco", "Original/sprites/Season0Field1(1.1)/Object3",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            boneDeco.rectTransform.anchoredPosition = new Vector2(-170f, 210f);
            boneDeco.rectTransform.sizeDelta = new Vector2(48f, 26f);
            boneDeco.preserveAspect = true;
            boneDeco.raycastTarget = false;

            var boneDeco2 = AddResourceImage("BottomBoneDeco", "Original/sprites/Season0Field1(1.1)/Object3",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            boneDeco2.rectTransform.anchoredPosition = new Vector2(-140f, -220f);
            boneDeco2.rectTransform.sizeDelta = new Vector2(48f, 26f);
            boneDeco2.preserveAspect = true;
            boneDeco2.raycastTarget = false;

            // Away goal area props (right side):
            var awaySkullDeco = AddResourceImage("AwaySkullDeco", "Original/sprites/Season0Field1(1.1)/Object11",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            awaySkullDeco.rectTransform.anchoredPosition = new Vector2(570f, -40f);
            awaySkullDeco.rectTransform.sizeDelta = new Vector2(40f, 30f);
            awaySkullDeco.preserveAspect = true;
            awaySkullDeco.raycastTarget = false;

            // Home goal area props (left side):
            var homeShoeDeco = AddResourceImage("HomeShoeDeco", "Original/sprites/Season0Field1(1.1)/Object1",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            homeShoeDeco.rectTransform.anchoredPosition = new Vector2(-510f, -190f);
            homeShoeDeco.rectTransform.sizeDelta = new Vector2(52f, 34f);
            homeShoeDeco.preserveAspect = true;
            homeShoeDeco.raycastTarget = false;

            var homeSkullDeco = AddResourceImage("HomeSkullDeco", "Original/sprites/Season0Field1(1.1)/Object11",
                _fieldPitchRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            homeSkullDeco.rectTransform.anchoredPosition = new Vector2(-570f, -40f);
            homeSkullDeco.rectTransform.sizeDelta = new Vector2(40f, 30f);
            homeSkullDeco.preserveAspect = true;
            homeSkullDeco.raycastTarget = false;

            // Soccer ball with shadow (sized to authentic proportions):
            var ballShadow = AddResourceImage("BallShadow", "Original/sprites/PlayerBody3/GameSoccerBallShadow",
                _field, Vector2.zero, Vector2.zero);
            ballShadow.rectTransform.sizeDelta = new Vector2(36f, 18f);
            ballShadow.raycastTarget = false;
            _ballShadow = ballShadow;

            var ball = AddResourceImage("Ball", "Original/sprites/PlayerBody3/GameSoccerBall",
                _field, Vector2.zero, Vector2.zero);
            ball.rectTransform.sizeDelta = new Vector2(38f, 38f);
            ball.raycastTarget = false;
            _ballMarker = ball;

            // Formation players:
            var awayLineup = _catalog.ResolveLineup(_currentFixture.lineup);
            _homePlayers = AddFormationPlayers(homeLineup, _field, true);
            _awayPlayers = AddFormationPlayers(awayLineup, _field, false);

            // Ball and shadow always stay crisp above pitch elements and boots
            if (_ballShadow != null) _ballShadow.transform.SetAsLastSibling();
            if (_ballMarker != null) _ballMarker.transform.SetAsLastSibling();

            if (_idleBobbingCoroutine != null) StopCoroutine(_idleBobbingCoroutine);
            _idleBobbingCoroutine = StartCoroutine(IdlePlayerBobbing());

            // Dice on Field:
            CreateFieldDice(_field);

            // Duel Totals on sides of dice (matching media_1792421110113_45d0305f.png):
            _homeTotalText = AddText("", _field, 50, new Color32(50, 45, 40, 255),
                new Vector2(0.18f, 0.20f), new Vector2(0.18f, 0.20f));
            _homeTotalText.rectTransform.sizeDelta = new Vector2(90f, 70f);
            _homeTotalText.fontStyle = FontStyle.Bold;
            _homeTotalText.alignment = TextAnchor.MiddleCenter;
            _homeTotalText.enabled = false;

            _awayTotalText = AddText("", _field, 50, new Color32(50, 45, 40, 255),
                new Vector2(0.83f, 0.20f), new Vector2(0.83f, 0.20f));
            _awayTotalText.rectTransform.sizeDelta = new Vector2(90f, 70f);
            _awayTotalText.fontStyle = FontStyle.Bold;
            _awayTotalText.alignment = TextAnchor.MiddleCenter;
            _awayTotalText.enabled = false;

            // Authentic Buff Belt Tray (matching media_1792421110113_45d0305f.png):
            _buffBeltGo = new GameObject("BuffBelt", typeof(RectTransform), typeof(Image));
            _buffBeltGo.transform.SetParent(_safeArea, false);
            var bbRect = _buffBeltGo.GetComponent<RectTransform>();
            bbRect.anchorMin = new Vector2(0.12f, 0.005f);
            bbRect.anchorMax = new Vector2(0.88f, 0.125f);
            bbRect.offsetMin = bbRect.offsetMax = Vector2.zero;
            var bbImg = _buffBeltGo.GetComponent<Image>();
            bbImg.sprite = GetUiSprite("Original/images/BuffBelt");
            bbImg.preserveAspect = true;
            bbImg.raycastTarget = false;

            _buffBeltMedal = AddResourceImage("BuffMedal", "Original/sprites/Buff6/DisplayBuffTimes", _buffBeltGo.transform,
                new Vector2(0.40f, 0.44f), new Vector2(0.60f, 0.98f));
            _buffBeltMedal.preserveAspect = true;
            _buffBeltMedal.raycastTarget = false;

            _buffBeltText = AddText("PLEASE SELECT TARGET", _buffBeltGo.transform, 14, Paper,
                new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.44f));
            _buffBeltText.alignment = TextAnchor.MiddleCenter;
            _buffBeltText.fontStyle = FontStyle.Bold;
            _buffBeltGo.SetActive(false);

            // Shake button & prompt in field center circle:
            _shakePrompt = AddResourceImage("ShakePrompt", "Original/sprites/Prompt1/Shake",
                _field, new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f));
            _shakePrompt.rectTransform.sizeDelta = new Vector2(360f, 295f);
            _shakePrompt.preserveAspect = true;
            _shakePrompt.raycastTarget = true;
            var shakeBtn = _shakePrompt.gameObject.AddComponent<Button>();
            shakeBtn.transition = Selectable.Transition.None;
            shakeBtn.onClick.AddListener(PlayEncounter);

            // Invisible full-field tap trigger for rolling:
            var tapFieldGo = new GameObject("FieldTapTrigger", typeof(RectTransform), typeof(Button));
            tapFieldGo.transform.SetParent(_field, false);
            tapFieldGo.transform.SetAsFirstSibling();
            var tapFieldRect = tapFieldGo.GetComponent<RectTransform>();
            tapFieldRect.anchorMin = Vector2.zero;
            tapFieldRect.anchorMax = Vector2.one;
            tapFieldRect.offsetMin = tapFieldRect.offsetMax = Vector2.zero;
            var tapFieldBtn = tapFieldGo.GetComponent<Button>();
            tapFieldBtn.transition = Selectable.Transition.None;
            tapFieldBtn.onClick.AddListener(PlayEncounter);
            _encounterButton = tapFieldBtn;

            UpdatePlayerPositions();
            SetBallPosition(_match.Location, _match.Possession, false);

            // Keep match report tracker (hidden to avoid pitch clutter):
            _matchReport = AddText("", _field, 12, Ink, Vector2.zero, Vector2.zero);
            _matchReport.enabled = false;

            // 2. Top HUD: Authentic Scoreboard (scoreboardBG sits at the very top):
            var scoreboard = AddResourceImage("MatchScoreboard",
                "Original/images/scoreboardBG", _safeArea,
                new Vector2(0f, 1f), new Vector2(1f, 1f));
            scoreboard.rectTransform.pivot = new Vector2(0.5f, 1f);
            scoreboard.rectTransform.anchoredPosition = Vector2.zero;
            scoreboard.rectTransform.sizeDelta = new Vector2(0f, 150f);
            scoreboard.preserveAspect = false;
            scoreboard.raycastTarget = false;

            // Home Team Crest Shield centered in the left yellow box of scoreboardBG:
            var homeCrestShield = AddResourceImage("HomeShield", $"Original/images/CustomCrest{_currentCrestId}", _safeArea,
                new Vector2(0.087f, 0.953f), new Vector2(0.087f, 0.953f));
            homeCrestShield.rectTransform.sizeDelta = new Vector2(70f, 70f);
            homeCrestShield.preserveAspect = true;
            homeCrestShield.raycastTarget = false;

            var homeTitle = _isLocalMultiplayer ? "PLAYER 1" : "SANUKU";
            var homeNameText = AddText(homeTitle, _safeArea, 12, Ink,
                new Vector2(0.01f, 0.874f), new Vector2(0.165f, 0.910f));
            homeNameText.fontStyle = FontStyle.Bold;
            homeNameText.alignment = TextAnchor.MiddleCenter;

            // Away Team Crest centered in the right red box of scoreboardBG:
            var awayCrestBody = AddResourceImage("AwayCrestBody", "Original/sprites/EnemyCrest1/EnemyCrest1Body", _safeArea,
                new Vector2(0.913f, 0.953f), new Vector2(0.913f, 0.953f));
            awayCrestBody.rectTransform.sizeDelta = new Vector2(70f, 70f);
            awayCrestBody.preserveAspect = true;
            awayCrestBody.raycastTarget = false;

            var awayCrestDesign = AddResourceImage("AwayDesign", "Original/sprites/EnemyCrest1/EnemyCrest1Design", awayCrestBody.transform,
                new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
            awayCrestDesign.preserveAspect = true;
            awayCrestDesign.raycastTarget = false;

            var awayNameText = AddText(_currentFixture.teamName.ToUpperInvariant(), _safeArea, 10, Ink,
                new Vector2(0.835f, 0.874f), new Vector2(0.99f, 0.910f));
            awayNameText.fontStyle = FontStyle.Bold;
            awayNameText.alignment = TextAnchor.MiddleCenter;
            awayNameText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Center Scoreboard Scores:
            _matchScore = AddText($"{_match.HomeScore} - {_match.AwayScore}", _safeArea, 36, Ink,
                new Vector2(0.35f, 0.925f), new Vector2(0.65f, 0.985f));
            _matchScore.fontStyle = FontStyle.Bold;
            _matchScore.alignment = TextAnchor.MiddleCenter;
            _matchScore.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Pause button at center directly docked under score:
            AddSpriteButton("Pause", "Original/images/PauseButton",
                "Original/images/PauseButtonPressed", _safeArea,
                new Vector2(0.50f, 0.892f), new Vector2(0.09f, 0.052f), ShowPauseMenu);

            // 3. Digital Clock on the Bottom Grass:
            _matchClock = AddText(_match.ClockText(), _safeArea, 25, new Color32(216, 255, 176, 255),
                new Vector2(0.3f, 0.015f), new Vector2(0.7f, 0.065f));
            _matchClock.fontStyle = FontStyle.Bold;
            _matchClock.alignment = TextAnchor.MiddleCenter;
            var clockShadow = _matchClock.gameObject.AddComponent<Shadow>();
            clockShadow.effectColor = Color.black;
            clockShadow.effectDistance = new Vector2(2f, -2f);

            // Substitution button state tracker (interactive via pause menu):
            var subBtnGo = new GameObject("SubButtonTracker", typeof(RectTransform), typeof(Button));
            subBtnGo.transform.SetParent(_safeArea, false);
            subBtnGo.SetActive(false);
            _substitutionButton = subBtnGo.GetComponent<Button>();

            // Overlay container for animations (Kickoff, Goal, Save, etc.):
            var overlayGo = new GameObject("OverlayContainer", typeof(RectTransform));
            overlayGo.transform.SetParent(_safeArea, false);
            _overlayContainer = overlayGo.GetComponent<RectTransform>();
            _overlayContainer.anchorMin = Vector2.zero;
            _overlayContainer.anchorMax = Vector2.one;
            _overlayContainer.offsetMin = _overlayContainer.offsetMax = Vector2.zero;

            // Kick-off banner:
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
            int clamped = Mathf.Clamp(sideValue, 1, 14);
            return $"Original/sprites/Card6/DiceNumber{clamped:00}";
        }

        private static string GetSuperpowerIconPath(int superpowerId)
        {
            if (superpowerId <= 0) return null;
            var path = $"Original/sprites/Card3/DiceFaceAbility{superpowerId}";
            return TryGetUiSprite(path) != null ? path : null;
        }

        private static string ResolvePlayerHeadPath(int id)
        {
            if (id >= 1000)
            {
                var p = $"Original/images/PlayerHead{id}";
                if (TryGetUiSprite(p) != null) return p;
                p = $"Original/sprites/Prompt1/PlayerHead{id}";
                if (TryGetUiSprite(p) != null) return p;
                return "Original/images/PlayerHead1012";
            }
            var p1 = $"Original/images/PlayerHead{id:00}";
            if (TryGetUiSprite(p1) != null) return p1;
            var p2 = $"Original/sprites/Prompt1/PlayerHead{id:00}";
            if (TryGetUiSprite(p2) != null) return p2;
            var p3 = $"Original/sprites/Prompt2/PlayerHead{id:00}";
            if (TryGetUiSprite(p3) != null) return p3;
            return "Original/images/PlayerHead01";
        }

        private static string GetPlayerFigurePath(PlayerData player, int fallbackId = 1)
        {
            int id = 1;
            if (player != null)
            {
                if (player.id >= 1 && player.id <= 96)
                    id = player.id;
                else if (player.id >= 1000)
                    id = ((player.id - 1000) % 96) + 1;
                else if (player.imageId > 0 && player.imageId <= 96)
                    id = player.imageId;
            }
            else if (fallbackId >= 1 && fallbackId <= 96)
            {
                id = fallbackId;
            }

            var path = $"Original/sprites/PlayerFullFigureSS/PlayerFullFigure{id:000}";
            if (TryGetUiSprite(path) != null)
                return path;
            return "Original/sprites/PlayerFullFigureSS/PlayerFullFigure001";
        }

        private static string GetPlayerFigurePath(int playerId)
        {
            var catalog = GameCatalog.Load();
            var p = catalog != null ? catalog.GetPlayer(playerId) : null;
            return GetPlayerFigurePath(p, playerId);
        }

        private static float GetPlayerFigureVisualScale(int playerId)
        {
            // Junior squad players (1, 2, 3, 4, 6) were drawn with ~100px blank headroom in the 340px canvas.
            // Calibrate visual scale so they stand uniformly alongside adult/varsity figures without extreme discrepancy.
            switch (playerId)
            {
                case 1: return 1.25f; // Willy the Kid (GK)
                case 2: return 1.28f; // Earnest Mulligan (DEF)
                case 3: return 1.22f; // Jo Lee-Roger (DEF)
                case 4: return 1.26f; // Sherman 'Tank' (DEF)
                case 6: return 1.24f; // David Little (FWD)
                default: return 1.0f;
            }
        }

        private GameObject CreatePlayerCardFront(PlayerData player, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, bool isHire = false, bool isInteractive = true,
            UnityEngine.Events.UnityAction onClick = null, bool isLocked = false)
        {
            var root = new GameObject($"CardFront_{player.id}", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var border = AddResourceImage("Border", "Original/sprites/PlayerCardBG/CardBorder", root.transform,
                Vector2.zero, Vector2.one);
            border.color = isLocked ? new Color32(160, 165, 170, 255) : Color.white;
            border.preserveAspect = false;

            var field = AddResourceImage("Field", "Original/sprites/PlayerCardBG/CardField", border.transform,
                new Vector2(0.045f, 0.035f), new Vector2(0.955f, 0.965f));
            field.preserveAspect = false;

            // Number badge (Top Left)
            var numBase = AddResourceImage("NumberBase", "Original/sprites/Card3/PlayerNumberBase", field.transform,
                new Vector2(0.04f, 0.79f), new Vector2(0.24f, 0.96f));
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

            // Position badge (Top Right)
            var posPath = $"Original/sprites/Card6/{player.position}Type";
            var posImg = AddResourceImage("PositionType", posPath, field.transform,
                new Vector2(0.76f, 0.81f), new Vector2(0.96f, 0.95f));
            posImg.preserveAspect = true;

            // Name Tag Banner (Top Center between number & position)
            var nameBox = new GameObject("NameBox", typeof(RectTransform), typeof(Image));
            nameBox.transform.SetParent(field.transform, false);
            var nameBoxRect = nameBox.GetComponent<RectTransform>();
            nameBoxRect.anchorMin = new Vector2(0.23f, 0.82f);
            nameBoxRect.anchorMax = new Vector2(0.77f, 0.94f);
            nameBoxRect.offsetMin = nameBoxRect.offsetMax = Vector2.zero;
            var nameBoxImg = nameBox.GetComponent<Image>();
            nameBoxImg.color = new Color32(245, 245, 240, 255);
            var nameOutline = nameBox.AddComponent<Outline>();
            nameOutline.effectColor = new Color32(40, 40, 40, 255);
            nameOutline.effectDistance = new Vector2(1, -1);

            var nameText = AddText(player.name.ToUpperInvariant(), nameBox.transform, 10, Ink,
                new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f));
            nameText.alignment = TextAnchor.MiddleCenter;
            nameText.fontStyle = FontStyle.Bold;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = 5;
            nameText.resizeTextMaxSize = 10;

            // Player figure & shadow standing on the grass
            var figPath = GetPlayerFigurePath(player.id);
            var figure = AddResourceImage("Figure", figPath, field.transform,
                new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.22f));
            figure.rectTransform.pivot = new Vector2(0.5f, 0.032f);
            figure.rectTransform.sizeDelta = new Vector2(85f, 144.5f);
            float cardVScale = GetPlayerFigureVisualScale(player.id);
            figure.rectTransform.localScale = new Vector3(cardVScale, cardVScale, 1f);
            figure.preserveAspect = true;

            var shadowGo = new GameObject("FigureShadow", typeof(RectTransform), typeof(Image));
            shadowGo.transform.SetParent(figure.transform, false);
            var shadowRect = shadowGo.GetComponent<RectTransform>();
            shadowRect.anchorMin = Vector2.zero;
            shadowRect.anchorMax = Vector2.one;
            shadowRect.offsetMin = shadowRect.offsetMax = Vector2.zero;
            var shadowImg = shadowGo.GetComponent<Image>();
            shadowImg.sprite = GetUiSprite("Original/sprites/PlayerFullFigureSS/PlayerFullFigureShadow");
            shadowImg.color = new Color(0, 0, 0, 0.45f);
            shadowImg.preserveAspect = true;
            shadowImg.raycastTarget = false;
            shadowGo.transform.SetAsFirstSibling();

            // Superpower Box (Bottom Right, above dice bar)
            var spIconPath = GetSuperpowerIconPath(player.superpower);
            if (!string.IsNullOrEmpty(spIconPath))
            {
                var spBox = AddResourceImage("SPBox", "Original/sprites/Card6/SPBox", field.transform,
                    new Vector2(0.78f, 0.18f), new Vector2(0.96f, 0.32f));
                spBox.preserveAspect = true;

                var spIcon = AddResourceImage("SPIcon", spIconPath, spBox.transform,
                    new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
                spIcon.preserveAspect = true;
            }

            // Authentic Dice bar at bottom with 6 white square dice slots (matching media_1792421110223_f7c56db8.png)
            var diceBarRoot = new GameObject("DiceBar", typeof(RectTransform));
            diceBarRoot.transform.SetParent(field.transform, false);
            var barRect = diceBarRoot.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.04f, 0.035f);
            barRect.anchorMax = new Vector2(0.96f, 0.170f);
            barRect.offsetMin = barRect.offsetMax = Vector2.zero;

            // Layer 1: Base border with white cutout slots and dividers
            var diceBarBorder = AddResourceImage("DiceBarBorder", "Original/sprites/Card3/DiceBarBorder", diceBarRoot.transform,
                Vector2.zero, Vector2.one);
            diceBarBorder.preserveAspect = false;
            diceBarBorder.raycastTarget = false;

            // Cutouts in 400x100 DiceBarBorder are at exact normalized x coordinates:
            float[] slotMinXs = { 0.0650f, 0.2100f, 0.3600f, 0.5100f, 0.6600f, 0.8075f };
            float[] slotMaxXs = { 0.1875f, 0.3350f, 0.4825f, 0.6350f, 0.7850f, 0.9325f };

            // Layer 2: White die tiles & pips inside the 6 slots
            for (var i = 0; i < 6; i++)
            {
                var minX = slotMinXs[i];
                var maxX = slotMaxXs[i];
                var side = (player.diceSides != null && i < player.diceSides.Length) ? player.diceSides[i] : 1;
                var sidePath = GetDiceSideSpritePath(side);

                // Crisp white square die backing inside cutout
                var tileGo = new GameObject($"SlotTile{i + 1}", typeof(RectTransform), typeof(Image));
                tileGo.transform.SetParent(diceBarRoot.transform, false);
                var tileRect = tileGo.GetComponent<RectTransform>();
                tileRect.anchorMin = new Vector2(minX, 0.08f);
                tileRect.anchorMax = new Vector2(maxX, 0.92f);
                tileRect.offsetMin = tileRect.offsetMax = Vector2.zero;
                var tileImg = tileGo.GetComponent<Image>();
                tileImg.color = Color.white;
                tileImg.raycastTarget = false;

                // Mini die face (dots 1..14 or ability icon >= 16)
                var sideImg = AddResourceImage($"DiceSide{i + 1}", sidePath, tileGo.transform,
                    new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
                sideImg.preserveAspect = true;
                sideImg.raycastTarget = false;
            }

            // Layer 3: Position-colored bar overlay (transparent over slots, tinted over outer frame & dividers)
            var diceBarColour = AddResourceImage("DiceBarColour", "Original/sprites/Card3/DiceBarColour", diceBarRoot.transform,
                Vector2.zero, Vector2.one);
            diceBarColour.color = GetPositionColor(player.position);
            diceBarColour.preserveAspect = false;
            diceBarColour.raycastTarget = false;

            if (isLocked)
            {
                var lockImg = AddResourceImage("Locked", "Original/sprites/PlayerCardBG/CardLocked", border.transform,
                    Vector2.zero, Vector2.one);
                lockImg.preserveAspect = false;
            }
            else if (isHire)
            {
                var hireImg = AddResourceImage("HireMe", "Original/sprites/PlayerCardBG/CardHireMe", border.transform,
                    Vector2.zero, Vector2.one);
                hireImg.preserveAspect = false;
                var costText = AddText($"{player.cost:N0}", hireImg.transform, 22, Ink,
                    new Vector2(0.48f, 0.40f), new Vector2(0.84f, 0.49f));
                costText.alignment = TextAnchor.MiddleCenter;
                costText.fontStyle = FontStyle.Bold;
            }

            if (isInteractive)
            {
                border.raycastTarget = true;
                var btn = root.AddComponent<Button>();
                btn.targetGraphic = border;
                btn.onClick.AddListener(() => onClick?.Invoke());
            }
            else
            {
                border.raycastTarget = false;
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
                if (actionLabel.Contains("HIRE") || isHire)
                {
                    AddSpriteButton("HireBtn", "Original/images/BuyButton", "Original/images/BuyButtonPressed",
                        modalBackdrop.transform, new Vector2(0.5f, 0.18f), new Vector2(0.48f, 0.075f), () =>
                        {
                            Destroy(modalBackdrop.gameObject);
                            onAction.Invoke();
                        });
                }
                else
                {
                    AddSpriteButton("AssignBtn", "Original/sprites/EnemyLineupSS1/AssignPlayerButton",
                        "Original/sprites/EnemyLineupSS1/AssignPlayerButtonPressed",
                        modalBackdrop.transform, new Vector2(0.5f, 0.18f), new Vector2(0.48f, 0.075f), () =>
                        {
                            Destroy(modalBackdrop.gameObject);
                            onAction.Invoke();
                        });
                }
            }

            AddSpriteButton("CloseBtn", "Original/sprites/KitSelectionSS/DoneButton",
                "Original/sprites/KitSelectionSS/DoneButtonPressed", modalBackdrop.transform,
                new Vector2(0.5f, 0.09f), new Vector2(0.36f, 0.065f), () =>
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
            var rowPanel = AddPanel($"PlayerRow_{player.id}", parent, new Color32(28, 38, 46, 245),
                (anchorMin + anchorMax) * 0.5f, anchorMax - anchorMin);

            // Left: Player head preview with position badge and number
            var cardContainer = new GameObject("PlayerHeadContainer", typeof(RectTransform));
            cardContainer.transform.SetParent(rowPanel.transform, false);
            var cardRect = cardContainer.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.015f, 0.08f);
            cardRect.anchorMax = new Vector2(0.18f, 0.92f);
            cardRect.offsetMin = cardRect.offsetMax = Vector2.zero;

            var headBg = AddPanel("HeadBg", cardContainer.transform, GetPositionColor(player.position),
                new Vector2(0.5f, 0.5f), Vector2.one);
            headBg.color = new Color(headBg.color.r, headBg.color.g, headBg.color.b, 0.35f);

            var headPath = ResolvePlayerHeadPath(player.id);
            var headImg = AddResourceImage("Head", headPath, cardContainer.transform,
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
            headImg.preserveAspect = true;
            headImg.raycastTarget = false;

            var posPath = $"Original/sprites/Card6/{player.position}Type";
            var posImg = AddResourceImage("PosBadge", posPath, cardContainer.transform,
                new Vector2(0.55f, 0.55f), new Vector2(1f, 1f));
            posImg.preserveAspect = true;
            posImg.raycastTarget = false;

            var numOutline = AddResourceImage("NumberOutline", "Original/sprites/Card3/PlayerNumberOutline", cardContainer.transform,
                new Vector2(0f, 0f), new Vector2(0.40f, 0.40f));
            numOutline.preserveAspect = true;
            numOutline.raycastTarget = false;

            var dPath = GetPlayerNumberSpritePath(player.id % 10);
            var numImg = AddResourceImage("Digit", dPath, numOutline.transform,
                new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f));
            numImg.preserveAspect = true;
            numImg.raycastTarget = false;

            var cardClickBtn = cardContainer.AddComponent<Button>();
            cardClickBtn.onClick.AddListener(() => onCardClick?.Invoke());

            // Center details
            var centerObj = new GameObject("Details", typeof(RectTransform));
            centerObj.transform.SetParent(rowPanel.transform, false);
            var centerRect = centerObj.GetComponent<RectTransform>();
            centerRect.anchorMin = new Vector2(0.20f, 0.06f);
            centerRect.anchorMax = new Vector2(0.74f, 0.94f);
            centerRect.offsetMin = centerRect.offsetMax = Vector2.zero;

            var titleStr = string.IsNullOrEmpty(headerPrefix)
                ? $"{player.name}"
                : $"{headerPrefix}: {player.name}";
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
                    var tileGo = new GameObject($"SlotTile{i}", typeof(RectTransform), typeof(Image));
                    tileGo.transform.SetParent(diceBarObj.transform, false);
                    var tileRect = tileGo.GetComponent<RectTransform>();
                    tileRect.anchorMin = new Vector2(i * slotW + 0.02f, 0.05f);
                    tileRect.anchorMax = new Vector2((i + 1) * slotW - 0.02f, 0.95f);
                    tileRect.offsetMin = tileRect.offsetMax = Vector2.zero;
                    var tileImg = tileGo.GetComponent<Image>();
                    tileImg.color = Color.white;
                    tileImg.raycastTarget = false;

                    var diceImg = AddResourceImage($"Dice{i}", sidePath, tileGo.transform,
                        new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
                    diceImg.preserveAspect = true;
                    diceImg.raycastTarget = false;
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
                _safeArea, 15, Ink, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.88f));
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
                _safeArea, 14, Ink, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.89f));

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

            // Vertical Soccer Pitch Backdrop (GreenField left normal + right mirrored horizontally)
            var pitchLeft = AddResourceImage("PitchLeft", "Original/images/GreenField", _canvas.transform,
                new Vector2(0f, 0f), new Vector2(0.5f, 0.92f));
            pitchLeft.preserveAspect = false;
            pitchLeft.rectTransform.localScale = Vector3.one;
            pitchLeft.raycastTarget = false;

            var pitchRight = AddResourceImage("PitchRight", "Original/images/GreenField", _canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(1f, 0.92f));
            pitchRight.preserveAspect = false;
            pitchRight.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            pitchRight.raycastTarget = false;

            _safeArea.SetAsLastSibling();
            AddMenuHeader(null, ShowHome);

            var lineup = _catalog.ResolveLineup(_lineupIds);

            // 1-3-3 Formation Positions (matching media_1792421110223_f7c56db8.png):
            // Top row: 3 Forwards (Left #8 Jerry, Center #6 David inside StrikerStar, Right #7 Justin)
            // Middle row: 3 Defenders (Left #4 Sherman, Center #3 Jo, Right #2 Ernest)
            // Bottom row: 1 Goalkeeper (Center #1 Willy)
            var slotPositions = new Vector2[]
            {
                new Vector2(0.500f, 0.205f), // Slot 0: GK (#1 Willy The Kid)
                new Vector2(0.755f, 0.475f), // Slot 1: Right DEF (#2 Ernest Milligan)
                new Vector2(0.500f, 0.475f), // Slot 2: Center DEF (#3 Jo Lee-Roger)
                new Vector2(0.245f, 0.475f), // Slot 3: Left DEF (#4 Sherman Tank)
                new Vector2(0.755f, 0.745f), // Slot 4: Right FWD (#7 Justin Huff)
                new Vector2(0.500f, 0.745f), // Slot 5: Center Striker (#6 David Little, Striker Star)
                new Vector2(0.245f, 0.745f), // Slot 6: Left FWD (#8 Jerry Hat-trick)
            };

            var cardHalfWidth = 0.118f;
            var cardHalfHeight = 0.118f;

            for (var slot = 0; slot < lineup.Length; slot++)
            {
                var capturedSlot = slot;
                var player = lineup[slot];
                var center = slotPositions[slot];

                // If Center Striker (slot 5), add StrikerStar frame behind it; otherwise subtle white card slot border
                if (slot == 5)
                {
                    var strikerStar = AddResourceImage("StrikerStar",
                        "Original/sprites/EnemyLineupSS1/StrikerStar", _safeArea,
                        new Vector2(center.x - cardHalfWidth - 0.018f, center.y - cardHalfHeight - 0.014f),
                        new Vector2(center.x + cardHalfWidth + 0.018f, center.y + cardHalfHeight + 0.038f));
                    strikerStar.preserveAspect = false;
                    strikerStar.raycastTarget = false;
                }
                else
                {
                    var slotBorder = AddResourceImage($"SlotBorder_{slot}",
                        "Original/sprites/PlayerCardBG/CardBorder", _safeArea,
                        new Vector2(center.x - cardHalfWidth - 0.005f, center.y - cardHalfHeight - 0.005f),
                        new Vector2(center.x + cardHalfWidth + 0.005f, center.y + cardHalfHeight + 0.005f));
                    slotBorder.color = new Color(1f, 1f, 1f, 0.45f);
                    slotBorder.preserveAspect = false;
                    slotBorder.raycastTarget = false;
                }

                CreatePlayerCardFront(player, _safeArea,
                    new Vector2(center.x - cardHalfWidth, center.y - cardHalfHeight),
                    new Vector2(center.x + cardHalfWidth, center.y + cardHalfHeight),
                    false, true, () => ShowPlayerPicker(capturedSlot));
            }

            // Left Side: FormationShopButton stacked directly above KitButton (matching media_1792421110223_f7c56db8.png)
            AddSpriteButton("ShopBtn", "Original/sprites/EnemyLineupSS1/FormationShopButton",
                "Original/sprites/EnemyLineupSS1/FormationShopButtonPressed", _safeArea,
                new Vector2(0.14f, 0.17f), new Vector2(0.13f, 0.085f), ShowShop);

            AddSpriteButton("KitBtn", "Original/sprites/EnemyLineupSS1/KitButton",
                "Original/sprites/EnemyLineupSS1/KitButtonPressed", _safeArea,
                new Vector2(0.14f, 0.08f), new Vector2(0.13f, 0.085f), ShowKitSelection);

            // Right Side: ONLY FormationPlayButton ("PLAY!")
            AddSpriteButton("PlayBtn", "Original/sprites/EnemyLineupSS1/FormationPlayButton",
                "Original/sprites/EnemyLineupSS1/FormationPlayButtonPressed", _safeArea,
                new Vector2(0.76f, 0.09f), new Vector2(0.28f, 0.085f), StartCareerMatch);
        }

        private void ShowPlayerPicker(int slot)
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            _selectedLineupSlot = slot;

            ClearCanvas();

            // Background: Sunny stadium with clouds (matching media_1792421110155_320ad894.png)
            AddMenuBackdrop(true);

            // Header: Back arrow (returns to ShowManage), cash, crest (matching media_1792421110155_320ad894.png)
            AddMenuHeader(null, ShowManage);

            // Determine active position tab (0 = FWD, 1 = MID, 2 = DEF, 3 = GK)
            int defaultTab = slot switch
            {
                0 => 3,       // GK
                1 or 2 or 3 => 2, // DEF
                4 or 5 or 6 => 0, // FWD
                _ => 0
            };
            int activeTab = _deckCategoryFilter >= 0 ? _deckCategoryFilter : defaultTab;
            if (activeTab < 0 || activeTab > 3) activeTab = 0;

            string positionKey = activeTab switch
            {
                0 => "FWD",
                1 => "MID",
                2 => "DEF",
                3 => "GK",
                _ => "FWD"
            };

            // 1. Collect candidates for the active position:
            var candidates = new List<PlayerData>();

            // (a) Owned players of this position
            foreach (var pId in _ownedPlayerIds)
            {
                var p = _catalog.GetPlayer(pId);
                if (p != null && p.position == positionKey)
                    candidates.Add(p);
            }

            // (b) Unowned hireable players of this position
            foreach (var p in _catalog.Data.players)
            {
                if (p.id > 0 && p.id < 1000 && p.cost > 0 &&
                    string.IsNullOrWhiteSpace(p.unlockCriteria) &&
                    !_ownedPlayerIds.Contains(p.id) &&
                    p.position == positionKey)
                {
                    candidates.Add(p);
                }
            }

            // (c) Locked players of this position
            foreach (var p in _catalog.Data.players)
            {
                if (p.id > 0 && p.id < 1000 &&
                    (!string.IsNullOrWhiteSpace(p.unlockCriteria) || p.cost <= 0) &&
                    !_ownedPlayerIds.Contains(p.id) &&
                    p.position == positionKey)
                {
                    candidates.Add(p);
                }
            }

            // Sort candidates by Player ID (matching authentic Cocos2d catalog ordering: 5, 13, 21, 29, 37...)
            candidates.Sort((a, b) => a.id.CompareTo(b.id));

            // 2. Pagination: 4 cards per page (2 columns x 2 rows, filled column-by-column)
            const int cardsPerPage = 4;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(candidates.Count / (float)cardsPerPage));
            _deckPage = Mathf.Clamp(_deckPage, 0, totalPages - 1);

            Vector2[] cardCenters = {
                new Vector2(0.30f, 0.62f), // Column 1 Top (matching #5 in media_1792421110155_320ad894.png)
                new Vector2(0.30f, 0.33f), // Column 1 Bottom (matching #13 in media_1792421110155_320ad894.png)
                new Vector2(0.70f, 0.62f), // Column 2 Top (matching #21 in media_1792421110155_320ad894.png)
                new Vector2(0.70f, 0.33f)  // Column 2 Bottom (matching #29 in media_1792421110155_320ad894.png)
            };
            float cardHalfW = 0.180f;
            float cardHalfH = 0.132f;

            int startIdx = _deckPage * cardsPerPage;
            for (int i = 0; i < cardsPerPage && (startIdx + i) < candidates.Count; i++)
            {
                var player = candidates[startIdx + i];
                var capturedPlayer = player;
                var pId = player.id;
                bool isOwned = _ownedPlayerIds.Contains(pId);
                bool isLocked = !isOwned && (!string.IsNullOrWhiteSpace(player.unlockCriteria) || player.cost <= 0);
                bool isHire = !isOwned && !isLocked && player.cost > 0;

                var c = cardCenters[i];
                var min = new Vector2(c.x - cardHalfW, c.y - cardHalfH);
                var max = new Vector2(c.x + cardHalfW, c.y + cardHalfH);

                CreatePlayerCardFront(player, _safeArea, min, max,
                    isHire: isHire, isInteractive: true,
                    onClick: () =>
                    {
                        if (isLocked)
                        {
                            AudioManager.Instance.PlayTap();
                            return;
                        }
                        if (isHire)
                        {
                            if (_careerCash >= capturedPlayer.cost)
                            {
                                _careerCash -= capturedPlayer.cost;
                                _ownedPlayerIds.Add(pId);
                                if (slot >= 0 && IsRoleAllowed(capturedPlayer, slot))
                                {
                                    _lineupIds[slot] = pId;
                                    PlayerPrefs.SetString(LineupPreferenceKey, string.Join(",", _lineupIds));
                                }
                                SaveCareerState();
                                AudioManager.Instance.PlayTransform();
                                if (slot >= 0 && IsRoleAllowed(capturedPlayer, slot))
                                    ShowManage();
                                else
                                    ShowPlayerPicker(slot);
                            }
                            else
                            {
                                AudioManager.Instance.PlayTap();
                            }
                            return;
                        }
                        // Owned player
                        if (slot >= 0)
                        {
                            AssignPlayerToSlot(slot, pId);
                        }
                        else
                        {
                            ShowCardModal(capturedPlayer);
                        }
                    },
                    isLocked: isLocked);
            }

            // 3. Navigation Chevrons on sides (matching media_1792421110155_320ad894.png)
            bool canPrev = _deckPage > 0;
            var leftArrow = AddSpriteButton("LeftArrow",
                canPrev ? "Original/images/LeftArrow" : "Original/images/LeftArrowDisabled",
                canPrev ? "Original/images/LeftArrowSelected" : "Original/images/LeftArrowDisabled",
                _safeArea, new Vector2(0.06f, 0.48f), new Vector2(0.08f, 0.10f),
                canPrev ? () => { _deckPage--; AudioManager.Instance.PlayTap(); ShowPlayerPicker(slot); } : null);
            leftArrow.interactable = canPrev;

            bool canNext = _deckPage < totalPages - 1;
            var rightArrow = AddSpriteButton("RightArrow",
                canNext ? "Original/images/RightArrow" : "Original/images/RightArrowDisabled",
                canNext ? "Original/images/RightArrowSelected" : "Original/images/RightArrowDisabled",
                _safeArea, new Vector2(0.94f, 0.48f), new Vector2(0.08f, 0.10f),
                canNext ? () => { _deckPage++; AudioManager.Instance.PlayTap(); ShowPlayerPicker(slot); } : null);
            rightArrow.interactable = canNext;

            // 4. Authentic Bottom Folder Tabs: FWD (Tab01), MID (Tab02), DEF (Tab03), GK (Tab04)
            string[] tabSprites = { "Tab01", "Tab02", "Tab03", "Tab04" };
            float[] tabXs = { 0.14f, 0.38f, 0.62f, 0.86f };

            for (int t = 0; t < 4; t++)
            {
                int tabIdx = t;
                bool isSelected = (activeTab == tabIdx);
                float tabY = isSelected ? 0.055f : 0.020f; // Active tab is elevated
                string spritePath = $"Original/images/{tabSprites[t]}";
                string pressedPath = $"Original/images/{tabSprites[t]}Pressed";

                AddSpriteButton($"Tab_{t}", spritePath, pressedPath, _safeArea,
                    new Vector2(tabXs[t], tabY), new Vector2(0.22f, 0.10f), () =>
                    {
                        _deckCategoryFilter = tabIdx;
                        _deckPage = 0;
                        AudioManager.Instance.PlayTap();
                        ShowPlayerPicker(slot);
                    });
            }
        }

        private void ShowHiring()
        {
            _catalog ??= GameCatalog.Load();
            EnsureLineupLoaded();
            ClearCanvas();
            AddMenuBackdrop(false);
            AddMenuHeader(null, ShowManage);

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
                AddText("NO PLAYERS AVAILABLE TO HIRE",
                    _safeArea, 20, Ink, new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.58f));
                return;
            }

            _hireCandidateIndex = Mathf.Clamp(_hireCandidateIndex, 0, candidates.Count - 1);
            var candidate = candidates[_hireCandidateIndex];
            var canAfford = _careerCash >= candidate.cost;
            var capturedPlayerId = candidate.id;

            // Centered player card matching post_355.jpg / sec_0433.jpg
            var cardWidth = 0.64f;
            var cardHeight = 0.52f;
            var cardCenter = new Vector2(0.5f, 0.55f);
            CreatePlayerCardFront(candidate, _safeArea,
                cardCenter - new Vector2(cardWidth * 0.5f, cardHeight * 0.5f),
                cardCenter + new Vector2(cardWidth * 0.5f, cardHeight * 0.5f),
                true, true, () => ShowCardModal(candidate));

            // Left & Right Navigation Chevrons
            var canPrev = _hireCandidateIndex > 0;
            var leftArrow = AddSpriteButton("LeftArrow",
                canPrev ? "Original/images/LeftArrow" : "Original/images/LeftArrowDisabled",
                canPrev ? "Original/images/LeftArrowSelected" : "Original/images/LeftArrowDisabled",
                _safeArea, new Vector2(0.10f, 0.55f), new Vector2(0.10f, 0.12f),
                canPrev ? () => { _hireCandidateIndex--; ShowHiring(); } : null);
            leftArrow.interactable = canPrev;

            var canNext = _hireCandidateIndex < candidates.Count - 1;
            var rightArrow = AddSpriteButton("RightArrow",
                canNext ? "Original/images/RightArrow" : "Original/images/RightArrowDisabled",
                canNext ? "Original/images/RightArrowSelected" : "Original/images/RightArrowDisabled",
                _safeArea, new Vector2(0.90f, 0.55f), new Vector2(0.10f, 0.12f),
                canNext ? () => { _hireCandidateIndex++; ShowHiring(); } : null);
            rightArrow.interactable = canNext;

            // Authentic HIRE Button (BuyButton.png)
            var hireBtn = AddSpriteButton("HireBtn", "Original/images/BuyButton",
                "Original/images/BuyButtonPressed", _safeArea,
                new Vector2(0.50f, 0.21f), new Vector2(0.42f, 0.08f),
                canAfford ? () => HirePlayer(capturedPlayerId) : null);
            hireBtn.interactable = canAfford;
            if (!canAfford)
            {
                var hireImg = hireBtn.GetComponent<Image>();
                if (hireImg != null) hireImg.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            }

            // Bottom Footer & Back to Manage
            var footerBar = AddResourceImage("HiringFooter", "Original/images/FooterBG", _safeArea,
                new Vector2(0f, 0f), new Vector2(1f, 0.08f));
            footerBar.preserveAspect = false;
            footerBar.raycastTarget = false;

            AddSpriteButton("BackToManage", "Original/sprites/EnemyLineupSS1/ManageButton",
                "Original/sprites/EnemyLineupSS1/ManageButtonPressed", _safeArea,
                new Vector2(0.5f, 0.04f), new Vector2(0.30f, 0.07f), ShowManage);
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
                if (PlayerPrefs.GetInt("diceSoccer.allUnlocked", 0) != 0)
                {
                    foreach (var p in _catalog.Data.players)
                    {
                        if (p.id > 0 && p.id < 1000 && !_ownedPlayerIds.Contains(p.id))
                            _ownedPlayerIds.Add(p.id);
                    }
                }
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
            var boxYs = new[] { 0.73f, 0.54f, 0.35f };

            for (var i = 0; i < count; i++)
            {
                var option = cashList[startIdx + i];
                var boxY = boxYs[i];

                var box = AddResourceImage($"ShopBox_{i}", "Original/sprites/ShopSS/ShopBox", _safeArea,
                    new Vector2(0.06f, boxY - 0.082f), new Vector2(0.94f, boxY + 0.082f));
                box.preserveAspect = false;

                var iconPath = option.price >= 9.99f
                    ? "Original/sprites/ShopSS/ShopMoneyBar"
                    : "Original/sprites/ShopSS/ShopMoneyPouch";
                var icon = AddResourceImage("CoinIcon", iconPath, box.transform,
                    new Vector2(0.04f, 0.15f), new Vector2(0.32f, 0.85f));
                icon.preserveAspect = true;

                AddText($"{option.coins:N0}\nCOINS", box.transform, 18, Ink,
                    new Vector2(0.32f, 0.15f), new Vector2(0.70f, 0.85f));

                var capturedOption = option;
                AddButton("FREE", box.transform, Grass,
                    new Vector2(0.83f, 0.5f), new Vector2(0.24f, 0.48f), () =>
                    {
                        _careerCash += capturedOption.coins;
                        SaveCareerState();
                        AudioManager.Instance.PlayCash();
                        ShowShop();
                    });
            }

            RenderShopPagination(maxPages, () => ShowShop(), 0.22f);

            AddButton("☕ BUY A COFFEE (PAYPAL)", _safeArea, new Color32(0, 112, 186, 255),
                new Vector2(0.5f, 0.135f), new Vector2(0.75f, 0.055f), ShowDonationModal);
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

        private void RenderShopPagination(int maxPages, UnityEngine.Events.UnityAction onRefresh, float y = 0.175f)
        {
            if (maxPages <= 1) return;

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

            // Background fills safe area (Green lockers)
            var bg = AddResourceImage("KitBG", "Original/images/KitSelectionBG", _safeArea,
                Vector2.zero, Vector2.one);
            bg.preserveAspect = false;

            // Header: Back button, cash, crest (matching media_1792421110173_15334404.png)
            AddMenuHeader(null, () =>
            {
                SaveCareerState();
                AudioManager.Instance.PlayTap();
                ShowManage();
            });

            // Tap crest on header to cycle crest (0.85f to 0.97f)
            AddButton("", _safeArea, Color.clear,
                new Vector2(0.91f, 0.96f), new Vector2(0.12f, 0.08f), () =>
                {
                    _currentCrestId = (_currentCrestId % 3) + 1;
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowKitSelection();
                });

            // ==========================================
            // CENTER: Character Kit Preview (matching media_1792421110173_15334404.png)
            // ==========================================
            var kitBox = new GameObject("CenterKitPreview", typeof(RectTransform));
            kitBox.transform.SetParent(_safeArea, false);
            var kitRect = kitBox.GetComponent<RectTransform>();
            kitRect.anchorMin = new Vector2(185f / 640f, (960f - 527f - 350f) / 960f);
            kitRect.anchorMax = new Vector2((185f + 260f) / 640f, (960f - 527f) / 960f);
            kitRect.offsetMin = Vector2.zero;
            kitRect.offsetMax = Vector2.zero;

            // Layer 1: Shorts
            var shorts = AddResourceImage("Shorts", "Original/sprites/KitSelectionSS/KitSelectionShorts", kitBox.transform,
                Vector2.zero, Vector2.one);
            shorts.preserveAspect = true;
            shorts.color = _catalog.GetColourRgb(_currentPantsId);

            // Layer 2: Shirt Body
            var shirt = AddResourceImage("Shirt", "Original/sprites/KitSelectionSS/KitSelectionShirt", kitBox.transform,
                Vector2.zero, Vector2.one);
            shirt.preserveAspect = true;
            shirt.color = _catalog.GetColourRgb(_currentShirtColorId);

            // Layer 3: Sleeves
            var sleeves = AddResourceImage("Sleeves", "Original/sprites/KitSelectionSS/KitSelectionSleeves", kitBox.transform,
                Vector2.zero, Vector2.one);
            sleeves.preserveAspect = true;
            sleeves.color = _catalog.GetColourRgb(_currentSleeveColorId);

            // Layer 4: Pattern Design (if style > 0)
            if (_currentPatternStyle > 0 && _currentPatternStyle <= 7)
            {
                var pat = AddResourceImage("Pattern", $"Original/sprites/KitSelectionSS/KitSelectionDesign{_currentPatternStyle}", kitBox.transform,
                    Vector2.zero, Vector2.one);
                pat.preserveAspect = true;
                pat.color = _catalog.GetColourRgb(_currentPatternColorId);
            }

            // ==========================================
            // Top Pair of Silver Arrows: Cycle Shirt Presets (chest level: y = 0.38f)
            // ==========================================
            var allShirts = _catalog.Data.shirts ?? Array.Empty<ShirtData>();
            AddSpriteButton("ShirtLeft", "Original/images/LeftArrowDisabled", "Original/images/LeftArrowSelected", _safeArea,
                new Vector2(0.26f, 0.38f), new Vector2(0.10f, 0.10f), () =>
                {
                    if (allShirts.Length > 0)
                    {
                        int currIdx = Array.FindIndex(allShirts, s => s.id == _currentShirtId);
                        if (currIdx < 0) currIdx = 0;
                        int nextIdx = (currIdx - 1 + allShirts.Length) % allShirts.Length;
                        ApplyShirt(allShirts[nextIdx]);
                    }
                });

            AddSpriteButton("ShirtRight", "Original/images/RightArrowDisabled", "Original/images/RightArrowSelected", _safeArea,
                new Vector2(0.74f, 0.38f), new Vector2(0.10f, 0.10f), () =>
                {
                    if (allShirts.Length > 0)
                    {
                        int currIdx = Array.FindIndex(allShirts, s => s.id == _currentShirtId);
                        if (currIdx < 0) currIdx = 0;
                        int nextIdx = (currIdx + 1) % allShirts.Length;
                        ApplyShirt(allShirts[nextIdx]);
                    }
                });

            // ==========================================
            // Bottom Pair of Silver Arrows: Cycle Shorts Colors (hip/shorts level: y = 0.22f)
            // ==========================================
            var allColours = _catalog.Data.colours ?? Array.Empty<ColourData>();
            AddSpriteButton("ShortsLeft", "Original/images/LeftArrowDisabled", "Original/images/LeftArrowSelected", _safeArea,
                new Vector2(0.26f, 0.22f), new Vector2(0.10f, 0.10f), () =>
                {
                    if (allColours.Length > 0)
                    {
                        int currIdx = Array.FindIndex(allColours, c => c.id == _currentPantsId);
                        if (currIdx < 0) currIdx = 0;
                        int nextIdx = (currIdx - 1 + allColours.Length) % allColours.Length;
                        _currentPantsId = allColours[nextIdx].id;
                        SaveCareerState();
                        AudioManager.Instance.PlayTap();
                        ShowKitSelection();
                    }
                });

            AddSpriteButton("ShortsRight", "Original/images/RightArrowDisabled", "Original/images/RightArrowSelected", _safeArea,
                new Vector2(0.74f, 0.22f), new Vector2(0.10f, 0.10f), () =>
                {
                    if (allColours.Length > 0)
                    {
                        int currIdx = Array.FindIndex(allColours, c => c.id == _currentPantsId);
                        if (currIdx < 0) currIdx = 0;
                        int nextIdx = (currIdx + 1) % allColours.Length;
                        _currentPantsId = allColours[nextIdx].id;
                        SaveCareerState();
                        AudioManager.Instance.PlayTap();
                        ShowKitSelection();
                    }
                });

            // ==========================================
            // Bottom Right: Authentic Done Button (DoneButton.png)
            // ==========================================
            AddSpriteButton("DoneBtn", "Original/sprites/KitSelectionSS/DoneButton",
                "Original/sprites/KitSelectionSS/DoneButtonPressed", _safeArea,
                new Vector2(0.78f, 0.08f), new Vector2(0.24f, 0.065f), () =>
                {
                    SaveCareerState();
                    AudioManager.Instance.PlayTap();
                    ShowManage();
                });
        }

        private void ApplyShirt(ShirtData s)
        {
            _currentShirtId = s.id;
            _currentShirtColorId = s.shirtColour;
            _currentSleeveColorId = s.sleeveColour;
            _currentPatternStyle = s.shirtStyle;
            _currentPatternColorId = s.styleColour;
            SaveCareerState();
            AudioManager.Instance.PlayTap();
            ShowKitSelection();
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

                    if (unlocked)
                    {
                        var trophyImg = AddResourceImage("TrophyIcon", $"Original/images/Trophy{trophyId:00}", slotBtn.transform,
                            new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f));
                        trophyImg.preserveAspect = true;
                        trophyImg.raycastTarget = false;
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

            var trophy = AddResourceImage("Trophy", $"Original/images/Trophy{achievementId:00}", box.transform,
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
                return $"Original/sprites/EntourageFullFigure2/EntourageFullFigure{id:00}";
            return $"Original/sprites/EntourageFullFigure1/EntourageFullFigure{id:00}";
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
            if (_shakePrompt != null) _shakePrompt.gameObject.SetActive(false);
            if (_buffBeltGo != null)
            {
                _buffBeltGo.SetActive(true);
                if (_buffBeltText != null) _buffBeltText.text = "ROLLING THE DICE...";
            }
            _matchReport.text = "Rolling the dice...";

            var loc = _match.Location;
            var (activeHome, activeAway) = GetActiveDuelSlots(loc);
            float[] ys = { 0.25f, 0.50f, 0.75f };

            // 1. Poof smoke on active players before rolling
            for (var i = 0; i < activeHome.Length; i++)
            {
                var p = _homePlayers[activeHome[i]];
                if (p != null) StartCoroutine(PlayPoofAtPosition(new Vector2(p.rectTransform.anchorMin.x, p.rectTransform.anchorMin.y + 0.12f)));
            }
            for (var i = 0; i < activeAway.Length; i++)
            {
                var p = _awayPlayers[activeAway[i]];
                if (p != null) StartCoroutine(PlayPoofAtPosition(new Vector2(p.rectTransform.anchorMin.x, p.rectTransform.anchorMin.y + 0.12f)));
            }
            yield return new WaitForSeconds(0.12f);

            // 2. Hide active players, show dice roots at matching positions (matching media_1792421110113_45d0305f.png)
            for (var i = 0; i < activeHome.Length; i++) _homePlayers[activeHome[i]].gameObject.SetActive(false);
            for (var i = 0; i < activeAway.Length; i++) _awayPlayers[activeAway[i]].gameObject.SetActive(false);

            if (_homeDieRoots != null && _awayDieRoots != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (i < homeFaces.Length && _homeDieRoots[i] != null)
                    {
                        _homeDieRoots[i].SetActive(true);
                        float y = homeFaces.Length == 1 ? 0.50f : ys[i];
                        float x = homeFaces.Length == 1 ? 0.35f : 0.31f;
                        _homeDieRoots[i].GetComponent<RectTransform>().anchorMin =
                            _homeDieRoots[i].GetComponent<RectTransform>().anchorMax = new Vector2(x, y);
                        if (_homeDice[i] != null) _homeDice[i].enabled = false;
                    }
                    else if (_homeDieRoots[i] != null) _homeDieRoots[i].SetActive(false);

                    if (i < awayFaces.Length && _awayDieRoots[i] != null)
                    {
                        _awayDieRoots[i].SetActive(true);
                        float y = awayFaces.Length == 1 ? 0.50f : ys[i];
                        float x = awayFaces.Length == 1 ? 0.65f : 0.70f;
                        _awayDieRoots[i].GetComponent<RectTransform>().anchorMin =
                            _awayDieRoots[i].GetComponent<RectTransform>().anchorMax = new Vector2(x, y);
                        if (_awayDice[i] != null) _awayDice[i].enabled = false;
                    }
                    else if (_awayDieRoots[i] != null) _awayDieRoots[i].SetActive(false);
                }

                // 3. Tumble rolling dice animation (DiceRoll01..DiceRoll13)
                AudioManager.Instance.PlayDiceRoll();
                for (var frame = 1; frame <= 14; frame++)
                {
                    int rollIdx = ((frame - 1) % 13) + 1;
                    var rollSprite = GetUiSprite($"Original/sprites/PlayerBody3/DiceRoll{rollIdx:00}");
                    for (var i = 0; i < homeFaces.Length; i++)
                        if (_homeDieRoots[i] != null) _homeDieRoots[i].GetComponent<Image>().sprite = rollSprite;
                    for (var i = 0; i < awayFaces.Length; i++)
                        if (_awayDieRoots[i] != null) _awayDieRoots[i].GetComponent<Image>().sprite = rollSprite;
                    yield return new WaitForSeconds(0.055f);
                }

                // 4. Settle dice on final faces with impact
                AudioManager.Instance.PlayTap();
                var diceBaseSprite = GetUiSprite("Original/sprites/Buff6/DiceBackground");
                for (var i = 0; i < homeFaces.Length; i++)
                {
                    if (_homeDieRoots[i] != null) _homeDieRoots[i].GetComponent<Image>().sprite = diceBaseSprite;
                    if (_homeDice[i] != null) SetDieFace(_homeDice[i], homeFaces[i]);
                }
                for (var i = 0; i < awayFaces.Length; i++)
                {
                    if (_awayDieRoots[i] != null) _awayDieRoots[i].GetComponent<Image>().sprite = diceBaseSprite;
                    if (_awayDice[i] != null) SetDieFace(_awayDice[i], awayFaces[i]);
                }
            }

            // SUSPENSE HOLD: Keep dice visible so player can manually count dots and feel suspense!
            _matchReport.text = "Counting the dice...";
            if (_buffBeltText != null) _buffBeltText.text = "COUNTING THE DICE...";
            yield return new WaitForSeconds(1.20f);

            var oldHomeScore = _match.HomeScore;
            var oldAwayScore = _match.AwayScore;
            var oldLocation = _match.Location;
            var oldPossession = _match.Possession;

            var report = _match.CompleteEncounter();
            report = CompactMatchReport(report, _match.ClockText());

            // 5. Reveal duel totals with punch impact (matching media_1792421110113_45d0305f.png)
            AudioManager.Instance.PlayTap();
            if (_homeTotalText != null && _awayTotalText != null)
            {
                _homeTotalText.text = _match.LastHomeTotal.ToString();
                _awayTotalText.text = _match.LastAwayTotal.ToString();

                Vector2 hTotPos = new Vector2(0.18f, 0.20f);
                Vector2 aTotPos = new Vector2(0.83f, 0.20f);

                _homeTotalText.rectTransform.anchorMin = _homeTotalText.rectTransform.anchorMax = hTotPos;
                _awayTotalText.rectTransform.anchorMin = _awayTotalText.rectTransform.anchorMax = aTotPos;
                _homeTotalText.rectTransform.anchoredPosition = Vector2.zero;
                _awayTotalText.rectTransform.anchoredPosition = Vector2.zero;

                _homeTotalText.color = new Color32(50, 45, 40, 255);
                _awayTotalText.color = new Color32(50, 45, 40, 255);

                _homeTotalText.enabled = true;
                _awayTotalText.enabled = true;
            }

            _matchScore.text = $"{_match.HomeScore}   -   {_match.AwayScore}";
            _matchClock.text = _match.ClockText();
            _matchReport.text = report;
            if (_buffBeltText != null) _buffBeltText.text = report;

            // Pause on revealed totals for dramatic outcome appreciation
            yield return new WaitForSeconds(0.80f);

            // 6. Check for ability triggers (e.g. Critical Striker on 3, Pair, etc.)
            bool hasPair = false;
            if (homeFaces.Length >= 2)
            {
                for (int a = 0; a < homeFaces.Length; a++)
                    for (int b = a + 1; b < homeFaces.Length; b++)
                        if (homeFaces[a] == homeFaces[b] && homeFaces[a] > 0)
                            hasPair = true;
            }
            int threeIdx = Array.IndexOf(homeFaces, 3);
            if (threeIdx >= 0 && _homeDieRoots != null && threeIdx < _homeDieRoots.Length && _homeDieRoots[threeIdx] != null)
            {
                Vector2 burstPos = _homeDieRoots[threeIdx].GetComponent<RectTransform>().anchorMin;
                yield return StartCoroutine(PlayAbilityBurst(burstPos, "Original/sprites/BuffTrigger/Trigger21", "Critical Striker"));
                yield return new WaitForSeconds(0.85f);
            }
            else if (hasPair && _homeDieRoots != null && _homeDieRoots[0] != null)
            {
                Vector2 burstPos = _homeDieRoots[0].GetComponent<RectTransform>().anchorMin;
                yield return StartCoroutine(PlayAbilityBurst(burstPos, "Original/sprites/BuffTrigger1/Trigger2", null));
                yield return new WaitForSeconds(0.85f);
            }
            else
            {
                yield return new WaitForSeconds(0.35f);
            }

            // 7. Smoke poof on dice before hiding them
            if (_homeDieRoots != null && _awayDieRoots != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (_homeDieRoots[i] != null && _homeDieRoots[i].activeSelf)
                        StartCoroutine(PlayPoofAtPosition(_homeDieRoots[i].GetComponent<RectTransform>().anchorMin));
                    if (_awayDieRoots[i] != null && _awayDieRoots[i].activeSelf)
                        StartCoroutine(PlayPoofAtPosition(_awayDieRoots[i].GetComponent<RectTransform>().anchorMin));
                }
            }
            yield return new WaitForSeconds(0.08f);

            // Hide dice, totals, and buff belt
            if (_homeDieRoots != null && _awayDieRoots != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (_homeDieRoots[i] != null) _homeDieRoots[i].SetActive(false);
                    if (_awayDieRoots[i] != null) _awayDieRoots[i].SetActive(false);
                }
            }
            if (_homeTotalText != null) _homeTotalText.enabled = false;
            if (_awayTotalText != null) _awayTotalText.enabled = false;
            if (_buffBeltGo != null) _buffBeltGo.SetActive(false);

            // Restore duel players on the pitch before shot/resolution
            AudioManager.Instance.PlayTransform();
            UpdatePlayerPositions(oldLocation);

            var scored = _match.HomeScore > oldHomeScore || _match.AwayScore > oldAwayScore || report.Contains("GOAL");
            if (scored)
            {
                bool homeScored = _match.HomeScore > oldHomeScore;
                Image striker = homeScored ? _homePlayers[5] : _awayPlayers[5];
                Image goalie = homeScored ? _awayPlayers[0] : _homePlayers[0];

                // Striker kick lunge animation + PlayKick()
                AudioManager.Instance.PlayKick();
                if (striker != null)
                {
                    float kickDir = homeScored ? 28f : -28f;
                    striker.rectTransform.anchoredPosition = new Vector2(kickDir, 0f);
                }

                // Animate ball shooting into the goal net with arc and rapid spin
                var ballStart = _ballMarker != null ? _ballMarker.rectTransform.anchorMin : (homeScored ? new Vector2(0.435f, 0.418f) : new Vector2(0.565f, 0.418f));
                var ballTarget = homeScored ? new Vector2(0.78f, 0.50f) : new Vector2(0.22f, 0.50f);
                yield return StartCoroutine(SmoothPassBall(ballStart, ballTarget, 0.24f, true));

                // Goalkeeper reacts defeated
                if (goalie != null)
                {
                    float fallDir = homeScored ? 25f : -25f;
                    goalie.rectTransform.anchoredPosition = new Vector2(fallDir, -10f);
                }
                if (striker != null) striker.rectTransform.anchoredPosition = Vector2.zero;

                yield return new WaitForSeconds(0.18f);

                if (goalie != null) goalie.rectTransform.anchoredPosition = Vector2.zero;

                // Authentic GOAL celebration animation
                yield return StartCoroutine(PlayGoalAnimation(homeScored));

                // Shift field smoothly back to midfield (0)
                yield return StartCoroutine(SmoothShiftFieldAndBall(oldLocation, 0, _match.Possession));
                UpdatePlayerPositions();
                yield return StartCoroutine(PlayKickOffAnimation());
            }
            else if (report.Contains("SAVED"))
            {
                bool homeShooting = oldLocation == 2;
                Image striker = homeShooting ? _homePlayers[5] : _awayPlayers[5];
                Image goalie = homeShooting ? _awayPlayers[0] : _homePlayers[0];

                AudioManager.Instance.PlayKick();
                if (striker != null)
                {
                    float kickDir = homeShooting ? 20f : -20f;
                    striker.rectTransform.anchoredPosition = new Vector2(kickDir, 0f);
                }

                // Ball shoots towards goalkeeper
                var ballStart = _ballMarker != null ? _ballMarker.rectTransform.anchorMin : (homeShooting ? new Vector2(0.435f, 0.418f) : new Vector2(0.565f, 0.418f));
                var ballTarget = homeShooting ? new Vector2(0.54f, 0.45f) : new Vector2(0.46f, 0.45f);
                yield return StartCoroutine(SmoothPassBall(ballStart, ballTarget, 0.20f, false));

                if (striker != null) striker.rectTransform.anchoredPosition = Vector2.zero;
                if (goalie != null)
                {
                    float diveDir = homeShooting ? -15f : 15f;
                    goalie.rectTransform.anchoredPosition = new Vector2(diveDir, 0f);
                }

                yield return StartCoroutine(PlaySaveAnimation());

                if (goalie != null) goalie.rectTransform.anchoredPosition = Vector2.zero;

                // Pitch shifts smoothly to cleared half
                yield return StartCoroutine(SmoothShiftFieldAndBall(oldLocation, _match.Location, _match.Possession));
                UpdatePlayerPositions();
            }
            else
            {
                // Outfield duel resolution
                if (report.Contains("TIE") || report.Contains("Tie") || report.Contains("Win on Tie"))
                {
                    yield return StartCoroutine(PlayTieAnimation());
                }
                else if (report.Contains("Through Pass"))
                {
                    yield return StartCoroutine(PlayThroughPassAnimation(_match.Possession == 0));
                }
                else if (oldPossession != _match.Possession)
                {
                    yield return StartCoroutine(PlayPossessionChangeAnimation(_match.Possession == 0));
                }
                else
                {
                    AudioManager.Instance.PlayTap();
                }

                // Smoothly shift field and ball to new location
                if (oldLocation != _match.Location)
                {
                    // If transitioning to 1v1, activate the duelists so they run smoothly onto the pitch
                    if (Mathf.Abs(_match.Location) == 2)
                    {
                        UpdatePlayerPositions(_match.Location);
                    }
                    yield return StartCoroutine(SmoothShiftFieldAndBall(oldLocation, _match.Location, _match.Possession));
                    UpdatePlayerPositions();
                }
                else
                {
                    var fromPos = _ballMarker != null ? _ballMarker.rectTransform.anchorMin : GetBallPosition(oldLocation, oldPossession);
                    var toPos = GetBallPosition(_match.Location, _match.Possession, _match.BallHolderSlotIndex);
                    yield return StartCoroutine(SmoothPassBall(fromPos, toPos, 0.32f, false));
                }
            }

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
                if (_shakePrompt != null) _shakePrompt.gameObject.SetActive(true);
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

        private IEnumerator PlayAbilityBurst(Vector2 anchorPos, string triggerBadgePath, string abilityName)
        {
            if (_field == null) yield break;

            AudioManager.Instance.PlayPowerup(1);
            AudioManager.Instance.PlayBuff();

            var burstContainer = new GameObject("AbilityBurstContainer", typeof(RectTransform));
            burstContainer.transform.SetParent(_field, false);
            var containerRect = burstContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = containerRect.anchorMax = anchorPos;
            containerRect.anchoredPosition = new Vector2(0f, 30f);
            burstContainer.transform.SetAsLastSibling();

            try
            {
                // 1. Ray burst background (TriggerBurst01..09)
                var rayGo = new GameObject("RayBurst", typeof(RectTransform), typeof(Image));
                rayGo.transform.SetParent(burstContainer.transform, false);
                var rayRect = rayGo.GetComponent<RectTransform>();
                rayRect.sizeDelta = new Vector2(220f, 220f);
                rayRect.anchoredPosition = Vector2.zero;
                var rayImg = rayGo.GetComponent<Image>();
                rayImg.preserveAspect = true;
                rayImg.raycastTarget = false;

                // 2. Trigger badge (e.g. Trigger21 "3*" or Trigger2 "PAIR!")
                var badgeGo = new GameObject("TriggerBadge", typeof(RectTransform), typeof(Image));
                badgeGo.transform.SetParent(burstContainer.transform, false);
                var badgeRect = badgeGo.GetComponent<RectTransform>();
                badgeRect.sizeDelta = new Vector2(160f, 130f);
                badgeRect.anchoredPosition = new Vector2(0f, 10f);
                var badgeImg = badgeGo.GetComponent<Image>();
                var badgeSprite = TryGetUiSprite(triggerBadgePath);
                if (badgeSprite != null) badgeImg.sprite = badgeSprite;
                badgeImg.preserveAspect = true;
                badgeImg.raycastTarget = false;

                // 3. Ribbon banner (e.g. AbilityName-Critical Striker)
                Image ribbonImg = null;
                if (!string.IsNullOrEmpty(abilityName))
                {
                    var ribbonSprite = TryGetUiSprite($"Original/sprites/BuffTrigger/AbilityName-{abilityName}")
                                    ?? TryGetUiSprite($"Original/sprites/BuffTrigger1/AbilityName-{abilityName}");
                    if (ribbonSprite != null)
                    {
                        var ribbonGo = new GameObject("AbilityRibbon", typeof(RectTransform), typeof(Image));
                        ribbonGo.transform.SetParent(burstContainer.transform, false);
                        var ribbonRect = ribbonGo.GetComponent<RectTransform>();
                        ribbonRect.sizeDelta = new Vector2(240f, 48f);
                        ribbonRect.anchoredPosition = new Vector2(0f, -45f);
                        ribbonImg = ribbonGo.GetComponent<Image>();
                        ribbonImg.sprite = ribbonSprite;
                        ribbonImg.preserveAspect = true;
                        ribbonImg.raycastTarget = false;
                    }
                }

                // Animate ray burst cycling + badge punch
                float elapsed = 0f;
                const float duration = 0.65f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);

                    // Ray burst animation frames (TriggerBurst01..09)
                    int burstFrame = Mathf.Clamp((int)(t * 9f) + 1, 1, 9);
                    var bSprite = TryGetUiSprite($"Original/sprites/BuffTrigger/TriggerBurst{burstFrame:00}");
                    if (bSprite != null) rayImg.sprite = bSprite;

                    // Badge scale punch
                    float badgeScale = 1f + Mathf.Sin(t * Mathf.PI) * 0.28f;
                    badgeRect.localScale = new Vector3(badgeScale, badgeScale, 1f);

                    if (ribbonImg != null)
                    {
                        float ribbonScale = Mathf.Clamp01(t * 3f);
                        ribbonImg.rectTransform.localScale = new Vector3(ribbonScale, ribbonScale, 1f);
                    }

                    yield return null;
                }
            }
            finally
            {
                if (burstContainer != null)
                    Destroy(burstContainer);
            }
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
            StartCoroutine(PlayPoofAtPosition(new Vector2(0.5f, 0.55f)));
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

        private IEnumerator PlayPossessionChangeAnimation(bool homeGainedPossession)
        {
            // 1. Identify tackling player
            var (activeHome, activeAway) = GetActiveDuelSlots(_match != null ? _match.Location : 0);
            Image tackler = null;
            float lungeDir = homeGainedPossession ? 22f : -22f;

            int winnerSlot = _match != null ? _match.BallHolderSlotIndex : 1;

            if (homeGainedPossession && activeHome.Length > 0 && _homePlayers != null)
            {
                int slotIdx = Mathf.Clamp(winnerSlot, 0, activeHome.Length - 1);
                int slot = activeHome[slotIdx];
                if (slot >= 0 && slot < _homePlayers.Length) tackler = _homePlayers[slot];
            }
            else if (!homeGainedPossession && activeAway.Length > 0 && _awayPlayers != null)
            {
                int slotIdx = Mathf.Clamp(winnerSlot, 0, activeAway.Length - 1);
                int slot = activeAway[slotIdx];
                if (slot >= 0 && slot < _awayPlayers.Length) tackler = _awayPlayers[slot];
            }

            // 2. Tackler lunges forward and dispossesses opponent
            AudioManager.Instance.PlayKick();
            if (tackler != null)
            {
                tackler.rectTransform.anchoredPosition = new Vector2(lungeDir, 0f);
            }

            // 3. Ball smoothly slides & rolls to winning player's feet
            int loc = _match != null ? _match.Location : 0;
            Vector2 fromPos = _ballMarker != null ? _ballMarker.rectTransform.anchorMin : GetBallPosition(loc, homeGainedPossession ? 1 : 0);
            Vector2 toPos = GetBallPosition(loc, homeGainedPossession ? 0 : 1, winnerSlot);
            yield return StartCoroutine(SmoothPassBall(fromPos, toPos, 0.32f, false));

            // 4. Tackler recovers to normal stance
            if (tackler != null)
            {
                tackler.rectTransform.anchoredPosition = Vector2.zero;
            }

            // 5. Possession change banner animation
            if (_overlayContainer != null)
            {
                var path = homeGainedPossession
                    ? "Original/sprites/Prompt1/PossessionChangeGreen"
                    : "Original/sprites/Prompt1/PossessionChangeRed";
                var banner = AddResourceImage("PossessionChangeBanner", path, _overlayContainer,
                    new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f));
                banner.rectTransform.sizeDelta = new Vector2(440f, 240f);
                banner.preserveAspect = true;

                if (homeGainedPossession)
                    AudioManager.Instance.PlayWhistle();
                else
                    AudioManager.Instance.PlayDisappoint();

                var elapsed = 0f;
                while (elapsed < 0.22f)
                {
                    elapsed += Time.deltaTime;
                    banner.rectTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 1.05f, elapsed / 0.22f);
                    yield return null;
                }
                elapsed = 0f;
                while (elapsed < 0.10f)
                {
                    elapsed += Time.deltaTime;
                    banner.rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.05f, Vector3.one, elapsed / 0.10f);
                    yield return null;
                }
                yield return new WaitForSeconds(0.35f);
                elapsed = 0f;
                var col = banner.color;
                while (elapsed < 0.22f)
                {
                    elapsed += Time.deltaTime;
                    banner.color = new Color(col.r, col.g, col.b, 1f - elapsed / 0.22f);
                    yield return null;
                }
                if (banner != null) Destroy(banner.gameObject);
            }
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

            if (_isLocalMultiplayer)
            {
                var p1Won = _match.HomeScore > _match.AwayScore;
                var p2Won = _match.AwayScore > _match.HomeScore;
                var isDraw = _match.HomeScore == _match.AwayScore;

                if (p1Won || p2Won)
                {
                    AudioManager.Instance.PlayMedalCeremonyLong();
                    AudioManager.Instance.PlayEffect("KidsCheering", 1f, 1f);
                }
                else
                {
                    AudioManager.Instance.PlayEffect("ChildrenAaaah", 1f, 0.9f);
                }

                var mpBackdrop = AddPanel("GameOverBackdrop", _overlayContainer,
                    new Color(0f, 0f, 0f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.one);
                mpBackdrop.raycastTarget = true;

                var mpBox = AddResourceImage("GameOverBox",
                    isDraw ? "Original/sprites/GameOver/GameOverDrawBox" : "Original/sprites/GameOver/GameOverVictoryBox",
                    mpBackdrop.transform,
                    new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f));
                mpBox.rectTransform.sizeDelta = new Vector2(390f, 560f);
                mpBox.preserveAspect = true;

                StartCoroutine(PopInBounceAnimation(mpBox.rectTransform));

                var winnerText = isDraw
                    ? "IT'S A DRAW!"
                    : (p1Won ? "PLAYER 1 (RED) WINS!" : "PLAYER 2 (BLUE) WINS!");
                var winnerColor = isDraw ? (Color)Paper : (Color)(p1Won ? new Color32(235, 60, 60, 255) : new Color32(60, 140, 235, 255));

                var mpTitle = AddText(winnerText, mpBox.transform, 22, winnerColor,
                    new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.94f));
                mpTitle.fontStyle = FontStyle.Bold;
                mpTitle.alignment = TextAnchor.MiddleCenter;

                var scoreText = AddText($"P1 (RED)  {_match.HomeScore} - {_match.AwayScore}  P2 (BLUE)", mpBox.transform, 24, Paper,
                    new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.78f));
                scoreText.fontStyle = FontStyle.Bold;
                scoreText.alignment = TextAnchor.MiddleCenter;

                var subTitle = AddText(isDraw ? "Great battle between both managers!" : (p1Won ? "Player 1 takes the victory!" : "Player 2 takes the victory!"),
                    mpBox.transform, 14, Ink,
                    new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.62f));
                subTitle.alignment = TextAnchor.MiddleCenter;

                // Rematch button
                AddButton("⚔ REMATCH (1v1)", mpBox.transform, Grass,
                    new Vector2(0.5f, 0.36f), new Vector2(0.75f, 0.09f), () =>
                    {
                        StartLocalMultiplayerMatch();
                    });

                // Main menu button
                AddButton("MAIN MENU", mpBox.transform, new Color32(68, 79, 84, 255),
                    new Vector2(0.5f, 0.22f), new Vector2(0.75f, 0.08f), () =>
                    {
                        _isLocalMultiplayer = false;
                        ShowHome();
                    });

                if (p1Won || p2Won)
                {
                    for (var i = 0; i < 24; i++)
                    {
                        var cId = UnityEngine.Random.Range(1, 4);
                        var x = UnityEngine.Random.Range(0.05f, 0.95f);
                        var y = UnityEngine.Random.Range(0.40f, 0.95f);
                        var cImg = AddResourceImage($"VictoryConfetti{i}",
                            $"Original/sprites/ConfettiSS/Confetti{cId}", mpBackdrop.transform,
                            new Vector2(x, y), new Vector2(x, y));
                        cImg.rectTransform.sizeDelta = new Vector2(22f, 22f);
                        cImg.rectTransform.localEulerAngles = new Vector3(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                        cImg.raycastTarget = false;
                    }
                }
                return;
            }

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

            var box = AddResourceImage("PauseBox", "Original/sprites/AchievementsSS4/LongDialogBox", backdrop.transform,
                new Vector2(0.5f, 0.50f), new Vector2(0.5f, 0.50f));
            box.rectTransform.sizeDelta = new Vector2(380f, 580f);
            box.preserveAspect = true;

            // Paused header banner:
            var header = AddResourceImage("PausedHeader", "Original/images/Paused", box.transform,
                new Vector2(0.5f, 0.83f), new Vector2(0.5f, 0.83f));
            header.rectTransform.sizeDelta = new Vector2(240f, 55f);
            header.preserveAspect = true;

            // 1. Resume Button:
            AddSpriteButton("Resume", "Original/images/ResumeButton",
                "Original/images/ResumeButtonPressed", box.transform,
                new Vector2(0.5f, 0.69f), new Vector2(0.68f, 0.12f),
                () => Destroy(backdrop.gameObject));

            // 2. Substitution Button:
            var subBtn = AddSpriteButton("Substitution", "Original/images/SubstitutionButton",
                "Original/images/SubstitutionButtonPressed", box.transform,
                new Vector2(0.5f, 0.53f), new Vector2(0.68f, 0.12f),
                () => {
                    if (_remainingSubs > 0 && _ownedPlayerIds.Count > 7)
                    {
                        Destroy(backdrop.gameObject);
                        ShowSubstitutionSlots();
                    }
                });
            if (_remainingSubs <= 0 || _ownedPlayerIds.Count <= 7)
                subBtn.interactable = false;

            AddText($"{_remainingSubs} SUBS LEFT", box.transform, 13, Ink,
                new Vector2(0.15f, 0.42f), new Vector2(0.85f, 0.48f));

            // 3. Pause Options Button:
            AddSpriteButton("PauseOptions", "Original/images/PauseOptionsButton",
                "Original/images/PauseOptionsButtonPressed", box.transform,
                new Vector2(0.5f, 0.31f), new Vector2(0.65f, 0.12f),
                () => ShowOptionsDialog(backdrop.transform));

            // 4. Forfeit Button:
            AddSpriteButton("Forfeit", "Original/images/ForfeitButton",
                "Original/images/ForfeitButtonPressed", box.transform,
                new Vector2(0.5f, 0.16f), new Vector2(0.65f, 0.12f),
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

            var box = AddResourceImage("OptionsBox", "Original/sprites/AchievementsSS4/LongDialogBox", optionsBackdrop.transform,
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

            var box = AddResourceImage("HandbookBox", "Original/sprites/AchievementsSS4/LongDialogBox", hbBackdrop.transform,
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

            var box = AddResourceImage("CreditsBox", "Original/sprites/AchievementsSS4/LongDialogBox", crBackdrop.transform,
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

                var box = AddResourceImage("TutorialBox", "Original/sprites/AchievementsSS4/LongDialogBox", backdropGo.transform,
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
            if (_isLocalMultiplayer)
            {
                _lastPayout = 0;
                return;
            }

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
            _currentCrestId = Mathf.Clamp(PlayerPrefs.GetInt("diceSoccer.currentCrest", 1), 1, 3);
            _currentPatternStyle = Mathf.Clamp(PlayerPrefs.GetInt("diceSoccer.currentPatternStyle", 0), 0, 7);
            _currentShirtColorId = Mathf.Max(1, PlayerPrefs.GetInt("diceSoccer.currentShirtColor", 1));
            _currentPatternColorId = Mathf.Max(1, PlayerPrefs.GetInt("diceSoccer.currentPatternColor", 10));
            _currentSleeveColorId = Mathf.Max(1, PlayerPrefs.GetInt("diceSoccer.currentSleeveColor", 1));

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
            PlayerPrefs.SetInt("diceSoccer.currentCrest", _currentCrestId);
            PlayerPrefs.SetInt("diceSoccer.currentPatternStyle", _currentPatternStyle);
            PlayerPrefs.SetInt("diceSoccer.currentShirtColor", _currentShirtColorId);
            PlayerPrefs.SetInt("diceSoccer.currentPatternColor", _currentPatternColorId);
            PlayerPrefs.SetInt("diceSoccer.currentSleeveColor", _currentSleeveColorId);
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
            var trophy = AddResourceImage("Trophy", $"Original/images/Trophy{trophyId:00}", box.transform,
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
                faceRect.anchorMin = new Vector2(0.12f, 0.12f);
                faceRect.anchorMax = new Vector2(0.88f, 0.88f);
                faceRect.offsetMin = faceRect.offsetMax = Vector2.zero;
                dice[i] = faceObject.GetComponent<Image>();
                dice[i].preserveAspect = true;
                dice[i].enabled = false;
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
            image.color = Color.white;
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

        private static float GetPitchOffsetForLocation(int location)
        {
            switch (location)
            {
                case -2: return 640f;
                case -1: return 375f;
                case 0:  return 0f;
                case 1:  return -375f;
                case 2:  return -640f;
                default: return 0f;
            }
        }

        private static (int[] homeSlots, int[] awaySlots) GetActiveDuelSlots(int location)
        {
            switch (location)
            {
                case -2: return (new[] { 0 }, new[] { 5 }); // Home GK vs Away CF
                case -1: return (new[] { 1, 2, 3 }, new[] { 4, 5, 6 }); // Home Def vs Away Att
                case 0:  return (new[] { 4, 5, 6 }, new[] { 4, 5, 6 }); // Home Mid vs Away Mid
                case 1:  return (new[] { 4, 5, 6 }, new[] { 1, 2, 3 }); // Home Att vs Away Def
                case 2:  return (new[] { 5 }, new[] { 0 }); // Home CF vs Away GK
                default: return (new[] { 4, 5, 6 }, new[] { 4, 5, 6 });
            }
        }

        private void CreateFieldDice(Transform parent)
        {
            _homeDice = new Image[3];
            _awayDice = new Image[3];
            _homeDieRoots = new GameObject[3];
            _awayDieRoots = new GameObject[3];

            float[] ys = { 0.25f, 0.50f, 0.75f };

            for (var i = 0; i < 3; i++)
            {
                // Home Die Root (matching media_1792421110113_45d0305f.png):
                var hRoot = new GameObject($"HomeDie{i + 1}", typeof(RectTransform), typeof(Image));
                hRoot.transform.SetParent(parent, false);
                var hRect = hRoot.GetComponent<RectTransform>();
                hRect.anchorMin = hRect.anchorMax = new Vector2(0.31f, ys[i]);
                hRect.sizeDelta = new Vector2(92f, 92f);
                var hImg = hRoot.GetComponent<Image>();
                hImg.sprite = GetUiSprite("Original/sprites/Buff6/DiceBackground");
                hImg.preserveAspect = true;
                hImg.raycastTarget = false;

                var hFaceGo = new GameObject("Face", typeof(RectTransform), typeof(Image));
                hFaceGo.transform.SetParent(hRoot.transform, false);
                var hFaceRect = hFaceGo.GetComponent<RectTransform>();
                hFaceRect.anchorMin = Vector2.zero;
                hFaceRect.anchorMax = Vector2.one;
                hFaceRect.offsetMin = hFaceRect.offsetMax = Vector2.zero;
                var hFaceImg = hFaceGo.GetComponent<Image>();
                hFaceImg.preserveAspect = true;
                hFaceImg.raycastTarget = false;
                hFaceImg.enabled = false;

                _homeDice[i] = hFaceImg;
                _homeDieRoots[i] = hRoot;
                hRoot.SetActive(false);

                // Away Die Root (matching media_1792421110113_45d0305f.png):
                var aRoot = new GameObject($"AwayDie{i + 1}", typeof(RectTransform), typeof(Image));
                aRoot.transform.SetParent(parent, false);
                var aRect = aRoot.GetComponent<RectTransform>();
                aRect.anchorMin = aRect.anchorMax = new Vector2(0.70f, ys[i]);
                aRect.sizeDelta = new Vector2(92f, 92f);
                var aImg = aRoot.GetComponent<Image>();
                aImg.sprite = GetUiSprite("Original/sprites/Buff6/DiceBackground");
                aImg.preserveAspect = true;
                aImg.raycastTarget = false;

                var aFaceGo = new GameObject("Face", typeof(RectTransform), typeof(Image));
                aFaceGo.transform.SetParent(aRoot.transform, false);
                var aFaceRect = aFaceGo.GetComponent<RectTransform>();
                aFaceRect.anchorMin = Vector2.zero;
                aFaceRect.anchorMax = Vector2.one;
                aFaceRect.offsetMin = aFaceRect.offsetMax = Vector2.zero;
                var aFaceImg = aFaceGo.GetComponent<Image>();
                aFaceImg.preserveAspect = true;
                aFaceImg.raycastTarget = false;
                aFaceImg.enabled = false;

                _awayDice[i] = aFaceImg;
                _awayDieRoots[i] = aRoot;
                aRoot.SetActive(false);
            }
        }

        private IEnumerator SmoothShiftFieldAndBall(int fromLoc, int toLoc, int possession)
        {
            float pitchScale = (_fieldPitchRoot != null && _fieldPitchRoot.sizeDelta.y > 960f)
                ? _fieldPitchRoot.sizeDelta.y / 960f : 1f;
            var startPitchX = GetPitchOffsetForLocation(fromLoc) * pitchScale;
            var targetPitchX = GetPitchOffsetForLocation(toLoc) * pitchScale;
            var startBallPos = _ballMarker != null ? _ballMarker.rectTransform.anchorMin : GetBallPosition(fromLoc, possession);
            var targetBallPos = GetBallPosition(toLoc, possession, _match != null ? _match.BallHolderSlotIndex : 1);
            var elapsed = 0f;
            const float duration = 0.50f;

            if (_ballShadow != null) _ballShadow.transform.SetAsLastSibling();
            if (_ballMarker != null) _ballMarker.transform.SetAsLastSibling();

            float rollSpin = (targetPitchX < startPitchX) ? -1f : 1f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                if (_fieldPitchRoot != null)
                {
                    _fieldPitchRoot.anchoredPosition = new Vector2(Mathf.Lerp(startPitchX, targetPitchX, t), 0f);
                }
                var ballPos = Vector2.Lerp(startBallPos, targetBallPos, t);
                if (_ballMarker != null)
                {
                    _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = ballPos;
                    _ballMarker.rectTransform.Rotate(0f, 0f, rollSpin * 450f * Time.deltaTime);
                }
                if (_ballShadow != null)
                {
                    _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax =
                        new Vector2(ballPos.x, ballPos.y - 0.015f);
                }

                // Running bounce on visible players during pitch transition
                float bounce = Mathf.Abs(Mathf.Sin(elapsed * 20f)) * 7f;
                if (_homePlayers != null)
                {
                    for (int i = 0; i < _homePlayers.Length; i++)
                        if (_homePlayers[i] != null && _homePlayers[i].gameObject.activeSelf)
                            _homePlayers[i].rectTransform.anchoredPosition = new Vector2(0f, bounce);
                }
                if (_awayPlayers != null)
                {
                    for (int i = 0; i < _awayPlayers.Length; i++)
                        if (_awayPlayers[i] != null && _awayPlayers[i].gameObject.activeSelf)
                            _awayPlayers[i].rectTransform.anchoredPosition = new Vector2(0f, bounce);
                }

                yield return null;
            }

            if (_fieldPitchRoot != null)
                _fieldPitchRoot.anchoredPosition = new Vector2(targetPitchX, 0f);
            if (_ballMarker != null)
                _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = targetBallPos;
            if (_ballShadow != null)
                _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax =
                    new Vector2(targetBallPos.x, targetBallPos.y - 0.015f);

            if (_homePlayers != null)
                for (int i = 0; i < _homePlayers.Length; i++)
                    if (_homePlayers[i] != null) _homePlayers[i].rectTransform.anchoredPosition = Vector2.zero;
            if (_awayPlayers != null)
                for (int i = 0; i < _awayPlayers.Length; i++)
                    if (_awayPlayers[i] != null) _awayPlayers[i].rectTransform.anchoredPosition = Vector2.zero;

            if (_ballShadow != null) _ballShadow.transform.SetAsLastSibling();
            if (_ballMarker != null) _ballMarker.transform.SetAsLastSibling();
        }

        private IEnumerator SmoothPassBall(Vector2 start, Vector2 target, float duration, bool withArc = false)
        {
            if (_ballMarker == null) yield break;
            if (_ballShadow != null) _ballShadow.transform.SetAsLastSibling();
            _ballMarker.transform.SetAsLastSibling();

            float elapsed = 0f;
            float spinDir = target.x >= start.x ? -1f : 1f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                var cur = Vector2.Lerp(start, target, smoothT);
                float arcY = withArc ? Mathf.Sin(smoothT * Mathf.PI) * 0.06f : 0f;

                _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = new Vector2(cur.x, cur.y + arcY);
                _ballMarker.rectTransform.Rotate(0f, 0f, spinDir * 900f * Time.deltaTime);

                if (_ballShadow != null)
                {
                    _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax = new Vector2(cur.x, cur.y - 0.015f);
                    float shadowScale = withArc ? Mathf.Lerp(1f, 0.75f, Mathf.Sin(smoothT * Mathf.PI)) : 1f;
                    _ballShadow.rectTransform.localScale = new Vector3(shadowScale, shadowScale, 1f);
                }

                yield return null;
            }

            _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = target;
            if (_ballShadow != null)
            {
                _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax = new Vector2(target.x, target.y - 0.015f);
                _ballShadow.rectTransform.localScale = Vector3.one;
            }
        }

        private IEnumerator IdlePlayerBobbing()
        {
            while (true)
            {
                if (!_inEncounterAnimation && _homePlayers != null && _awayPlayers != null)
                {
                    float t = Time.time * 3f;
                    for (int i = 0; i < _homePlayers.Length; i++)
                    {
                        if (_homePlayers[i] != null && _homePlayers[i].gameObject.activeSelf)
                        {
                            float bob = Mathf.Sin(t + i * 0.7f) * 2.2f;
                            _homePlayers[i].rectTransform.anchoredPosition = new Vector2(0f, bob);
                        }
                    }
                    for (int i = 0; i < _awayPlayers.Length; i++)
                    {
                        if (_awayPlayers[i] != null && _awayPlayers[i].gameObject.activeSelf)
                        {
                            float bob = Mathf.Sin(t + i * 0.7f + 1.2f) * 2.2f;
                            _awayPlayers[i].rectTransform.anchoredPosition = new Vector2(0f, bob);
                        }
                    }
                }
                yield return null;
            }
        }

        private static Sprite GetLogoShineSprite(int frame)
        {
            if (frame == 7)
                return GetUiSprite("Original/sprites/StartScreenLogoAnimation2/LogoShine07");
            return GetUiSprite($"Original/sprites/StartScreenLogoAnimation/LogoShine{frame:00}");
        }

        private IEnumerator AnimateLogoShine(Image shineOverlay)
        {
            while (shineOverlay != null)
            {
                yield return new WaitForSeconds(3.5f);
                if (shineOverlay == null) yield break;
                shineOverlay.enabled = true;
                for (int frame = 1; frame <= 11; frame++)
                {
                    if (shineOverlay == null) yield break;
                    shineOverlay.sprite = GetLogoShineSprite(frame);
                    yield return new WaitForSeconds(0.055f);
                }
                if (shineOverlay != null) shineOverlay.enabled = false;
            }
        }

        private IEnumerator AnimatePulse(RectTransform targetRect)
        {
            while (targetRect != null)
            {
                float scale = 1f + Mathf.Sin(Time.time * 2.2f) * 0.025f;
                targetRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
        }

        private Vector2 GetBallPosition(int location, int possession, int slotIndex = -1)
        {
            if (slotIndex < 0)
            {
                slotIndex = _match != null ? _match.BallHolderSlotIndex : 1;
            }

            // In 1v1 encounters (location 2 or -2), striker and GK face off at y = 0.38f
            if (Mathf.Abs(location) == 2)
            {
                if (location == 2) // Home Striker attacking Away GK
                {
                    return possession == 0 ? new Vector2(0.435f, 0.395f) : new Vector2(0.565f, 0.395f);
                }
                else // Away Striker attacking Home GK
                {
                    return possession == 1 ? new Vector2(0.565f, 0.395f) : new Vector2(0.435f, 0.395f);
                }
            }

            // In 3v3 outfield encounters (locations -1, 0, 1):
            // Ball rests right next to the boots of the active ball holder:
            float y = slotIndex switch
            {
                0 => 0.145f,
                2 => 0.645f,
                _ => 0.395f
            };

            float x = possession == 0 ? 0.365f : 0.645f;
            return new Vector2(x, y);
        }

        private void SetBallPosition(int location, int possession, bool smooth, int slotIndex = -1)
        {
            var target = GetBallPosition(location, possession, slotIndex);
            if (_ballMarker != null)
            {
                _ballMarker.rectTransform.anchorMin = _ballMarker.rectTransform.anchorMax = target;
                _ballMarker.rectTransform.anchoredPosition = Vector2.zero;
            }
            if (_ballShadow != null)
            {
                _ballShadow.rectTransform.anchorMin = _ballShadow.rectTransform.anchorMax =
                    new Vector2(target.x, target.y - 0.012f);
                _ballShadow.rectTransform.anchoredPosition = Vector2.zero;
            }
            if (_ballShadow != null) _ballShadow.transform.SetAsLastSibling();
            if (_ballMarker != null) _ballMarker.transform.SetAsLastSibling();
        }

        private string GetEncounterPreviewText()
        {
            if (_match == null) return "Shake or tap either screen edge to roll.";

            if (_isLocalMultiplayer)
            {
                switch (_match.Location)
                {
                    case 0:
                        return "MIDFIELD BATTLE: P1 (RED) VS P2 (BLUE) — TAP TO ROLL!";
                    case 1:
                        return _match.Possession == 0
                            ? "P1 ATTACKING BLUE DEFENSE! — TAP TO ROLL"
                            : "P2 COUNTER-ATTACKING FROM MIDFIELD! — TAP TO ROLL";
                    case 2:
                        return _match.Possession == 0
                            ? "P1 1-ON-1 SHOT ON GOAL! — P1 vs P2 GOALKEEPER"
                            : "P2 DEFENSIVE CLEARANCE! — TAP TO ROLL";
                    case -1:
                        return _match.Possession == 1
                            ? "P2 ATTACKING RED DEFENSE! — TAP TO ROLL"
                            : "P1 COUNTER-ATTACKING FROM RED DEFENSE! — TAP TO ROLL";
                    case -2:
                        return _match.Possession == 1
                            ? "P2 1-ON-1 SHOT ON GOAL! — P2 vs P1 GOALKEEPER"
                            : "P1 DEFENSIVE CLEARANCE! — TAP TO ROLL";
                    default:
                        return "P1 (RED) VS P2 (BLUE) — TAP TO ROLL!";
                }
            }

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
            var scales = new float[lineup.Length];

            for (var slot = 0; slot < lineup.Length; slot++)
            {
                var pData = lineup[slot];
                var resourcePath = GetPlayerFigurePath(pData);
                var pId = pData != null ? pData.id : (slot + 1);

                var playerGo = new GameObject($"{(home ? "Home" : "Away")}Player{slot + 1}", typeof(RectTransform), typeof(Image));
                playerGo.transform.SetParent(parent, false);
                var player = playerGo.GetComponent<Image>();
                player.sprite = GetUiSprite(resourcePath);
                // Pin pivot to boot soles (y=330 in 200x340 sprite => normalized 0.032f)
                player.rectTransform.pivot = new Vector2(0.5f, 0.032f);
                player.rectTransform.sizeDelta = new Vector2(140f, 238f);
                player.color = Color.white;
                player.raycastTarget = false;
                player.preserveAspect = true;

                // Calibrate visual height so junior squad members stand uniformly alongside adult figures
                float vScale = GetPlayerFigureVisualScale(pId);
                scales[slot] = vScale;
                player.rectTransform.localScale = new Vector3((home ? -1f : 1f) * vScale, vScale, 1f);

                // Add shadow under player feet (1:1 overlay with 200x340 sprite)
                var shadowGo = new GameObject("Shadow", typeof(RectTransform), typeof(Image));
                shadowGo.transform.SetParent(player.transform, false);
                var shadowRect = shadowGo.GetComponent<RectTransform>();
                shadowRect.anchorMin = Vector2.zero;
                shadowRect.anchorMax = Vector2.one;
                shadowRect.offsetMin = shadowRect.offsetMax = Vector2.zero;
                var shadowImg = shadowGo.GetComponent<Image>();
                shadowImg.sprite = GetUiSprite("Original/sprites/PlayerFullFigureSS/PlayerFullFigureShadow");
                shadowImg.color = new Color(1f, 1f, 1f, 0.85f);
                shadowImg.preserveAspect = true;
                shadowImg.raycastTarget = false;
                shadowGo.transform.SetAsFirstSibling();

                players[slot] = player;
            }

            if (home) _homePlayerScales = scales;
            else _awayPlayerScales = scales;

            return players;
        }

        private void UpdatePlayerPositions(int? overrideLocation = null)
        {
            if (_homePlayers == null || _awayPlayers == null || _match == null) return;
            var loc = overrideLocation.HasValue ? overrideLocation.Value : _match.Location;
            var (activeHome, activeAway) = GetActiveDuelSlots(loc);

            // Ground baseline Ys matching media_1792421110195_880f72c3.png:
            // Row 0 (bottom): 0.13f, Row 1 (center): 0.38f, Row 2 (top): 0.63f
            float[] ys = { 0.13f, 0.38f, 0.63f };

            // Inactive slots hidden
            for (var slot = 0; slot < 7; slot++)
            {
                _homePlayers[slot].gameObject.SetActive(false);
                _awayPlayers[slot].gameObject.SetActive(false);
            }

            // Position active Home players (face right towards opponent/goal)
            for (var i = 0; i < activeHome.Length; i++)
            {
                var slot = activeHome[i];
                var p = _homePlayers[slot];
                p.gameObject.SetActive(true);
                float y = activeHome.Length == 1 ? 0.38f : ys[i];
                float x = activeHome.Length == 1 ? (loc == 2 ? 0.38f : 0.42f) : 0.31f;
                p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(x, y);
                p.rectTransform.anchoredPosition = Vector2.zero;
                float currentScale = (_homePlayerScales != null && slot < _homePlayerScales.Length) ? _homePlayerScales[slot] : 1f;
                p.rectTransform.localScale = new Vector3(-currentScale, currentScale, 1f);
            }

            // Position active Away players (face left towards opponent/goal)
            for (var i = 0; i < activeAway.Length; i++)
            {
                var slot = activeAway[i];
                var p = _awayPlayers[slot];
                p.gameObject.SetActive(true);
                float y = activeAway.Length == 1 ? 0.38f : ys[i];
                float x = activeAway.Length == 1 ? (loc == 2 ? 0.58f : 0.62f) : 0.70f;
                p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(x, y);
                p.rectTransform.anchoredPosition = Vector2.zero;
                float currentScale = (_awayPlayerScales != null && slot < _awayPlayerScales.Length) ? _awayPlayerScales[slot] : 1f;
                p.rectTransform.localScale = new Vector3(currentScale, currentScale, 1f);
            }

            if (_ballShadow != null) _ballShadow.transform.SetAsLastSibling();
            if (_ballMarker != null) _ballMarker.transform.SetAsLastSibling();
        }

        private IEnumerator PlayPoofAtPosition(Vector2 anchorPos)
        {
            if (_field == null) yield break;
            var poofObj = new GameObject("PoofEffect", typeof(RectTransform), typeof(Image));
            poofObj.transform.SetParent(_field, false);
            var rect = poofObj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchorPos;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(110f, 110f);
            var img = poofObj.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;

            for (var f = 1; f <= 13; f++)
            {
                img.sprite = GetUiSprite($"Original/sprites/PlayerBody3/Poof{f:00}");
                yield return new WaitForSeconds(0.035f);
            }
            Destroy(poofObj);
        }

        private void AnimateAttackingPlayers(int attackingSide, float amount)
        {
            var players = attackingSide == 0 ? _homePlayers : _awayPlayers;
            if (players == null) return;
            var direction = attackingSide == 0 ? 1f : -1f;

            for (var slot = 0; slot < players.Length; slot++)
            {
                if (players[slot].gameObject.activeSelf)
                {
                    var rect = players[slot].rectTransform;
                    rect.anchoredPosition = new Vector2(direction * 14f * amount, 5f * amount);
                }
            }
        }

        private void ClearCanvas()
        {
            if (_idleBobbingCoroutine != null)
            {
                StopCoroutine(_idleBobbingCoroutine);
                _idleBobbingCoroutine = null;
            }

            for (var i = _canvas.transform.childCount - 1; i >= 0; i--)
            {
                var child = _canvas.transform.GetChild(i);
                if (child != _safeArea)
                {
                    if (Application.isPlaying)
                        Destroy(child.gameObject);
                    else
                        DestroyImmediate(child.gameObject);
                }
            }
            for (var i = _safeArea.childCount - 1; i >= 0; i--)
            {
                var child = _safeArea.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
            _backButton = null;
            _substitutionButton = null;
            _encounterButton = null;
            _overlayContainer = null;
            _inEncounterAnimation = false;
            _fieldPitchRoot = null;
            _shakePrompt = null;
            _homeTotalText = null;
            _awayTotalText = null;
            _homeDieRoots = null;
            _awayDieRoots = null;
            _homePlayerScales = null;
            _awayPlayerScales = null;
        }

        private void ApplySafeArea()
        {
            if (_safeArea == null)
                return;

            var area = Screen.safeArea;
            float screenW = Screen.width;
            float screenH = Screen.height;

            bool fullScreen = PlayerPrefs.GetInt("diceSoccer.fullscreen", 0) == 1;

            if (fullScreen)
            {
                // Edge-to-edge safe area: fills the screen while respecting hardware notch and nav bar
                _safeArea.anchorMin = new Vector2(area.xMin / screenW, area.yMin / screenH);
                _safeArea.anchorMax = new Vector2(area.xMax / screenW, area.yMax / screenH);
            }
            else
            {
                // Strict authentic 2:3 aspect ratio letterboxing
                const float targetAspect = ReferenceWidth / ReferenceHeight; // 640 / 960 = 2/3 = 0.666667f
                float currentAspect = screenW / screenH;

                if (currentAspect < targetAspect)
                {
                    // Screen is taller than 2:3 (modern phones) -> top/bottom letterbox bars
                    float viewportHeightFraction = currentAspect / targetAspect;
                    float marginY = (1f - viewportHeightFraction) * 0.5f;
                    _safeArea.anchorMin = new Vector2(0f, marginY);
                    _safeArea.anchorMax = new Vector2(1f, 1f - marginY);
                }
                else
                {
                    // Screen is wider than 2:3 (tablets) -> left/right pillarbox bars
                    float viewportWidthFraction = targetAspect / currentAspect;
                    float marginX = (1f - viewportWidthFraction) * 0.5f;
                    _safeArea.anchorMin = new Vector2(marginX, 0f);
                    _safeArea.anchorMax = new Vector2(1f - marginX, 1f);
                }
            }

            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
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
                    new Vector2(0.08f, 0.96f), new Vector2(0.12f, 0.075f), backAction);

            if (!string.IsNullOrEmpty(title))
            {
                var fontSize = title.Length > 12 ? 11 : (title.Length > 9 ? 13 : 18);
                var titleText = AddText(title.ToUpperInvariant(), _safeArea, fontSize, Ink,
                    new Vector2(backAction != null ? 0.16f : 0.05f, 0.93f), new Vector2(0.39f, 0.99f));
                titleText.alignment = TextAnchor.MiddleLeft;
                titleText.fontStyle = FontStyle.Bold;
                titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
                titleText.verticalOverflow = VerticalWrapMode.Truncate;
                titleText.resizeTextForBestFit = true;
                titleText.resizeTextMinSize = 8;
                titleText.resizeTextMaxSize = fontSize;
            }

            var cashText = AddText(_careerCash.ToString("N0"), _safeArea, 18, Ink,
                new Vector2(0.40f, 0.93f), new Vector2(0.68f, 0.99f));
            cashText.alignment = TextAnchor.MiddleRight;
            cashText.fontStyle = FontStyle.Bold;

            var coinImg = AddResourceImage("CareerCoin", "Original/images/CoinSmall", _safeArea,
                new Vector2(0.69f, 0.935f), new Vector2(0.76f, 0.985f));
            coinImg.preserveAspect = true;
            coinImg.raycastTarget = false;

            // Authentic crest frame on top-right
            var crestBox = AddResourceImage("HeaderCrestBox", "Original/images/CustomCrestBox", _safeArea,
                new Vector2(0.85f, 0.925f), new Vector2(0.97f, 0.995f));
            crestBox.preserveAspect = true;
            crestBox.raycastTarget = false;

            var crestDesign = AddResourceImage("HeaderCrestDesign", $"Original/images/CustomCrest{_currentCrestId}", crestBox.transform,
                new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f));
            crestDesign.preserveAspect = true;
            crestDesign.raycastTarget = false;
        }

        private void AddMenuFooter()
        {
            var footer = AddResourceImage("MenuFooter", "Original/images/FooterBG",
                _safeArea, new Vector2(0f, 0f), new Vector2(1f, 0.12f));
            footer.preserveAspect = false;
            footer.raycastTarget = false;

            AddSpriteButton("Shop", "Original/images/BottomTab01",
                "Original/images/BottomTab01Pressed", _safeArea,
                new Vector2(0.17f, 0.06f), new Vector2(0.32f, 0.10f), ShowShop);
            AddSpriteButton("Trophies", "Original/images/BottomTab02",
                "Original/images/BottomTab02Pressed", _safeArea,
                new Vector2(0.50f, 0.06f), new Vector2(0.32f, 0.10f), ShowTrophyRoom);
            AddSpriteButton("Options", "Original/images/BottomTab03",
                "Original/images/BottomTab03Pressed", _safeArea,
                new Vector2(0.83f, 0.06f), new Vector2(0.32f, 0.10f), ShowOptions);
        }

        private static Button AddSpriteButton(string objectName, string resourcePath,
            string pressedSpritePath, Transform parent, Vector2 anchor, Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var image = AddResourceImage(objectName, resourcePath, parent,
                anchor - size * 0.5f, anchor + size * 0.5f);
            image.preserveAspect = true;
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
            var image = AddPanel(label, parent, color, anchor, size);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.65f);
            outline.effectDistance = new Vector2(2, -2);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance.PlayTap();
                onClick?.Invoke();
            });
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.pressedColor = new Color32(200, 200, 200, 255);
            colors.disabledColor = new Color32(110, 110, 110, 180);
            button.colors = colors;
            var text = AddText(label, image.transform, 17, Paper, Vector2.zero, Vector2.one);
            text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = 17;
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
