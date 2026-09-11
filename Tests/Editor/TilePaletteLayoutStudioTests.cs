using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TilePaletteLayoutStudio.Tests
{
    public sealed class TilePaletteLayoutStudioTests
    {
        [TestCase("object_7", "object", 7)]
        [TestCase("building_part_12", "building_part", 12)]
        public void NameParser_UsesNumericSuffixAsOptionalHint(
            string sourceId,
            string expectedGroup,
            int expectedIndex)
        {
            bool parsed = TileSourceNameParser.TryParse(
                sourceId,
                out string group,
                out int index,
                out string semanticSuffix);

            Assert.That(parsed, Is.True);
            Assert.That(group, Is.EqualTo(expectedGroup));
            Assert.That(index, Is.EqualTo(expectedIndex));
            Assert.That(semanticSuffix, Is.Empty);
        }

        [Test]
        public void AnalyzedLayout_RejectsMissingSprite()
        {
            AnalyzedLayout layout = new AnalyzedLayout
            {
                placements = new List<AnalyzedLayoutPlacement>
                {
                    Placement("sprite-a", "group", Vector3Int.zero)
                }
            };

            bool valid = layout.TryValidate(
                new[] { "sprite-a", "sprite-b" },
                out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("遗漏"));
        }

        [Test]
        public void AnalyzedLayout_RejectsCoordinateCollision()
        {
            AnalyzedLayout layout = new AnalyzedLayout
            {
                placements = new List<AnalyzedLayoutPlacement>
                {
                    Placement("sprite-a", "group", Vector3Int.zero),
                    Placement("sprite-b", "group", Vector3Int.zero)
                }
            };

            bool valid = layout.TryValidate(
                new[] { "sprite-a", "sprite-b" },
                out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("坐标冲突"));
        }

        [Test]
        public void GlmProvider_UsesOfficialChatImageShape()
        {
            ZhipuGlmVisionProvider provider = new ZhipuGlmVisionProvider();
            VisionProviderRequest request = provider.CreateRequest(
                "test-key",
                "layout",
                new[] { new byte[] { 1, 2, 3 } },
                120);

            Assert.That(provider.Model, Is.EqualTo("glm-4.6v-flash"));
            Assert.That(request.Url, Is.EqualTo(provider.Endpoint));
            Assert.That(request.Json, Does.Contain("\"type\":\"image_url\""));
            Assert.That(
                request.Json,
                Does.Contain("\"url\":\"data:image/png;base64,AQID\""));
            Assert.That(request.Headers["Authorization"], Is.EqualTo("Bearer test-key"));
            Assert.That(request.TimeoutSeconds, Is.EqualTo(120));
        }

        [Test]
        public void ManagedCells_DoNotIncludeLayoutHoles()
        {
            TilePaletteProfile profile = ScriptableObject.CreateInstance<TilePaletteProfile>();
            try
            {
                profile.ReplaceGroups(new[]
                {
                    new TilePaletteProfileGroup
                    {
                        groupName = "shape",
                        subgroupName = "main",
                        entries = new List<TilePaletteProfileEntry>
                        {
                            new TilePaletteProfileEntry
                            {
                                included = true,
                                localPosition = new Vector3Int(0, 0, 0)
                            },
                            new TilePaletteProfileEntry
                            {
                                included = true,
                                localPosition = new Vector3Int(2, 0, 0)
                            }
                        }
                    }
                });

                Assert.That(
                    TilePaletteProfileUtility.IsManagedCell(
                        profile,
                        new Vector3Int(0, 0, 0)),
                    Is.True);
                Assert.That(
                    TilePaletteProfileUtility.IsManagedCell(
                        profile,
                        new Vector3Int(1, 0, 0)),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProviderRegistry_DiscoversGlmPlugin()
        {
            Assert.That(
                TilePaletteVisionProviderRegistry.Providers.Any(
                    provider => provider is ZhipuGlmVisionProvider),
                Is.True);
        }

        [Test]
        public void VisionAnalyzer_KeepsSmallNameGroupsTogether()
        {
            List<SourceSpriteInfo> sources = Enumerable
                .Range(1, 20)
                .Select(index => Source("first", index))
                .Concat(
                    Enumerable.Range(21, 20)
                        .Select(index => Source("second", index)))
                .ToList();

            IReadOnlyList<IReadOnlyList<SourceSpriteInfo>> batches =
                TilePaletteVisionAnalyzer.BuildBatches(sources);

            Assert.That(batches, Has.Count.EqualTo(2));
            Assert.That(batches.All(batch => batch.Count == 20), Is.True);
            Assert.That(
                batches.Any(batch =>
                    batch.All(source => source.groupName == "first")),
                Is.True);
            Assert.That(
                batches.Any(batch =>
                    batch.All(source => source.groupName == "second")),
                Is.True);
        }

        [Test]
        public void VisionAnalyzer_SplitsLargeGroupsWithContinuationCapacity()
        {
            List<SourceSpriteInfo> sources = Enumerable
                .Range(1, 73)
                .Select(index => Source("large", index))
                .ToList();

            IReadOnlyList<VisionAnalysisBatch> batches =
                TilePaletteVisionAnalyzer.BuildAnalysisBatches(sources);

            Assert.That(batches, Has.Count.EqualTo(3));
            Assert.That(batches[0].NewSources, Has.Count.EqualTo(36));
            Assert.That(batches[0].RequiresAnchors, Is.False);
            Assert.That(batches[1].NewSources, Has.Count.EqualTo(30));
            Assert.That(batches[1].RequiresAnchors, Is.True);
            Assert.That(batches[2].NewSources, Has.Count.EqualTo(7));
            Assert.That(batches[2].RequiresAnchors, Is.True);
            Assert.That(
                batches.Skip(1).All(batch => batch.ContinuationKey == "large"),
                Is.True);
            Assert.That(
                batches.Sum(batch => batch.NewSources.Count),
                Is.EqualTo(73));
        }

        [Test]
        public void VisionAnalyzer_RejectsMovedContinuationAnchor()
        {
            SourceSpriteInfo source = Source("large", 1);
            source.resourceId = "resource-anchor";
            source.sourceId = "anchor";
            VisionAnchor anchor = new VisionAnchor(
                source,
                "object",
                "main",
                Vector3Int.zero);
            AnalyzedLayout batch = new AnalyzedLayout
            {
                placements = new List<AnalyzedLayoutPlacement>
                {
                    new AnalyzedLayoutPlacement
                    {
                        resourceId = source.resourceId,
                        sourceId = source.sourceId,
                        groupName = "object",
                        subgroupName = "main",
                        localPosition = new Vector3Int(1, 0, 0)
                    }
                }
            };

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() =>
                    TilePaletteVisionAnalyzer.ValidateAnchors(
                        batch,
                        new[] { anchor }));

            Assert.That(exception.Message, Does.Contain("anchor moved"));
        }

        [Test]
        public void VisionAnalyzer_MergesContinuationWithoutBatchRenaming()
        {
            AnalyzedLayout target = new AnalyzedLayout
            {
                placements = new List<AnalyzedLayoutPlacement>
                {
                    new AnalyzedLayoutPlacement
                    {
                        resourceId = "resource-anchor",
                        sourceId = "anchor",
                        groupName = "object",
                        subgroupName = "main",
                        localPosition = Vector3Int.zero
                    }
                }
            };
            AnalyzedLayout batch = new AnalyzedLayout
            {
                placements = new List<AnalyzedLayoutPlacement>
                {
                    new AnalyzedLayoutPlacement
                    {
                        resourceId = "resource-anchor",
                        sourceId = "anchor",
                        groupName = "object",
                        subgroupName = "main",
                        localPosition = Vector3Int.zero
                    },
                    new AnalyzedLayoutPlacement
                    {
                        resourceId = "resource-new",
                        sourceId = "new",
                        groupName = "object",
                        subgroupName = "main",
                        localPosition = new Vector3Int(1, 0, 0)
                    }
                }
            };

            TilePaletteVisionAnalyzer.MergeBatch(
                target,
                batch,
                1,
                new[] { "resource-anchor" },
                true);

            Assert.That(target.placements, Has.Count.EqualTo(2));
            Assert.That(
                target.placements.Single(value =>
                    value.resourceId == "resource-new").subgroupName,
                Is.EqualTo("main"));
            Assert.That(
                target.placements.Any(value =>
                    value.subgroupName.Contains("_batch_")),
                Is.False);
        }

        [Test]
        public void ContactSheet_CompositeOverAlwaysProducesOpaquePixel()
        {
            Color32 background = new Color32(32, 35, 40, 255);

            Color32 transparent = TilePaletteContactSheetRenderer.CompositeOver(
                new Color32(255, 0, 0, 0),
                background);
            Color32 half = TilePaletteContactSheetRenderer.CompositeOver(
                new Color32(255, 0, 0, 128),
                background);

            Assert.That(transparent.a, Is.EqualTo(255));
            Assert.That(transparent.r, Is.EqualTo(background.r));
            Assert.That(transparent.g, Is.EqualTo(background.g));
            Assert.That(transparent.b, Is.EqualTo(background.b));
            Assert.That(half.a, Is.EqualTo(255));
            Assert.That(half.r, Is.GreaterThan(background.r));
        }

        [Test]
        public void VisionAnalyzer_ReportsMissingIdsBriefly()
        {
            SourceScanResult sources = new SourceScanResult();
            sources.sprites.AddRange(
                Enumerable.Range(1, 10)
                    .Select(index =>
                    {
                        SourceSpriteInfo source = Source("group", index);
                        source.resourceId = "resource-" + index;
                        source.sourceId = "sprite-" + index;
                        return source;
                    }));
            string json =
                "{\"confidence\":1,\"groups\":[{\"group\":\"group\"," +
                "\"subgroup\":\"main\",\"width\":1,\"height\":1," +
                "\"entries\":[{\"id\":\"T0001\",\"x\":0,\"y\":0}]}]}";

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() =>
                    TilePaletteVisionAnalyzer.ParseLayoutText(json, sources));

            Assert.That(exception.Message, Does.Contain("Missing 9 Sprite IDs"));
            Assert.That(exception.Message, Does.Contain("(+1 more)"));
            Assert.That(exception.Message, Does.Not.Contain("T0010"));
        }

        private static AnalyzedLayoutPlacement Placement(
            string resourceId,
            string group,
            Vector3Int position) =>
            new AnalyzedLayoutPlacement
            {
                resourceId = resourceId,
                groupName = group,
                subgroupName = "main",
                localPosition = position
            };

        private static SourceSpriteInfo Source(
            string groupName,
            int index) =>
            new SourceSpriteInfo
            {
                resourceId = "resource-" + index,
                sourceId = "sprite-" + index,
                analysisId = "T" + index.ToString("D4"),
                groupName = groupName
            };
    }
}
