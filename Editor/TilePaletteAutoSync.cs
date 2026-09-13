using System;
using System.Collections.Generic;
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
            return new SuppressionScope();
        }

        public static void SaveAssetsWithoutSync()
        {
            using (Suppress())
                AssetDatabase.SaveAssets();
        }

        private sealed class SuppressionScope : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                suppressionDepth--;
            }
        }
    }

    internal sealed class TilePaletteSaveProcessor : AssetModificationProcessor
    {
        private static readonly HashSet<string> PendingPalettePaths = new HashSet<string>(StringComparer.Ordinal);
        private static bool scheduled;

        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (!ShouldScheduleSync(paths)) return paths;
            HashSet<string> saved = new HashSet<string>(paths, StringComparer.Ordinal);
            foreach (TilePaletteProfile profile in TilePaletteProfileStore.FindAll())
            {
                if (!string.IsNullOrWhiteSpace(profile.PalettePrefabPath) && saved.Contains(profile.PalettePrefabPath))
                    PendingPalettePaths.Add(profile.PalettePrefabPath);
            }

            if (PendingPalettePaths.Count > 0 && !scheduled)
            {
                scheduled = true;
                EditorApplication.delayCall += SyncSavedPalettes;
            }
            return paths;
        }

        internal static bool ShouldScheduleSync(string[] paths)
        {
            if (TilePaletteAutoSyncGuard.IsSuppressed || paths == null || paths.Length == 0) return false;
            HashSet<string> saved = new HashSet<string>(paths, StringComparer.Ordinal);
            return TilePaletteProfileStore.FindAll().Any(profile =>
                !string.IsNullOrWhiteSpace(profile.PalettePrefabPath) &&
                saved.Contains(profile.PalettePrefabPath));
        }

        private static void SyncSavedPalettes()
        {
            scheduled = false;
            string[] paths = PendingPalettePaths.ToArray();
            PendingPalettePaths.Clear();
            bool changed = false;

            foreach (TilePaletteProfile profile in TilePaletteProfileStore.FindAll()
                         .Where(profile => paths.Contains(profile.PalettePrefabPath, StringComparer.Ordinal)))
            {
                try
                {
                    TilePaletteSyncResult result = TilePaletteBuilder.SyncPaletteToProfile(profile);
                    changed |= result.HasChanges;
                }
                catch (Exception exception)
                {
                    Debug.LogError("[TilePalette] 自动同步失败：" + exception.Message);
                }
            }

            if (changed) Debug.Log("[TilePalette] 自动同步完成");
            TilePaletteLayoutStudioWindow.RepaintOpenWindows();
        }
    }
}
