using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal sealed class TilePaletteLayoutStudioWindow : EditorWindow
    {
        private const float PreviewCellSize = 32f;
        private TilePaletteProfile profile;
        private Vector2 scroll;

        [MenuItem("Tools/Tile Palette/Layout Studio", priority = 100)]
        private static void Open()
        {
            GetWindow<TilePaletteLayoutStudioWindow>("Tile Palette Layout Studio");
        }

        internal static void RepaintOpenWindows()
        {
            foreach (TilePaletteLayoutStudioWindow window in Resources.FindObjectsOfTypeAll<TilePaletteLayoutStudioWindow>())
                window.Repaint();
        }

        private void OnEnable()
        {
            profile = TilePaletteProfileStore.LoadPreferred();
            minSize = new Vector2(720f, 500f);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawProfile();
            if (profile != null)
            {
                DrawActions();
                DrawPreview();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawProfile()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                new GUIContent("Profile & Paths", "References used by this layout profile."),
                EditorStyles.boldLabel);
            TilePaletteProfile selected = (TilePaletteProfile)EditorGUILayout.ObjectField(
                new GUIContent("Profile", "The authoritative layout for one Tile Palette."),
                profile,
                typeof(TilePaletteProfile),
                false);
            if (selected != profile)
            {
                profile = selected;
                TilePaletteProfileStore.SetPreferred(profile);
            }

            if (profile == null)
            {
                if (GUILayout.Button(new GUIContent("Create Profile", "Create a layout Profile in Assets.")))
                    Run("Profile 创建", () =>
                    {
                        profile = TilePaletteProfileStore.CreateProfile();
                        Debug.Log("[TilePalette] Profile 创建完成");
                    });
                return;
            }

            EditorGUI.BeginChangeCheck();
            DefaultAsset sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("Source Folder", "Used when a Recipe refers to Sprites by name."),
                profile.SourceFolder,
                typeof(DefaultAsset),
                false);
            DefaultAsset tileOutputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("Tile Output Folder", "Missing Tile assets are created here."),
                profile.TileOutputFolder,
                typeof(DefaultAsset),
                false);
            GameObject palettePrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Palette Prefab", "The Tile Palette Prefab built and validated by this Profile."),
                profile.PalettePrefab,
                typeof(GameObject),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Change Tile Palette Profile Paths");
                profile.SourceFolder = sourceFolder;
                profile.TileOutputFolder = tileOutputFolder;
                profile.PalettePrefab = palettePrefab;
                EditorUtility.SetDirty(profile);
                TilePaletteProfileStore.SetPreferred(profile);
                TilePaletteAutoSyncGuard.SaveAssetsWithoutSync();
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                new GUIContent("Main Actions", "Build or validate the saved Profile layout."),
                EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent("Build Palette", "Validate, apply the Profile, then validate again."),
                    GUILayout.Height(28f)))
                BuildPalette();
            if (GUILayout.Button(
                    new GUIContent("Validate", "Read-only check of Profile, Tile assets, and Palette cells."),
                    GUILayout.Height(28f)))
                ValidatePalette();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreview()
        {
            List<PreviewEntry> entries = profile.Groups
                .SelectMany(group => group.entries.Where(entry => entry.included && entry.sprite != null)
                    .Select(entry => new PreviewEntry
                    {
                        Group = group,
                        Entry = entry,
                        Position = TilePaletteProfileUtility.GetTargetPosition(group, entry)
                    }))
                .ToList();
            if (entries.Count == 0) return;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                new GUIContent("Layout Preview", "The final layout currently stored in the Profile."),
                EditorStyles.boldLabel);
            BoundsInt bounds = TilePaletteProfileUtility.CalculateBounds(entries.Select(entry => entry.Position));
            float width = Mathf.Max(position.width - 26f, bounds.size.x * PreviewCellSize);
            float height = bounds.size.y * PreviewCellSize;
            Rect area = GUILayoutUtility.GetRect(width, height);

            foreach (PreviewEntry previewEntry in entries)
            {
                int x = previewEntry.Position.x - bounds.xMin;
                int y = bounds.yMax - 1 - previewEntry.Position.y;
                Rect tileRect = new Rect(
                    area.x + x * PreviewCellSize,
                    area.y + y * PreviewCellSize,
                    PreviewCellSize - 2f,
                    PreviewCellSize - 2f);
                Texture preview = AssetPreview.GetAssetPreview(previewEntry.Entry.sprite) ??
                                  AssetPreview.GetMiniThumbnail(previewEntry.Entry.sprite);
                if (preview != null) GUI.DrawTexture(tileRect, preview, ScaleMode.ScaleToFit, true);
                GUI.Box(
                    tileRect,
                    new GUIContent(
                        string.Empty,
                        previewEntry.Group.Id + "/" + previewEntry.Entry.sourceId + " " + previewEntry.Position));
            }
        }

        private void BuildPalette()
        {
            Run("构建", () =>
            {
                LayoutPlan plan = TilePaletteBuilder.CreatePlan(profile);
                if (plan.HasBlockingConflicts)
                    throw new InvalidOperationException(string.Join("\n", plan.Errors));
                if (plan.HasConfirmedMoves && !EditorUtility.DisplayDialog(
                        "Confirm Palette Changes",
                        "Known Tiles will exchange occupied Profile cells. Continue?",
                        "Build",
                        "Cancel"))
                    return;

                TilePaletteBuilder.Apply(profile);
                Debug.Log("[TilePalette] 构建完成");
            });
        }

        private void ValidatePalette()
        {
            Run("验证", () =>
            {
                TilePaletteBuilder.ValidateComplete(profile);
                Debug.Log("[TilePalette] 验证通过");
            });
        }

        private void Run(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError("[TilePalette] " + operation + "失败：" + exception.Message);
            }
            finally
            {
                Repaint();
            }
        }

        private sealed class PreviewEntry
        {
            public TilePaletteProfileGroup Group;
            public TilePaletteProfileEntry Entry;
            public Vector3Int Position;
        }
    }
}
