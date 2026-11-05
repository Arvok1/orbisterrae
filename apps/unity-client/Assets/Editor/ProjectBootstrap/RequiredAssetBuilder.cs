using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Editor
{
    public static class RequiredAssetBuilder
    {
        const string MaterialDirectory = "Assets/Resources/Orbis";
        const string MaterialPath = MaterialDirectory + "/WorldRelief.mat";
        const string ThemePath = MaterialDirectory + "/OrbisRuntimeTheme.asset";
        const string PanelSettingsPath = MaterialDirectory + "/WorldPanelSettings.asset";

        [MenuItem("Orbis/Setup/Ensure Required Assets")]
        public static void Ensure()
        {
            Directory.CreateDirectory(MaterialDirectory);
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new System.InvalidOperationException("The URP Lit shader is unavailable. Resolve the Universal Render Pipeline package first.");
                }

                var material = new Material(shader) { name = "World Relief" };
                material.SetColor("_BaseColor", new Color(0.075f, 0.16f, 0.12f));
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0.18f);
                AssetDatabase.CreateAsset(material, MaterialPath);
                Debug.Log($"[Orbis] Created required material at {MaterialPath}.");
            }

            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) == null)
            {
                var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
                if (theme == null)
                {
                    theme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                    theme.name = "Orbis Runtime Theme";
                    AssetDatabase.CreateAsset(theme, ThemePath);
                }

                var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.name = "World Panel Settings";
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1280, 720);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;
                panelSettings.themeStyleSheet = theme;
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
                Debug.Log($"[Orbis] Created required panel settings at {PanelSettingsPath}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
