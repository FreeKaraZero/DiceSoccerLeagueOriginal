#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace DiceSoccer.Editor
{
    public static class FixTextureImporterSettings
    {
        public static void FixAll()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Original" });
            Debug.Log($"Found {guids.Length} textures to check.");
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    bool changed = false;
                    if (importer.mipmapEnabled)
                    {
                        importer.mipmapEnabled = false;
                        changed = true;
                    }
                    if (importer.npotScale != TextureImporterNPOTScale.None)
                    {
                        importer.npotScale = TextureImporterNPOTScale.None;
                        changed = true;
                    }
                    if (!importer.alphaIsTransparency)
                    {
                        importer.alphaIsTransparency = true;
                        changed = true;
                    }
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                    {
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        changed = true;
                    }
                    if (importer.filterMode != FilterMode.Bilinear)
                    {
                        importer.filterMode = FilterMode.Bilinear;
                        changed = true;
                    }

                    var androidSettings = importer.GetPlatformTextureSettings("Android");
                    if (androidSettings.overridden)
                    {
                        androidSettings.overridden = false;
                        importer.SetPlatformTextureSettings(androidSettings);
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        modified++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
            Debug.Log($"Finished fixing {modified} textures.");
        }
    }
}
#endif
