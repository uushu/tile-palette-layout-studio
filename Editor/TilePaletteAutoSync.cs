using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteAutoSyncGuard
    {
        private static int suppressionDepth;
        public static bool IsSuppressed => suppressionDepth > 0;

        public static IDisposable Suppress()
        {
            suppressionDepth++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                suppressionDepth = Math.Max(0, suppressionDepth - 1);
            }
        }
    }

    internal sealed class TilePaletteAutoSyncSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (TilePaletteAutoSyncGuard.IsSuppressed || paths == null || paths.Length == 0)
                return paths;

            HashSet<string> saved = new HashSet<string>(paths.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            foreach (TilePaletteProfile profile in TilePaletteProfileStore.FindAll())
            {
                if (!string.IsNullOrEmpty(profile.PalettePrefabPath) && saved.Contains(Normalize(profile.PalettePrefabPath)))
                    TilePaletteAutoSyncScheduler.Queue(profile);
            }
            return paths;
        }

        private static string Normalize(string path)
        {
            return (path ?? string.Empty)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }

    [InitializeOnLoad]
    internal static class TilePaletteAutoSyncScheduler
    {
        private static bool queued;
        private static readonly HashSet<string> QueuedProfileGuids = new HashSet<string>(StringComparer.Ordinal);

        static TilePaletteAutoSyncScheduler()
        {
        }

        public static void Queue(TilePaletteProfile profile)
        {
            if (profile == null) return;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            if (!string.IsNullOrWhiteSpace(guid)) QueuedProfileGuids.Add(guid);
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            queued = false;
            if (TilePaletteAutoSyncGuard.IsSuppressed ||
                EditorApplication.isCompiling ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            string[] profileGuids = QueuedProfileGuids.ToArray();
            QueuedProfileGuids.Clear();
            foreach (string guid in profileGuids)
            {
                TilePaletteProfile profile = AssetDatabase.LoadAssetAtPath<TilePaletteProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || profile.Groups.Count == 0) continue;
                try
                {
                    TilePaletteAutoSyncResult result = TilePaletteBuilder.SyncManualChangesAutomatically(profile);
                    foreach (string warning in result.warnings.Distinct(StringComparer.Ordinal))
                        Debug.LogWarning(warning);
                    if (result.HasChanges) Debug.Log("[TilePalette] 自动同步完成");
                }
                catch (Exception exception)
                {
                    Debug.LogError("[TilePalette] 自动同步失败：" + exception.Message);
                }
            }
        }
    }
}
