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
    public static class ConfigureMobileBuild
    {
        [MenuItem("Dice Soccer/Configure Android and iOS")]
        public static void Configure()
        {
            PlayerSettings.companyName = "KaraZero";
            PlayerSettings.productName = "Dice Soccer";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,
                "com.karazero.dicesoccer");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS,
                "com.karazero.dicesoccer");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

            string keystorePath = Path.GetFullPath("Keystores/DiceSoccer.keystore");
            if (File.Exists(keystorePath))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = "dicesoccer";
                PlayerSettings.Android.keyaliasName = "dicesoccer";
                PlayerSettings.Android.keyaliasPass = "dicesoccer";
            }

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
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;

            // Setup high-resolution AppIcons
            var bgPath = "Assets/AppIcons/AppIcon_Adaptive_Background.png";
            var fgPath = "Assets/AppIcons/AppIcon_Adaptive_Foreground.png";
            var legacyPath = "Assets/AppIcons/AppIcon_Legacy.png";
            var roundPath = "Assets/AppIcons/AppIcon_Round.png";

            foreach (var p in new[] { bgPath, fgPath, legacyPath, roundPath })
            {
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                if (importer != null)
                {
                    bool changed = false;
                    if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
                    if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
                    if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
                    if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
                    if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; changed = true; }
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                    if (changed) importer.SaveAndReimport();
                }
            }

            var bgIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);
            var fgIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(fgPath);
            var legacyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(legacyPath);
            var roundIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(roundPath);

            if (legacyIcon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Android, new[] { legacyIcon }, IconKind.Application);
                PlayerSettings.SetIcons(NamedBuildTarget.iOS, new[] { legacyIcon }, IconKind.Application);
            }

            // Configure modern Android Adaptive Icons (Pixel 10 / Android 8.0-15+)
            if (bgIcon != null && fgIcon != null)
            {
                var supportedKinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
                foreach (var kind in supportedKinds)
                {
                    var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                    if (icons != null && icons.Length > 0)
                    {
                        foreach (var icon in icons)
                        {
                            if (icon.maxLayerCount >= 2)
                            {
                                icon.SetTextures(new[] { bgIcon, fgIcon });
                            }
                            else if (legacyIcon != null)
                            {
                                icon.SetTextures(new[] { legacyIcon });
                            }
                        }
                        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
                    }
                }
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true)
            };

            AssetDatabase.SaveAssets();
            Debug.Log("Dice Soccer mobile settings configured with high-res Adaptive Icons for Android and iOS.");
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

            SignApk(outputPath);
            Debug.Log($"Android APK created and signed at {outputPath}.");
        }

        private static void SignApk(string apkPath)
        {
            string keystorePath = Path.GetFullPath("Keystores/DiceSoccer.keystore");
            if (!File.Exists(keystorePath)) return;

            string contentsPath = EditorApplication.applicationContentsPath;
            string apksignerPath = Path.Combine(contentsPath, "PlaybackEngines/AndroidPlayer/SDK/build-tools/36.0.0/apksigner");
            if (!File.Exists(apksignerPath)) return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = apksignerPath,
                Arguments = $"sign --ks \"{keystorePath}\" --ks-pass pass:dicesoccer --ks-key-alias dicesoccer --key-pass pass:dicesoccer --v1-signing-enabled true --v2-signing-enabled true --v3-signing-enabled true \"{apkPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc.WaitForExit();
            Debug.Log($"apksigner result: {proc.ExitCode}");
        }
    }
}
#endif
