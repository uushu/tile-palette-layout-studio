using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    public sealed class TilePaletteProfile : ScriptableObject
    {
        [SerializeField] private DefaultAsset sourceFolder;
        [SerializeField] private DefaultAsset tileOutputFolder;
        [SerializeField] private GameObject palettePrefab;
        [SerializeField] private List<TileLayoutTemplate> customTemplates = new List<TileLayoutTemplate>();
        [SerializeField] private List<TilePaletteProfileGroup> groups = new List<TilePaletteProfileGroup>();
        [SerializeField] private string lastBuildHash;
        [SerializeField] private long lastBuildUtcTicks;

        public DefaultAsset SourceFolder { get => sourceFolder; set => sourceFolder = value; }
        public DefaultAsset TileOutputFolder { get => tileOutputFolder; set => tileOutputFolder = value; }
        public GameObject PalettePrefab { get => palettePrefab; set => palettePrefab = value; }
        public List<TileLayoutTemplate> CustomTemplates => customTemplates;
        public List<TilePaletteProfileGroup> Groups => groups;
        public string LastBuildHash => lastBuildHash;
        public DateTime LastBuildUtc => lastBuildUtcTicks <= 0 ? DateTime.MinValue : new DateTime(lastBuildUtcTicks, DateTimeKind.Utc);
        public IEnumerable<TilePaletteProfileEntry> IncludedEntries => groups.SelectMany(group => group.entries).Where(entry => entry.included);
        public string SourceFolderPath => sourceFolder == null ? string.Empty : AssetDatabase.GetAssetPath(sourceFolder);
        public string TileOutputFolderPath => tileOutputFolder == null ? string.Empty : AssetDatabase.GetAssetPath(tileOutputFolder);
        public string PalettePrefabPath => palettePrefab == null ? string.Empty : AssetDatabase.GetAssetPath(palettePrefab);

        public void ReplaceGroups(IEnumerable<TilePaletteProfileGroup> newGroups) => groups = newGroups?.ToList() ?? new List<TilePaletteProfileGroup>();
        public void MarkBuilt(string hash) { lastBuildHash = hash; lastBuildUtcTicks = DateTime.UtcNow.Ticks; }
    }
}
