using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal sealed class TilePaletteLayoutStudioWindow : EditorWindow
    {
        private TilePaletteProfile profile;
        private AnalyzedLayout analyzedLayout;
        private Vector2 scroll;
        private bool busy;
        private bool previewIsStale;
        private TilePaletteVisionAnalyzer activeAnalyzer;
        private VisionAnalysisProgress analysisProgress;

        [MenuItem("Tools/Tile Palette/Layout Studio", priority = 100)]
        private static void Open()
        {
            GetWindow<TilePaletteLayoutStudioWindow>(
                "Tile Palette Layout Studio");
        }

        private void OnEnable()
        {
            profile = TilePaletteProfileStore.LoadPreferred();
            minSize = new Vector2(760f, 540f);
        }

        private void OnDisable()
        {
            activeAnalyzer?.Cancel();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUI.BeginDisabledGroup(busy);
            DrawProfile();
            if (profile != null)
                DrawActions();
            EditorGUI.EndDisabledGroup();

            if (busy)
                DrawAnalysisProgress();
            if (profile != null)
                DrawPreview();

            EditorGUILayout.EndScrollView();
        }

        private void DrawProfile()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Profile & Paths",
                    "References used by this layout profile."),
                EditorStyles.boldLabel);

            TilePaletteProfile selected =
                (TilePaletteProfile)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "Profile",
                        "The authoritative layout data for one Palette."),
                    profile,
                    typeof(TilePaletteProfile),
                    false);

            if (selected != profile)
            {
                profile = selected;
                TilePaletteProfileStore.SetPreferred(profile);
                ResetAnalysis();
            }

            if (profile == null)
            {
                if (GUILayout.Button(
                        new GUIContent(
                            "Create Profile",
                            "Creates a new profile in Assets.")))
                {
                    RunAction(
                        "Profile 创建",
                        () =>
                        {
                            profile =
                                TilePaletteProfileStore.CreateProfile();
                            Debug.Log(
                                "[TilePalette] Profile 创建完成");
                        });
                }
                return;
            }

            EditorGUI.BeginChangeCheck();

            DefaultAsset source =
                (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "Source Folder",
                        "Sprite assets under this folder are analyzed."),
                    profile.SourceFolder,
                    typeof(DefaultAsset),
                    false);

            DefaultAsset output =
                (DefaultAsset)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "Tile Output Folder",
                        "Missing Tile assets are created here."),
                    profile.TileOutputFolder,
                    typeof(DefaultAsset),
                    false);

            GameObject palette =
                (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent(
                        "Palette Prefab",
                        "The Palette prefab managed by this profile."),
                    profile.PalettePrefab,
                    typeof(GameObject),
                    false);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(
                    profile,
                    "Change Tile Palette Profile Paths");
                profile.SourceFolder = source;
                profile.TileOutputFolder = output;
                profile.PalettePrefab = palette;
                EditorUtility.SetDirty(profile);
                TilePaletteProfileStore.SetPreferred(profile);
                ResetAnalysis();
            }

            EditorGUILayout.LabelField(
                new GUIContent(
                    "Last Build Hash",
                    "Fingerprint of the last successful build."),
                new GUIContent(
                    string.IsNullOrEmpty(profile.LastBuildHash)
                        ? "Not built by Studio"
                        : profile.LastBuildHash));
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Main Actions",
                    "Analyze, build, or validate this Palette."),
                EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(
                    new GUIContent(
                        "Analyze Layout",
                        "Uses the local Ollama vision model to create one final layout."),
                    GUILayout.Height(28f)))
                AnalyzeLayout();

            if (GUILayout.Button(
                    new GUIContent(
                        "Build / Update Palette",
                        "Applies the analyzed layout, or repairs the saved Profile layout."),
                    GUILayout.Height(28f)))
                ApplyBuild();

            if (GUILayout.Button(
                    new GUIContent(
                        "Validate",
                        "Checks the Profile, Tile assets, and Palette cells."),
                    GUILayout.Height(28f)))
                ValidateLayout();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAnalysisProgress()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "Local Vision Analysis",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Model",
                OllamaVisionClient.Model);

            Rect progressRect = GUILayoutUtility.GetRect(
                18f,
                18f,
                GUILayout.ExpandWidth(true));

            int currentBatch =
                analysisProgress?.CurrentBatch ?? 0;
            int batchCount =
                analysisProgress?.BatchCount ?? 0;
            float progress =
                batchCount <= 0
                    ? 0f
                    : Mathf.Clamp01(
                        currentBatch /
                        (float)batchCount);

            string label;
            if (analysisProgress == null)
                label = "Preparing analysis...";
            else if (batchCount > 0)
                label =
                    $"Batch {currentBatch}/{batchCount}";
            else
                label =
                    StageLabel(analysisProgress.Stage);

            EditorGUI.ProgressBar(
                progressRect,
                progress,
                label);

            if (!string.IsNullOrWhiteSpace(
                    analysisProgress?.Message))
                EditorGUILayout.LabelField(
                    analysisProgress.Message,
                    EditorStyles.miniLabel);

            EditorGUI.BeginDisabledGroup(
                activeAnalyzer == null);

            if (GUILayout.Button(
                    new GUIContent(
                        "Cancel Analysis",
                        "Stops the current local model request and keeps the last successful preview."),
                    GUILayout.Height(24f)))
            {
                if (analysisProgress != null)
                    analysisProgress.Message =
                        "Cancelling analysis...";
                activeAnalyzer?.Cancel();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawPreview()
        {
            if (analyzedLayout == null ||
                analyzedLayout.placements.Count == 0)
                return;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Layout Preview",
                    "The latest successful layout produced by visual analysis."),
                EditorStyles.boldLabel);

            if (previewIsStale)
            {
                EditorGUILayout.HelpBox(
                    "The latest analysis did not complete. " +
                    "This preview is from the previous successful analysis.",
                    MessageType.Warning);
            }

            EditorGUILayout.LabelField(
                "Confidence",
                analyzedLayout.confidence.ToString("P0"));

            foreach (
                IGrouping<string, AnalyzedLayoutPlacement> group
                in analyzedLayout.placements
                    .GroupBy(
                        value =>
                            value.groupName + "/" +
                            value.subgroupName,
                        StringComparer.Ordinal)
                    .OrderBy(
                        value => value.Key,
                        StringComparer.Ordinal))
            {
                EditorGUILayout.LabelField(
                    group.Key,
                    EditorStyles.miniBoldLabel);

                BoundsInt bounds =
                    TilePaletteProfileUtility.CalculateBounds(
                        group.Select(
                            value =>
                                value.localPosition));

                float cell = Mathf.Clamp(
                    420f /
                    Mathf.Max(1, bounds.size.x),
                    24f,
                    42f);

                Rect area = GUILayoutUtility.GetRect(
                    bounds.size.x * cell,
                    bounds.size.y * cell);

                foreach (
                    AnalyzedLayoutPlacement placement
                    in group)
                {
                    int x =
                        placement.localPosition.x -
                        bounds.xMin;
                    int y =
                        bounds.yMax - 1 -
                        placement.localPosition.y;

                    Rect tileRect = new Rect(
                        area.x + x * cell,
                        area.y + y * cell,
                        cell - 2f,
                        cell - 2f);

                    Texture preview =
                        AssetPreview.GetAssetPreview(
                            placement.sprite) ??
                        AssetPreview.GetMiniThumbnail(
                            placement.sprite);

                    if (preview != null)
                        GUI.DrawTexture(
                            tileRect,
                            preview,
                            ScaleMode.ScaleToFit,
                            true);

                    GUI.Box(
                        tileRect,
                        new GUIContent(
                            string.Empty,
                            placement.sourceId));
                }
            }
        }

        private void AnalyzeLayout()
        {
            if (busy) return;

            try
            {
                SourceScanResult sourceScan =
                    TilePaletteSourceScanner.Scan(
                        profile.SourceFolderPath,
                        true);

                if (!sourceScan.IsValid)
                    throw new InvalidOperationException(
                        string.Join(
                            "\n",
                            sourceScan.errors));

                busy = true;
                analysisProgress =
                    new VisionAnalysisProgress
                    {
                        Stage =
                            VisionAnalysisStage.CheckingRuntime,
                        Message =
                            "Checking local Ollama runtime..."
                    };

                activeAnalyzer =
                    new TilePaletteVisionAnalyzer();
                TilePaletteVisionAnalyzer analyzer =
                    activeAnalyzer;

                analyzer.Analyze(
                    new InferenceRequest
                    {
                        sources = sourceScan,
                        customTemplates =
                            profile.CustomTemplates
                    },
                    progress =>
                    {
                        if (activeAnalyzer != analyzer)
                            return;

                        analysisProgress = progress;
                        Repaint();
                    },
                    result =>
                    {
                        if (activeAnalyzer != analyzer)
                            return;

                        busy = false;
                        activeAnalyzer = null;
                        analysisProgress = null;

                        switch (result.Status)
                        {
                            case VisionAnalysisStatus.Success:
                                analyzedLayout =
                                    result.Layout;
                                previewIsStale = false;
                                Debug.Log(
                                    "[TilePalette] 分析完成（本地 Ollama）");

                                if (analyzedLayout != null &&
                                    analyzedLayout.confidence <
                                    0.65f)
                                {
                                    Debug.LogWarning(
                                        "[TilePalette] 视觉布局置信度较低，请检查二维预览。");
                                }
                                break;

                            case VisionAnalysisStatus.Cancelled:
                                previewIsStale =
                                    analyzedLayout != null;
                                Debug.LogWarning(
                                    "[TilePalette] 分析已取消，已保留上一次成功预览。");
                                break;

                            default:
                                previewIsStale =
                                    analyzedLayout != null;
                                Debug.LogError(
                                    "[TilePalette] 分析失败：" +
                                    (string.IsNullOrWhiteSpace(
                                         result.Error)
                                        ? "本地视觉模型未返回有效布局。"
                                        : result.Error));
                                break;
                        }

                        Repaint();
                    });
            }
            catch (Exception exception)
            {
                busy = false;
                activeAnalyzer = null;
                analysisProgress = null;
                previewIsStale =
                    analyzedLayout != null;
                Debug.LogError(
                    "[TilePalette] 分析失败：" +
                    exception.Message);
                Repaint();
            }
        }

        private void ValidateLayout()
        {
            RunAction(
                "验证",
                () =>
                {
                    LayoutPlan validationPlan =
                        TilePaletteBuilder.CreatePlan(
                            profile);

                    TilePaletteBuilder.ValidateComplete(
                        profile);

                    string details = string.Join(
                        "\n",
                        validationPlan.items
                            .Where(
                                item =>
                                    item.action ==
                                    LayoutPlanAction.Orphan)
                            .Select(
                                item =>
                                    item.diagnostic)
                            .Where(
                                value =>
                                    !string.IsNullOrWhiteSpace(
                                        value))
                            .Distinct(
                                StringComparer.Ordinal));

                    if (string.IsNullOrWhiteSpace(
                            details))
                        Debug.Log(
                            "[TilePalette] 验证通过");
                    else
                        Debug.LogWarning(
                            "[TilePalette] 验证发现问题：\n" +
                            details);
                });
        }

        private void ApplyBuild()
        {
            RunAction(
                "构建",
                () =>
                {
                    if (analyzedLayout != null &&
                        previewIsStale &&
                        !EditorUtility.DisplayDialog(
                            "Use Previous Analysis?",
                            "The latest analysis failed or was cancelled. " +
                            "The current preview is from the previous successful analysis. " +
                            "Build using that previous layout?",
                            "Use Previous Layout",
                            "Cancel"))
                        return;

                    string profileBefore =
                        EditorJsonUtility.ToJson(profile);

                    try
                    {
                        if (analyzedLayout != null)
                        {
                            SourceScanResult currentScan =
                                TilePaletteSourceScanner.Scan(
                                    profile.SourceFolderPath,
                                    true);

                            if (!currentScan.IsValid)
                                throw new InvalidOperationException(
                                    string.Join(
                                        "\n",
                                        currentScan.errors));

                            Undo.RecordObject(
                                profile,
                                "Apply Visual Tile Layout");

                            TilePaletteProfileUtility
                                .ApplyAnalyzedLayout(
                                    profile,
                                    analyzedLayout,
                                    currentScan.sprites.Select(
                                        value =>
                                            value.resourceId),
                                    true);

                            using (
                                TilePaletteAutoSyncGuard
                                    .Suppress())
                                AssetDatabase.SaveAssets();
                        }
                        else if (!profile.Groups
                                     .SelectMany(
                                         group =>
                                             group.entries)
                                     .Any(
                                         entry =>
                                             entry.included))
                        {
                            throw new InvalidOperationException(
                                "Profile 还没有布局。请先点击 Analyze Layout。");
                        }

                        LayoutPlan preview =
                            TilePaletteBuilder.CreatePlan(
                                profile);

                        if (preview.HasBlockingConflicts)
                            throw new InvalidOperationException(
                                string.Join(
                                    "\n",
                                    preview.BlockingConflicts
                                        .Select(
                                            item =>
                                                item.diagnostic)
                                        .Distinct(
                                            StringComparer.Ordinal)));

                        if (preview.HasConfirmedMoves &&
                            !EditorUtility.DisplayDialog(
                                "Confirm Layout Changes",
                                "The saved Profile requires Tile swaps or cyclic moves. Continue?",
                                "Build",
                                "Cancel"))
                        {
                            RestoreProfile(profileBefore);
                            return;
                        }

                        TilePaletteBuilder.Apply(
                            profile,
                            false);
                        Debug.Log(
                            "[TilePalette] 构建完成");
                    }
                    catch
                    {
                        RestoreProfile(profileBefore);
                        throw;
                    }
                });
        }

        private void RestoreProfile(string json)
        {
            EditorJsonUtility.FromJsonOverwrite(
                json,
                profile);
            EditorUtility.SetDirty(profile);

            using (TilePaletteAutoSyncGuard.Suppress())
                AssetDatabase.SaveAssets();
        }

        private void RunAction(
            string operation,
            Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[TilePalette] {operation}失败：" +
                    exception.Message);
            }
            finally
            {
                Repaint();
            }
        }

        private void ResetAnalysis()
        {
            analyzedLayout = null;
            previewIsStale = false;
            analysisProgress = null;
        }

        private static string StageLabel(
            VisionAnalysisStage stage)
        {
            switch (stage)
            {
                case VisionAnalysisStage.CheckingRuntime:
                    return "Checking Ollama...";
                case VisionAnalysisStage.Preparing:
                    return "Preparing analysis...";
                case VisionAnalysisStage.PreparingBatch:
                    return "Preparing batch...";
                case VisionAnalysisStage.RunningBatch:
                    return "Running local model...";
                case VisionAnalysisStage.ParsingBatch:
                    return "Validating response...";
                case VisionAnalysisStage.Completed:
                    return "Completed";
                default:
                    return "Analyzing...";
            }
        }
    }
}
