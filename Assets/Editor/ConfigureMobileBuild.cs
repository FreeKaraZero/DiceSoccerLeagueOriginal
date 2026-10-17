#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DiceSoccer.Editor
{
    internal static class ConfigureMobileBuild
    {
        [MenuItem("Dice Soccer/Configure Android and iOS")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Dice Soccer Revival";
            PlayerSettings.productName = "Dice Soccer";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.dicesoccer.revival");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,
                "com.dicesoccer.revival");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            EditorPrefs.SetBool("SdkUseEmbedded", true);
            EditorPrefs.SetBool("NdkUseEmbedded", true);
            EditorPrefs.SetBool("JdkUseEmbedded", true);

            string contentsPath = EditorApplication.applicationContentsPath;
            string androidPlayerPath = Path.Combine(contentsPath, "PlaybackEngines/AndroidPlayer");
            string sdkPath = Path.Combine(androidPlayerPath, "SDK");
            string ndkPath = Path.Combine(androidPlayerPath, "NDK");
            string jdkPath = Path.Combine(androidPlayerPath, "OpenJDK");

            if (Directory.Exists(sdkPath))
                EditorPrefs.SetString("AndroidSDKRoot", sdkPath);
            if (Directory.Exists(ndkPath))
                EditorPrefs.SetString("AndroidNDKRoot", ndkPath);
            if (Directory.Exists(jdkPath))
                EditorPrefs.SetString("JdkPath", jdkPath);

            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var appIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Resources/Original/images/Icon@2x.png");
            if (appIcon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Android, new[] { appIcon },
                    IconKind.Application);
                PlayerSettings.SetIcons(NamedBuildTarget.iOS, new[] { appIcon },
                    IconKind.Application);
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true)
            };

            AssetDatabase.SaveAssets();
            Debug.Log("Dice Soccer mobile settings configured for portrait Android and iOS.");
        }

        public static void BuildAndroid()
        {
            Configure();
            var outputPath = Path.GetFullPath("Builds/Android/DiceSoccer.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Android build failed with result {report.summary.result}.");

            Debug.Log($"Android APK created at {outputPath}.");
        }
    }
}
#endif
