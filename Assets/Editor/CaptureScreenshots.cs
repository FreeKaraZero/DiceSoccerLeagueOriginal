#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DiceSoccer.Editor
{
    public static class CaptureScreenshots
    {
        public static void Capture()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var bootstrap = UnityEngine.Object.FindAnyObjectByType<DiceSoccerBootstrap>();
            if (bootstrap == null) return;

            // Mark tutorials as seen so they don't block screens
            for (int i = 0; i < 100; i++)
            {
                PlayerPrefs.SetInt($"diceSoccer.tutorial.{i}", 1);
            }
            PlayerPrefs.Save();

            var awakeMethod = typeof(DiceSoccerBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awakeMethod?.Invoke(bootstrap, null);

            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            int width = 640;
            int height = 960;

            var camGo = new GameObject("CaptureCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.orthographicSize = height / 2f;
            camGo.transform.position = new Vector3(0, 0, -100);

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;

            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Screenshots");

            void SaveScreen(string name)
            {
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"Screenshots/{name}.png", tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                Debug.Log($"Saved Screenshots/{name}.png");
            }

            void Call(string methodName, params object[] args)
            {
                var method = typeof(DiceSoccerBootstrap).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                method?.Invoke(bootstrap, args);
            }

            // 1. Home
            SaveScreen("Home");

            // 2. Fixtures
            try { Call("StartCareerMatch"); SaveScreen("Fixtures"); } catch (Exception e) { Debug.LogWarning(e); }

            // 3. Match
            try
            {
                var catalog = GameCatalog.Load();
                var fixtures = catalog.GetFixtures(0, "League");
                var lineupField = typeof(DiceSoccerBootstrap).GetField("_lineupIds", BindingFlags.Instance | BindingFlags.NonPublic);
                var fixtureField = typeof(DiceSoccerBootstrap).GetField("_currentFixture", BindingFlags.Instance | BindingFlags.NonPublic);
                var matchField = typeof(DiceSoccerBootstrap).GetField("_match", BindingFlags.Instance | BindingFlags.NonPublic);
                
                var lineupIds = (int[])lineupField.GetValue(bootstrap);
                var homeLineup = catalog.ResolveLineup(lineupIds);
                fixtureField.SetValue(bootstrap, fixtures[0]);
                
                var match = new MatchSimulation(
                    homeLineup,
                    catalog.ResolveLineup(fixtures[0].lineup),
                    fixtures[0].teamName,
                    0,
                    new int[0],
                    fixtures[0].entourageLineup,
                    0,
                    fixtures[0].possessionBonus);
                matchField.SetValue(bootstrap, match);

                var showMatchScreenMethod = typeof(DiceSoccerBootstrap).GetMethod("ShowMatchScreen", BindingFlags.Instance | BindingFlags.NonPublic);
                showMatchScreenMethod?.Invoke(bootstrap, new object[] { homeLineup });
                SaveScreen("Match");

                // 4. Pause Menu
                Call("ShowPauseMenu");
                SaveScreen("PauseMenu");

                // Resume
                Call("ResumeFromPause");
            }
            catch (Exception e) { Debug.LogWarning(e); }

            // 5. Manage
            try { Call("ShowManage"); SaveScreen("Manage"); } catch (Exception e) { Debug.LogWarning(e); }

            // 6. Hiring
            try { Call("ShowHiring"); SaveScreen("Hiring"); } catch (Exception e) { Debug.LogWarning(e); }

            // 7. Shop
            try { Call("ShowShop"); SaveScreen("Shop"); } catch (Exception e) { Debug.LogWarning(e); }

            // 8. Trophies
            try { Call("ShowTrophyRoom"); SaveScreen("Trophies"); } catch (Exception e) { Debug.LogWarning(e); }

            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }
}
#endif
