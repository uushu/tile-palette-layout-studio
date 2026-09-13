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
            IReadOnlyList<TilePalettePreviewGroup> groups = TilePalettePreviewUtility.BuildGroups(profile);
            if (groups.Count == 0) return;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                new GUIContent("Layout Preview", "The final layout currently stored in the Profile."),
                EditorStyles.boldLabel);

            float availableWidth = Mathf.Max(320f, position.width - 30f);
            const float cardPadding = 8f;
            const float labelHeight = 20f;
            const float cardSpacing = 10f;
            float cursorX = 0f;
            float cursorY = 0f;
            float rowHeight = 0f;
            TilePalettePreviewCard[] cards = groups.Select(group =>
            {
                float labelWidth = EditorStyles.boldLabel.CalcSize(new GUIContent(group.Id)).x + cardPadding * 2f;
                float cardWidth = Mathf.Max(
                    120f,
                    group.Bounds.size.x * PreviewCellSize + cardPadding * 2f,
                    labelWidth);
                float cardHeight = labelHeight + group.Bounds.size.y * PreviewCellSize + cardPadding * 2f;
                if (cursorX > 0f && cursorX + cardWidth > availableWidth)
                {
                    cursorX = 0f;
                    cursorY += rowHeight + cardSpacing;
                    rowHeight = 0f;
                }
                TilePalettePreviewCard card = new TilePalettePreviewCard
                {
                    Group = group,
                    Position = new Rect(cursorX, cursorY, cardWidth, cardHeight)
                };
                cursorX += cardWidth + cardSpacing;
                rowHeight = Mathf.Max(rowHeight, cardHeight);
                return card;
            }).ToArray();
            float totalHeight = cursorY + rowHeight;
            Rect area = GUILayoutUtility.GetRect(availableWidth, totalHeight);

            foreach (TilePalettePreviewCard card in cards)
            {
                Rect cardRect = new Rect(
                    area.x + card.Position.x,
                    area.y + card.Position.y,
                    card.Position.width,
                    card.Position.height);
                EditorGUI.DrawRect(cardRect, new Color(0.16f, 0.16f, 0.17f, 1f));
                GUI.Label(
                    new Rect(cardRect.x + cardPadding, cardRect.y + 3f, cardRect.width - cardPadding * 2f, labelHeight),
                    new GUIContent(card.Group.Id, "Profile group and subgroup."),
                    EditorStyles.boldLabel);
                Rect gridRect = new Rect(
                    cardRect.x + cardPadding,
                    cardRect.y + labelHeight + cardPadding,
                    card.Group.Bounds.size.x * PreviewCellSize,
                    card.Group.Bounds.size.y * PreviewCellSize);
                foreach (TilePalettePreviewEntry previewEntry in card.Group.Entries)
                {
                    int x = previewEntry.LocalPosition.x - card.Group.Bounds.xMin;
                    int y = card.Group.Bounds.yMax - 1 - previewEntry.LocalPosition.y;
                    Rect cellRect = new Rect(
                        gridRect.x + x * PreviewCellSize,
                        gridRect.y + y * PreviewCellSize,
                        PreviewCellSize,
                        PreviewCellSize);
                    EditorGUI.DrawRect(cellRect, new Color(0.22f, 0.22f, 0.23f, 1f));
                    Rect spriteRect = TilePalettePreviewUtility.GetSpriteDrawRect(previewEntry.Entry.sprite, cellRect);
                    GUI.DrawTextureWithTexCoords(
                        spriteRect,
                        previewEntry.Entry.sprite.texture,
                        TilePalettePreviewUtility.GetSpriteUv(previewEntry.Entry.sprite),
                        true);
                    GUI.Box(
                        cellRect,
                        new GUIContent(
                            string.Empty,
                            card.Group.Id + "/" + previewEntry.Entry.sourceId + " " +
                            previewEntry.TargetPosition),
                        GUIStyle.none);
                }
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

        private sealed class TilePalettePreviewCard
        {
            public TilePalettePreviewGroup Group;
            public Rect Position;
        }
    }
}
