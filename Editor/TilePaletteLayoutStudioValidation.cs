using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    public static class TilePaletteLayoutStudioValidation
    {
        public static void RunAll()
        {
            List<string> report = new List<string>
            {
                "Tile Palette Layout Studio Validation",
                "UTC: " + DateTime.UtcNow.ToString("O"),
                string.Empty
            };

            try
            {
                ValidateNameHints();
                report.Add("PASS Optional name hints");

                ValidateVisionProviders();
                report.Add("PASS Vision provider discovery");

                TilePaletteProfile profile = TilePaletteProfileStore.LoadPreferred();
                if (profile != null && AssetDatabase.IsValidFolder(profile.SourceFolderPath))
                {
                    SourceScanResult scan = TilePaletteSourceScanner.Scan(profile.SourceFolderPath, true);
                    Require(scan.IsValid, "Source scan failed: " + string.Join("; ", scan.errors));
                    Require(scan.sprites.Count > 0, "Source scan returned no sprites.");
                    Require(scan.sprites.Select(value => value.resourceId).Distinct(StringComparer.Ordinal).Count() == scan.sprites.Count,
                        "Stable Sprite resource IDs are not unique.");
                    Require(scan.sprites.Select(value => value.analysisId).Distinct(StringComparer.Ordinal).Count() == scan.sprites.Count,
                        "Temporary visual-analysis IDs are not unique.");
                    report.Add($"PASS Source scan: {scan.sprites.Count} sprites, {scan.warnings.Count} warnings");
                }
                else
                {
                    report.Add("SKIP Source scan: no configured Profile source folder");
                }

                if (profile != null && profile.Groups.Count > 0 && profile.PalettePrefab != null)
                {
                    string profileBefore = EditorJsonUtility.ToJson(profile);
                    LayoutPlan plan = TilePaletteBuilder.CreatePlan(profile);
                    Require(profileBefore == EditorJsonUtility.ToJson(profile), "Read-only validation modified the Profile.");
                    report.Add($"PASS Build plan: keep={plan.Count(LayoutPlanAction.Keep)}, create={plan.Count(LayoutPlanAction.Create)}, place={plan.Count(LayoutPlanAction.Place)}, move={plan.Count(LayoutPlanAction.Move)}, conflict={plan.Count(LayoutPlanAction.Conflict)}");
                }
                else
                {
                    report.Add("SKIP Build plan: Profile layout is not configured");
                }

                report.Add(string.Empty);
                report.Add("PASS");
                WriteReport(report);
                Debug.Log("[TilePalette] 自检通过");
            }
            catch (Exception exception)
            {
                report.Add(string.Empty);
                report.Add("FAIL " + exception.Message);
                WriteReport(report);
                Debug.LogError("[TilePalette] 自检失败：" + exception.Message);
                if (Application.isBatchMode) throw;
            }
        }

        private static void ValidateNameHints()
        {
            RequireParsed("building_part_12", "building_part", 12, string.Empty);
            RequireParsed("structure_corner_left", "structure", -1, "corner_left");
            RequireParsed("unstructured-name", "unstructured-name", -1, "singleton");
        }

        private static void RequireParsed(string value, string expectedGroup, int expectedIndex, string expectedSuffix)
        {
            Require(TileSourceNameParser.TryParse(value, out string group, out int index, out string suffix),
                "Name hint parser rejected " + value);
            Require(group == expectedGroup && index == expectedIndex && suffix == expectedSuffix,
                "Name hint parser returned unexpected data for " + value);
        }

        private static void ValidateVisionProviders()
        {
            Require(TilePaletteVisionProviderRegistry.Providers.Count > 0, "No vision provider was discovered.");
            string duplicate = TilePaletteVisionProviderRegistry.Providers
                .GroupBy(provider => provider.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            Require(string.IsNullOrEmpty(duplicate), "Duplicate vision provider ID: " + duplicate);
            foreach (ITilePaletteVisionProvider provider in TilePaletteVisionProviderRegistry.Providers)
            {
                Require(!string.IsNullOrWhiteSpace(provider.Endpoint), provider.DisplayName + " has no endpoint.");
                Require(!string.IsNullOrWhiteSpace(provider.Model), provider.DisplayName + " has no model ID.");
                Require(!string.IsNullOrWhiteSpace(provider.ApiKeyEnvironment), provider.DisplayName + " has no API Key environment name.");
            }
        }

        private static void WriteReport(IEnumerable<string> lines)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? throw new InvalidOperationException("Cannot resolve project root.");
            string folder = Path.Combine(root, StudioConstants.ReportFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "latest.txt"), lines);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
