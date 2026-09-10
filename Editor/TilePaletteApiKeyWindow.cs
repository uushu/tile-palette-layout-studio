using System;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal sealed class TilePaletteApiKeyWindow : EditorWindow
    {
        private const float WindowWidth = 420f;
        private const float WindowHeight = 92f;

        private string environmentVariable;
        private string apiKey = string.Empty;
        private Action saved;

        public static void Open(
            ITilePaletteVisionProvider provider,
            Action saved)
        {
            TilePaletteApiKeyWindow window =
                CreateInstance<TilePaletteApiKeyWindow>();
            window.titleContent = new GUIContent("GLM API Key");
            window.environmentVariable = provider.ApiKeyEnvironment;
            window.saved = saved;
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = window.minSize;
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10f);
            apiKey = EditorGUILayout.PasswordField(
                new GUIContent(
                    "API Key",
                    "Stored only in this Unity project's local Library folder."),
                apiKey);
            EditorGUILayout.Space(8f);

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(apiKey));
            if (GUILayout.Button(
                    new GUIContent("Save", "Saves the key locally and continues analysis."),
                    GUILayout.Height(26f)))
                Save();
            EditorGUI.EndDisabledGroup();
        }

        private void Save()
        {
            try
            {
                TilePaletteVisionSettings.SaveApiKey(
                    environmentVariable,
                    apiKey);
                apiKey = string.Empty;
                Action callback = saved;
                saved = null;
                Close();
                callback?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogError("[TilePalette] API Key 保存失败：" + exception.Message);
            }
        }
    }
}
