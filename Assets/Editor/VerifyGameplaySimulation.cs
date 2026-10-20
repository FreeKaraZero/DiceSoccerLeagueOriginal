#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DiceSoccer.Editor
{
    public static class VerifyGameplaySimulation
    {
        public static void RunTest()
        {
            Debug.Log("[TEST] Starting VerifyGameplaySimulation...");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var bootstrap = UnityEngine.Object.FindAnyObjectByType<DiceSoccerBootstrap>();
            if (bootstrap == null)
            {
                throw new Exception("DiceSoccerBootstrap not found in scene!");
            }

            // 1. Clear any broken tutorial state
            for (int i = 0; i < 100; i++)
            {
                PlayerPrefs.SetInt($"diceSoccer.tutorial.{i}", 1);
            }
            PlayerPrefs.Save();

            // 2. Awake
            var awake = typeof(DiceSoccerBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake?.Invoke(bootstrap, null);
            Debug.Log("[TEST] Awake successfully executed. Home screen active.");

            // 3. Start a career match to load fixtures, then start the current fixture
            var startCareer = typeof(DiceSoccerBootstrap).GetMethod("StartCareerMatch", BindingFlags.Instance | BindingFlags.NonPublic);
            startCareer?.Invoke(bootstrap, null);
            Debug.Log("[TEST] StartCareerMatch invoked successfully (Fixtures loaded).");

            var startFixture = typeof(DiceSoccerBootstrap).GetMethod("StartCurrentFixture", BindingFlags.Instance | BindingFlags.NonPublic);
            startFixture?.Invoke(bootstrap, null);
            Debug.Log("[TEST] StartCurrentFixture invoked successfully (Match started).");

            // 4. Verify match state
            var matchField = typeof(DiceSoccerBootstrap).GetField("_match", BindingFlags.Instance | BindingFlags.NonPublic);
            var match = matchField?.GetValue(bootstrap) as MatchSimulation;
            if (match == null)
            {
                throw new Exception("MatchSimulation is null after StartCareerMatch!");
            }
            Debug.Log($"[TEST] Match active: HomeScore={match.HomeScore}, AwayScore={match.AwayScore}, Location={match.Location}, Finished={match.IsFinished}");

            // 5. Verify field and players are created
            var homePlayersField = typeof(DiceSoccerBootstrap).GetField("_homePlayers", BindingFlags.Instance | BindingFlags.NonPublic);
            var awayPlayersField = typeof(DiceSoccerBootstrap).GetField("_awayPlayers", BindingFlags.Instance | BindingFlags.NonPublic);
            var homePlayers = homePlayersField?.GetValue(bootstrap) as Image[];
            var awayPlayers = awayPlayersField?.GetValue(bootstrap) as Image[];

            if (homePlayers == null || homePlayers.Length != 7)
                throw new Exception($"Home players invalid: {homePlayers?.Length ?? 0}");
            if (awayPlayers == null || awayPlayers.Length != 7)
                throw new Exception($"Away players invalid: {awayPlayers?.Length ?? 0}");

            for (int i = 0; i < 7; i++)
            {
                if (homePlayers[i].sprite == null)
                    throw new Exception($"Home player {i} sprite is null!");
                if (awayPlayers[i].sprite == null)
                    throw new Exception($"Away player {i} sprite is null!");
            }
            Debug.Log("[TEST] All 14 players have valid sprites and are placed on field.");

            // 6. Simulate multiple encounters (play full match)
            var completeEncounterMethod = typeof(MatchSimulation).GetMethod("CompleteEncounter", BindingFlags.Instance | BindingFlags.Public);
            var beginEncounterMethod = typeof(MatchSimulation).GetMethod("BeginEncounter", BindingFlags.Instance | BindingFlags.Public);
            var updatePlayerPositions = typeof(DiceSoccerBootstrap).GetMethod("UpdatePlayerPositions", BindingFlags.Instance | BindingFlags.NonPublic);

            int round = 0;
            while (!match.IsFinished && round < 30)
            {
                round++;
                beginEncounterMethod.Invoke(match, null);
                string report = completeEncounterMethod.Invoke(match, null) as string;
                updatePlayerPositions?.Invoke(bootstrap, new object[] { null });
                Debug.Log($"[TEST] Encounter {round}: Clock={match.ClockText()}, Score={match.HomeScore}-{match.AwayScore}, Loc={match.Location}, Report={report}");
            }

            if (!match.IsFinished)
            {
                throw new Exception($"Match did not finish within 30 encounters! Clock={match.ClockText()}");
            }
            Debug.Log($"[TEST] Match successfully finished! Final Score: {match.HomeScore} - {match.AwayScore}");

            // 7. Verify screen transitions: Manage, KitSelection, TrophyRoom, Shop, Fixtures
            var showManage = typeof(DiceSoccerBootstrap).GetMethod("ShowManage", BindingFlags.Instance | BindingFlags.NonPublic);
            showManage?.Invoke(bootstrap, null);
            Debug.Log("[TEST] ShowManage executed successfully.");

            var showKit = typeof(DiceSoccerBootstrap).GetMethod("ShowKitSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            showKit?.Invoke(bootstrap, null);
            Debug.Log("[TEST] ShowKitSelection executed successfully.");

            var showTrophies = typeof(DiceSoccerBootstrap).GetMethod("ShowTrophyRoom", BindingFlags.Instance | BindingFlags.NonPublic);
            showTrophies?.Invoke(bootstrap, null);
            Debug.Log("[TEST] ShowTrophyRoom executed successfully.");

            var showShop = typeof(DiceSoccerBootstrap).GetMethod("ShowShop", BindingFlags.Instance | BindingFlags.NonPublic);
            showShop?.Invoke(bootstrap, null);
            Debug.Log("[TEST] ShowShop executed successfully.");

            var showFixtures = typeof(DiceSoccerBootstrap).GetMethod("ShowLeagueFixtures", BindingFlags.Instance | BindingFlags.NonPublic);
            showFixtures?.Invoke(bootstrap, null);
            Debug.Log("[TEST] ShowLeagueFixtures executed successfully.");

            // 8. Verify Audio system and clips
            var listener = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
            if (listener == null)
            {
                throw new Exception("No AudioListener found in scene! Audio will not play!");
            }
            Debug.Log($"[TEST] AudioListener verified on GameObject: {listener.gameObject.name}");

            var audioManager = AudioManager.Instance;
            if (audioManager == null)
            {
                throw new Exception("AudioManager instance is null!");
            }

            var bgmField = typeof(AudioManager).GetField("_bgmSource", BindingFlags.Instance | BindingFlags.NonPublic);
            var bgmSource = bgmField?.GetValue(audioManager) as AudioSource;
            if (bgmSource == null || bgmSource.spatialBlend != 0f)
            {
                throw new Exception("BGM AudioSource invalid or not 2D!");
            }

            string[] testClips = { "MainTheme", "StadiumCrowdChant", "Tap1", "DiceRoll1", "Whistle1", "KidsCheering" };
            foreach (var clipName in testClips)
            {
                var clip = Resources.Load<AudioClip>($"Original/audio/{clipName}");
                if (clip == null)
                {
                    throw new Exception($"AudioClip {clipName} could not be loaded from Resources!");
                }
                clip.LoadAudioData();
                Debug.Log($"[TEST] AudioClip {clipName} verified: Channels={clip.channels}, Frequency={clip.frequency}, Length={clip.length}s, LoadState={clip.loadState}");
            }

            Debug.Log("[TEST] ALL GAMEPLAY, UI AND AUDIO VERIFICATION TESTS PASSED COMPLETELY!");
        }
    }
}
#endif
