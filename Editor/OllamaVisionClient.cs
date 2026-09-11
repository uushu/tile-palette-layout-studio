using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace TilePaletteLayoutStudio
{
    internal enum OllamaClientStatus
    {
        Success,
        Cancelled,
        RuntimeUnavailable,
        ModelMissing,
        RequestFailed,
        InvalidResponse
    }

    internal sealed class OllamaReadyResult
    {
        public OllamaClientStatus Status;
        public string Error = string.Empty;
        public long ElapsedMilliseconds;
    }

    internal sealed class OllamaGenerateResult
    {
        public OllamaClientStatus Status;
        public string Content = string.Empty;
        public string Error = string.Empty;
        public string DoneReason = string.Empty;
        public long ElapsedMilliseconds;
        public long TotalDurationNanoseconds;
        public long LoadDurationNanoseconds;
        public long PromptEvalCount;
        public long PromptEvalDurationNanoseconds;
        public long EvalCount;
        public long EvalDurationNanoseconds;
    }

    internal static class OllamaVisionClient
    {
        internal const string BaseUrl = "http://127.0.0.1:11434";
        internal const string Model = "qwen2.5vl:7b";
        internal const int ReadyTimeoutSeconds = 5;
        internal const int GenerateTimeoutSeconds = 120;
        internal const int ContextWindow = 4096;
        internal const int MaximumPredictedTokens = 2048;
        private const string KeepAlive = "2m";

        private static string TagsUrl => BaseUrl + "/api/tags";
        private static string GenerateUrl => BaseUrl + "/api/generate";

        internal static void CheckReady(
            Func<bool> cancellationRequested,
            Action<OllamaReadyResult> completed)
        {
            TilePaletteVisionHttpClient.Send(
                TagsUrl,
                UnityWebRequest.kHttpVerbGET,
                string.Empty,
                ReadyTimeoutSeconds,
                cancellationRequested,
                result =>
                {
                    if (result.Status == HttpRequestStatus.Cancelled)
                    {
                        completed(new OllamaReadyResult
                        {
                            Status = OllamaClientStatus.Cancelled,
                            Error = "Analysis cancelled.",
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                        return;
                    }

                    if (result.Status != HttpRequestStatus.Success)
                    {
                        completed(new OllamaReadyResult
                        {
                            Status = OllamaClientStatus.RuntimeUnavailable,
                            Error = string.IsNullOrWhiteSpace(result.Error)
                                ? "Cannot connect to the local Ollama service."
                                : result.Error,
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                        return;
                    }

                    try
                    {
                        if (!HasRequiredModel(result.Body))
                        {
                            completed(new OllamaReadyResult
                            {
                                Status = OllamaClientStatus.ModelMissing,
                                Error = "Required Ollama model is not installed: " + Model,
                                ElapsedMilliseconds = result.ElapsedMilliseconds
                            });
                            return;
                        }

                        completed(new OllamaReadyResult
                        {
                            Status = OllamaClientStatus.Success,
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                    }
                    catch (Exception exception)
                    {
                        completed(new OllamaReadyResult
                        {
                            Status = OllamaClientStatus.InvalidResponse,
                            Error = "Cannot parse Ollama model list: " + exception.Message,
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                    }
                });
        }

        internal static void Generate(
            string systemPrompt,
            string prompt,
            byte[] pngBytes,
            string responseSchemaJson,
            Func<bool> cancellationRequested,
            Action<OllamaGenerateResult> completed)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                completed(new OllamaGenerateResult
                {
                    Status = OllamaClientStatus.RequestFailed,
                    Error = "Contact sheet image is empty."
                });
                return;
            }

            string[] orderedIds = ExtractOrderedIds(responseSchemaJson);
            string json;
            try
            {
                json = BuildGenerateRequestJson(
                    systemPrompt,
                    prompt,
                    pngBytes,
                    responseSchemaJson);
            }
            catch (Exception exception)
            {
                completed(new OllamaGenerateResult
                {
                    Status = OllamaClientStatus.RequestFailed,
                    Error = "Cannot build Ollama request: " + exception.Message
                });
                return;
            }

            TilePaletteVisionHttpClient.Send(
                GenerateUrl,
                UnityWebRequest.kHttpVerbPOST,
                json,
                GenerateTimeoutSeconds,
                cancellationRequested,
                result =>
                {
                    if (result.Status == HttpRequestStatus.Cancelled)
                    {
                        completed(new OllamaGenerateResult
                        {
                            Status = OllamaClientStatus.Cancelled,
                            Error = "Analysis cancelled.",
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                        return;
                    }

                    if (result.Status != HttpRequestStatus.Success)
                    {
                        completed(new OllamaGenerateResult
                        {
                            Status = OllamaClientStatus.RequestFailed,
                            Error = string.IsNullOrWhiteSpace(result.Error)
                                ? "Ollama request failed."
                                : result.Error,
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                        return;
                    }

                    try
                    {
                        OllamaGenerateResult parsed =
                            ParseGenerateResponse(result.Body);
                        parsed.ElapsedMilliseconds = result.ElapsedMilliseconds;

                        if (parsed.Status == OllamaClientStatus.Success &&
                            orderedIds.Length > 0)
                        {
                            parsed.Content = ExpandPositionalResponse(
                                parsed.Content,
                                orderedIds);
                        }

                        Debug.Log(
                            "[TilePalette] Ollama timing: " +
                            FormatTiming(parsed));
                        completed(parsed);
                    }
                    catch (Exception exception)
                    {
                        completed(new OllamaGenerateResult
                        {
                            Status = OllamaClientStatus.InvalidResponse,
                            Error = "Cannot parse Ollama response: " + exception.Message,
                            ElapsedMilliseconds = result.ElapsedMilliseconds
                        });
                    }
                });
        }

        internal static void ReleaseModel()
        {
            string json = JsonUtility.ToJson(new UnloadRequest
            {
                model = Model,
                keep_alive = 0
            });

            TilePaletteVisionHttpClient.Send(
                GenerateUrl,
                UnityWebRequest.kHttpVerbPOST,
                json,
                10,
                null,
                _ => { });
        }

        internal static bool HasRequiredModel(string responseJson)
        {
            TagsResponse response =
                JsonUtility.FromJson<TagsResponse>(responseJson);
            if (response?.models == null) return false;

            foreach (ModelInfo model in response.models)
            {
                if (model == null) continue;
                if (string.Equals(
                        model.name,
                        Model,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        model.model,
                        Model,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static string BuildGenerateRequestJson(
            string systemPrompt,
            string prompt,
            byte[] pngBytes,
            string responseSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(responseSchemaJson))
                throw new InvalidOperationException(
                    "Response schema is empty.");

            string[] orderedIds = ExtractOrderedIds(responseSchemaJson);
            bool usePositionalOutput = orderedIds.Length > 0;
            string effectivePrompt = prompt ?? string.Empty;
            string effectiveSchema = responseSchemaJson;

            if (usePositionalOutput)
            {
                effectivePrompt += BuildPositionalPromptSuffix(orderedIds);
                effectiveSchema = BuildPositionalResponseSchema(
                    orderedIds.Length);
            }

            GenerateRequest request = new GenerateRequest
            {
                model = Model,
                system = systemPrompt ?? string.Empty,
                prompt = effectivePrompt,
                images = new[] { Convert.ToBase64String(pngBytes) },
                stream = false,
                think = false,
                keep_alive = KeepAlive,
                options = new GenerateOptions
                {
                    temperature = 0f,
                    num_ctx = ContextWindow,
                    num_predict = MaximumPredictedTokens
                }
            };

            string json = JsonUtility.ToJson(request);
            return AddRawProperty(
                json,
                "format",
                effectiveSchema);
        }

        internal static string[] ExtractOrderedIds(string schema)
        {
            if (string.IsNullOrWhiteSpace(schema))
                return Array.Empty<string>();

            Match enumMatch = Regex.Match(
                schema,
                "\\\"enum\\\"\\s*:\\s*\\[(?<values>[^\\]]*)\\]",
                RegexOptions.CultureInvariant);
            if (!enumMatch.Success)
                return Array.Empty<string>();

            List<string> ids = new List<string>();
            MatchCollection valueMatches = Regex.Matches(
                enumMatch.Groups["values"].Value,
                "\\\"(?<value>[^\\\"]+)\\\"",
                RegexOptions.CultureInvariant);

            foreach (Match match in valueMatches)
            {
                string value = match.Groups["value"].Value;
                if (!string.IsNullOrWhiteSpace(value))
                    ids.Add(value);
            }

            return ids.ToArray();
        }

        internal static string BuildPositionalResponseSchema(int count)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            return
                "{\"type\":\"object\"," +
                "\"properties\":{" +
                    "\"c\":{" +
                        "\"type\":\"number\"," +
                        "\"minimum\":0," +
                        "\"maximum\":1}," +
                    "\"p\":{" +
                        "\"type\":\"array\"," +
                        "\"minItems\":" + count + "," +
                        "\"maxItems\":" + count + "," +
                        "\"items\":{" +
                            "\"type\":\"object\"," +
                            "\"properties\":{" +
                                "\"g\":{" +
                                    "\"type\":\"integer\"," +
                                    "\"minimum\":0}," +
                                "\"s\":{" +
                                    "\"type\":\"integer\"," +
                                    "\"minimum\":0}," +
                                "\"x\":{" +
                                    "\"type\":\"integer\"}," +
                                "\"y\":{" +
                                    "\"type\":\"integer\"}" +
                            "}," +
                            "\"required\":[" +
                                "\"g\",\"s\",\"x\",\"y\"]," +
                            "\"additionalProperties\":false" +
                        "}" +
                    "}" +
                "}," +
                "\"required\":[\"c\",\"p\"]," +
                "\"additionalProperties\":false}";
        }

        internal static OllamaGenerateResult ParseGenerateResponse(
            string responseJson)
        {
            GenerateResponse response =
                JsonUtility.FromJson<GenerateResponse>(responseJson);
            if (response == null)
                throw new InvalidOperationException("Response is empty.");
            if (!string.IsNullOrWhiteSpace(response.error))
                throw new InvalidOperationException(response.error);

            OllamaGenerateResult result = new OllamaGenerateResult
            {
                Status = OllamaClientStatus.Success,
                Content = response.response ?? string.Empty,
                DoneReason = response.done_reason ?? string.Empty,
                TotalDurationNanoseconds = response.total_duration,
                LoadDurationNanoseconds = response.load_duration,
                PromptEvalCount = response.prompt_eval_count,
                PromptEvalDurationNanoseconds = response.prompt_eval_duration,
                EvalCount = response.eval_count,
                EvalDurationNanoseconds = response.eval_duration
            };

            if (string.Equals(
                    response.done_reason,
                    "length",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Status = OllamaClientStatus.InvalidResponse;
                result.Error =
                    "Ollama output was truncated at the generation limit " +
                    $"({response.eval_count}/{MaximumPredictedTokens} tokens).";
                return result;
            }

            if (string.IsNullOrWhiteSpace(response.response))
                throw new InvalidOperationException(
                    "Response does not contain generated content.");

            return result;
        }

        internal static string ExpandPositionalResponse(
            string content,
            IReadOnlyList<string> orderedIds)
        {
            if (orderedIds == null || orderedIds.Count == 0)
                return content ?? string.Empty;

            PositionalEnvelope compact =
                JsonUtility.FromJson<PositionalEnvelope>(content);
            if (compact?.p == null)
                throw new InvalidOperationException(
                    "Compact response does not contain p.");
            if (compact.p.Length != orderedIds.Count)
                throw new InvalidOperationException(
                    $"Compact response count mismatch: expected " +
                    $"{orderedIds.Count}, got {compact.p.Length}.");

            ExpandedPlacement[] placements =
                new ExpandedPlacement[orderedIds.Count];

            for (int index = 0; index < orderedIds.Count; index++)
            {
                PositionalPlacement entry = compact.p[index];
                if (entry == null)
                    throw new InvalidOperationException(
                        $"Compact response placement {index + 1} is null.");

                placements[index] = new ExpandedPlacement
                {
                    id = orderedIds[index],
                    group = "g" + entry.g,
                    subgroup = "s" + entry.s,
                    x = entry.x,
                    y = entry.y
                };
            }

            return JsonUtility.ToJson(new ExpandedEnvelope
            {
                confidence = compact.c,
                placements = placements
            });
        }

        internal static string FormatTiming(OllamaGenerateResult result)
        {
            if (result == null) return "no timing data";

            string stop = string.IsNullOrWhiteSpace(result.DoneReason)
                ? string.Empty
                : $", stop={result.DoneReason}";

            return
                $"wall={result.ElapsedMilliseconds / 1000d:0.0}s, " +
                $"total={NanosecondsToSeconds(result.TotalDurationNanoseconds):0.0}s, " +
                $"load={NanosecondsToSeconds(result.LoadDurationNanoseconds):0.0}s, " +
                $"prompt={NanosecondsToSeconds(result.PromptEvalDurationNanoseconds):0.0}s " +
                $"({result.PromptEvalCount} tokens), " +
                $"generate={NanosecondsToSeconds(result.EvalDurationNanoseconds):0.0}s " +
                $"({result.EvalCount} tokens)" + stop;
        }

        private static string BuildPositionalPromptSuffix(
            IReadOnlyList<string> orderedIds)
        {
            return
                "\nOutput compact positional JSON only. p must contain exactly " +
                orderedIds.Count +
                " items in this exact ID order: " +
                string.Join(",", orderedIds) +
                ". Do not output IDs inside p. p[0] belongs to the first ID, " +
                "p[1] to the second, and so on. Each p item contains only " +
                "integer g,s,x,y. Use small non-negative integers for g and s; " +
                "reuse the same g/s pair for pieces in the same subgroup. " +
                "If an anchor says group=gN and subgroup=sM, output g=N and s=M. " +
                "Return no prose.";
        }

        private static double NanosecondsToSeconds(long nanoseconds)
        {
            return nanoseconds / 1000000000d;
        }

        private static string AddRawProperty(
            string json,
            string name,
            string rawValue)
        {
            if (string.IsNullOrEmpty(json) ||
                json[json.Length - 1] != '}')
                throw new InvalidOperationException(
                    "Cannot build Ollama request JSON.");

            return json.Substring(0, json.Length - 1) +
                   ",\"" + name + "\":" + rawValue + "}";
        }

        [Serializable]
        private sealed class GenerateRequest
        {
            public string model;
            public string system;
            public string prompt;
            public string[] images;
            public bool stream;
            public bool think;
            public string keep_alive;
            public GenerateOptions options;
        }

        [Serializable]
        private sealed class GenerateOptions
        {
            public float temperature;
            public int num_ctx;
            public int num_predict;
        }

        [Serializable]
        private sealed class GenerateResponse
        {
            public string response;
            public bool done;
            public string done_reason;
            public string error;
            public long total_duration;
            public long load_duration;
            public long prompt_eval_count;
            public long prompt_eval_duration;
            public long eval_count;
            public long eval_duration;
        }

        [Serializable]
        private sealed class PositionalEnvelope
        {
            public float c;
            public PositionalPlacement[] p;
        }

        [Serializable]
        private sealed class PositionalPlacement
        {
            public int g;
            public int s;
            public int x;
            public int y;
        }

        [Serializable]
        private sealed class ExpandedEnvelope
        {
            public float confidence;
            public ExpandedPlacement[] placements;
        }

        [Serializable]
        private sealed class ExpandedPlacement
        {
            public string id;
            public string group;
            public string subgroup;
            public int x;
            public int y;
        }

        [Serializable]
        private sealed class TagsResponse
        {
            public ModelInfo[] models;
        }

        [Serializable]
        private sealed class ModelInfo
        {
            public string name;
            public string model;
        }

        [Serializable]
        private sealed class UnloadRequest
        {
            public string model;
            public int keep_alive;
        }
    }
}
