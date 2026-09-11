using System;
using System.Linq;
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
        private const string CompactPromptSuffix =
            "\nUse the compact schema keys exactly: c=confidence, p=placements, " +
            "i=id, g=group, s=subgroup. Keep g and s as short stable labels " +
            "such as g0/g1 and s0/s1. Return no prose.";

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

            string compactSchema =
                CompactResponseSchema(responseSchemaJson);
            GenerateRequest request = new GenerateRequest
            {
                model = Model,
                system = systemPrompt ?? string.Empty,
                prompt = (prompt ?? string.Empty) + CompactPromptSuffix,
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
                compactSchema);
        }

        internal static string CompactResponseSchema(string schema)
        {
            return (schema ?? string.Empty)
                .Replace("\"confidence\"", "\"c\"")
                .Replace("\"placements\"", "\"p\"")
                .Replace("\"subgroup\"", "\"s\"")
                .Replace("\"group\"", "\"g\"")
                .Replace("\"id\"", "\"i\"");
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

            result.Content = ExpandCompactResponse(response.response);
            return result;
        }

        internal static string ExpandCompactResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content ?? string.Empty;

            CompactEnvelope compact;
            try
            {
                compact = JsonUtility.FromJson<CompactEnvelope>(content);
            }
            catch
            {
                return content;
            }

            if (compact?.p == null)
                return content;

            ExpandedEnvelope expanded = new ExpandedEnvelope
            {
                confidence = compact.c,
                placements = compact.p
                    .Where(entry => entry != null)
                    .Select(entry => new ExpandedPlacement
                    {
                        id = entry.i,
                        group = entry.g,
                        subgroup = entry.s,
                        x = entry.x,
                        y = entry.y
                    })
                    .ToArray()
            };

            return JsonUtility.ToJson(expanded);
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
        private sealed class CompactEnvelope
        {
            public float c;
            public CompactPlacement[] p;
        }

        [Serializable]
        private sealed class CompactPlacement
        {
            public string i;
            public string g;
            public string s;
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
