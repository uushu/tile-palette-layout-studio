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
        public void OllamaClient_BuildsGenerateRequestWithImageAndSchema()
        {
            const string schema =
                "{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}}}";

            string json = OllamaVisionClient.BuildGenerateRequestJson(
                "system",
                "prompt",
                new byte[] { 1, 2, 3 },
                schema);

            Assert.That(OllamaVisionClient.Model, Is.EqualTo("qwen2.5vl:7b"));
            Assert.That(json, Does.Contain("\"model\":\"qwen2.5vl:7b\""));
            Assert.That(json, Does.Contain("\"images\":[\"AQID\"]"));
            Assert.That(json, Does.Contain("\"stream\":false"));
            Assert.That(json, Does.Contain("\"think\":false"));
            Assert.That(json, Does.Contain("\"temperature\":0.0"));
            Assert.That(json, Does.Contain("\"num_ctx\":4096"));
            Assert.That(json, Does.Contain("\"num_predict\":2048"));
            Assert.That(json, Does.Contain("\"format\":{\"type\":\"object\""));
        }

        [Test]
        public void OllamaClient_DetectsRequiredModelInTags()
        {
            string json =
                "{\"models\":[" +
                "{\"name\":\"qwen2.5vl:7b\",\"model\":\"qwen2.5vl:7b\"}," +
                "{\"name\":\"nomic-embed-text:latest\",\"model\":\"nomic-embed-text:latest\"}" +
                "]}";

            Assert.That(
                OllamaVisionClient.HasRequiredModel(json),
                Is.True);
            Assert.That(
                OllamaVisionClient.HasRequiredModel("{\"models\":[]}"),
                Is.False);
        }

        [Test]
        public void OllamaClient_ParsesGenerateResponse()
        {
            string json =
                "{\"response\":\"{\\\"confidence\\\":1,\\\"placements\\\":[]}\"," +
                "\"done\":true," +
                "\"done_reason\":\"stop\"," +
                "\"total_duration\":1200000," +
                "\"load_duration\":300000}";

            OllamaGenerateResult result =
                OllamaVisionClient.ParseGenerateResponse(json);

            Assert.That(result.Status, Is.EqualTo(OllamaClientStatus.Success));
            Assert.That(result.Content, Does.Contain("\"confidence\":1"));
            Assert.That(result.DoneReason, Is.EqualTo("stop"));
            Assert.That(result.TotalDurationNanoseconds, Is.EqualTo(1200000));
            Assert.That(result.LoadDurationNanoseconds, Is.EqualTo(300000));
        }

        [Test]
        public void OllamaClient_ExpandsPositionalResponse()
        {
            string expanded = OllamaVisionClient.ExpandPositionalResponse(
                "{\"c\":0.9,\"p\":[" +
                "{\"g\":0,\"s\":0,\"x\":2,\"y\":-1}," +
                "{\"g\":1,\"s\":0,\"x\":0,\"y\":0}]}",
                new[] { "T0003", "T0008" });

            Assert.That(expanded, Does.Contain("\"id\":\"T0003\""));
            Assert.That(expanded, Does.Contain("\"id\":\"T0008\""));
            Assert.That(expanded, Does.Contain("\"group\":\"g0\""));
            Assert.That(expanded, Does.Contain("\"group\":\"g1\""));
            Assert.That(expanded, Does.Contain("\"x\":2"));
            Assert.That(expanded, Does.Contain("\"y\":-1"));
        }

        [Test]
        public void ManagedCells_DoNotIncludeLayoutHoles()
        {
            TilePaletteProfile profile =
                ScriptableObject.CreateInstance<TilePaletteProfile>();
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
        public void VisionAnalyzer_KeepsSmallNameGroupsTogether()
        {
            List<SourceSpriteInfo> sources = Enumerable
                .Range(1, 20)
                .Select(index => Source("first", index))
                .Concat(
                    Enumerable.Range(21, 20)
                        .Select(index => Source("second", index)))
                .ToList();

            IReadOnlyList<VisionAnalysisBatch> batches =
                TilePaletteVisionAnalyzer.BuildAnalysisBatches(sources);

            Assert.That(batches, Has.Count.EqualTo(2));
            Assert.That(batches[0].NewSources, Has.Count.EqualTo(20));
            Assert.That(batches[1].NewSources, Has.Count.EqualTo(20));
            Assert.That(
                batches[0].NewSources.All(source =>
                    source.groupName == "first"),
                Is.True);
            Assert.That(
                batches[1].NewSources.All(source =>
                    source.groupName == "second"),
                Is.True);
            Assert.That(batches[0].RequiresAnchors, Is.False);
            Assert.That(batches[1].RequiresAnchors, Is.True);
            Assert.That(
                batches[1].ContinuationKey,
                Is.EqualTo(TilePaletteVisionAnalyzer.GlobalContinuationKey));
        }

        [Test]
        public void VisionAnalyzer_SplitsLargeGroupsWithGlobalContinuationCapacity()
        {
            List<SourceSpriteInfo> sources = Enumerable
                .Range(1, 73)
                .Select(index => Source("large", index))
                .ToList();

            IReadOnlyList<VisionAnalysisBatch> batches =
                TilePaletteVisionAnalyzer.BuildAnalysisBatches(sources);

            Assert.That(batches, Has.Count.EqualTo(3));
            Assert.That(batches[0].NewSources, Has.Count.EqualTo(30));
            Assert.That(batches[0].RequiresAnchors, Is.False);
            Assert.That(batches[1].NewSources, Has.Count.EqualTo(24));
            Assert.That(batches[1].RequiresAnchors, Is.True);
            Assert.That(batches[2].NewSources, Has.Count.EqualTo(19));
            Assert.That(batches[2].RequiresAnchors, Is.True);
            Assert.That(
                batches.Skip(1).All(batch =>
                    batch.ContinuationKey ==
                    TilePaletteVisionAnalyzer.GlobalContinuationKey),
                Is.True);
            Assert.That(
                batches.Sum(batch => batch.NewSources.Count),
                Is.EqualTo(73));
        }

        [Test]
        public void VisionAnalyzer_UsesGlobalContinuationAcrossUnrelatedHints()
        {
            List<SourceSpriteInfo> sources = Enumerable
                .Range(1, 70)
                .Select(index => Source("singleton-" + index, index))
                .ToList();

            IReadOnlyList<VisionAnalysisBatch> batches =
                TilePaletteVisionAnalyzer.BuildAnalysisBatches(sources);

            Assert.That(batches, Has.Count.EqualTo(3));
            Assert.That(batches[0].NewSources, Has.Count.EqualTo(30));
            Assert.That(batches[1].NewSources, Has.Count.EqualTo(24));
            Assert.That(batches[2].NewSources, Has.Count.EqualTo(16));
            Assert.That(batches[0].RequiresAnchors, Is.False);
            Assert.That(
                batches.Skip(1).All(batch => batch.RequiresAnchors),
                Is.True);
            Assert.That(
                batches.Skip(1).All(batch =>
                    batch.ContinuationKey ==
                    TilePaletteVisionAnalyzer.GlobalContinuationKey),
                Is.True);
        }

        [Test]
        public void VisionAnalyzer_ShufflesContactSheetOrderDeterministically()
        {
            List<SourceSpriteInfo> sources = Enumerable
                .Range(1, 12)
                .Select(index => Source("group", index))
                .ToList();

            string[] first = TilePaletteVisionAnalyzer
                .OrderContactSheetSources(sources, 0)
                .Select(source => source.analysisId)
                .ToArray();
            string[] second = TilePaletteVisionAnalyzer
                .OrderContactSheetSources(sources, 0)
                .Select(source => source.analysisId)
                .ToArray();
            string[] original = sources
                .Select(source => source.analysisId)
                .ToArray();

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(original));
            Assert.That(
                first.OrderBy(value => value, StringComparer.Ordinal),
                Is.EqualTo(original.OrderBy(value => value, StringComparer.Ordinal)));
        }

        [Test]
        public void VisionAnalyzer_ResponseSchemaRestrictsIdsAndCount()
        {
            string schema =
                TilePaletteVisionAnalyzer.BuildResponseSchema(
                    new[]
                    {
                        Source("group", 1),
                        Source("group", 2)
                    });

            Assert.That(schema, Does.Contain("\"placements\""));
            Assert.That(schema, Does.Contain("\"enum\":[\"T0001\",\"T0002\"]"));
            Assert.That(schema, Does.Contain("\"minItems\":2"));
            Assert.That(schema, Does.Contain("\"maxItems\":2"));
            Assert.That(schema, Does.Contain("\"uniqueItems\":true"));
            Assert.That(schema, Does.Contain("\"additionalProperties\":false"));
            Assert.That(schema, Does.Not.Contain("T0003"));
        }

        [Test]
        public void VisionAnalyzer_ParsesSimplifiedStructuredOutput()
        {
            SourceScanResult sources = new SourceScanResult();
            sources.sprites.Add(Source("group", 1));
            sources.sprites.Add(Source("group", 2));

            string json =
                "{\"confidence\":0.9,\"placements\":[" +
                "{\"id\":\"T0001\",\"group\":\"object\",\"subgroup\":\"main\",\"x\":0,\"y\":0}," +
                "{\"id\":\"T0002\",\"group\":\"object\",\"subgroup\":\"main\",\"x\":1,\"y\":0}" +
                "]}";

            AnalyzedLayout layout =
                TilePaletteVisionAnalyzer.ParseLayoutText(json, sources);

            Assert.That(layout.placements, Has.Count.EqualTo(2));
            Assert.That(layout.confidence, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(
                layout.placements.Single(
                    value => value.resourceId == "resource-2").localPosition,
                Is.EqualTo(new Vector3Int(1, 0, 0)));
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
        public void VisionAnalyzer_NextGroupIndexAdvancesPastExistingGroups()
        {
            AnalyzedLayout layout = new AnalyzedLayout
            {
                placements = new List<AnalyzedLayoutPlacement>
                {
                    Placement("a", "g0", Vector3Int.zero),
                    Placement("b", "g3", Vector3Int.right),
                    Placement("c", "custom", Vector3Int.up)
                }
            };

            Assert.That(
                TilePaletteVisionAnalyzer.NextGroupIndex(layout),
                Is.EqualTo(4));
        }

        [Test]
        public void ContactSheet_CompositeOverAlwaysProducesOpaquePixel()
        {
            Color32 background = new Color32(32, 35, 40, 255);

            Color32 transparent =
                TilePaletteContactSheetRenderer.CompositeOver(
                    new Color32(255, 0, 0, 0),
                    background);
            Color32 half =
                TilePaletteContactSheetRenderer.CompositeOver(
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
                "{\"confidence\":1,\"placements\":[" +
                "{\"id\":\"T0001\",\"group\":\"group\",\"subgroup\":\"main\",\"x\":0,\"y\":0}]}";

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() =>
                    TilePaletteVisionAnalyzer.ParseLayoutText(
                        json,
                        sources));

            Assert.That(
                exception.Message,
                Does.Contain("Missing 9 Sprite IDs"));
            Assert.That(
                exception.Message,
                Does.Contain("(+1 more)"));
            Assert.That(
                exception.Message,
                Does.Not.Contain("T0010"));
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
