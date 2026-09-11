using System;
using System.Collections.Generic;
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
        public long ElapsedMilliseconds;
        public long TotalDurationNanoseconds;
        public long LoadDurationNanoseconds;
    }

    internal static class OllamaVisionClient
    {
        internal const string BaseUrl = "http://127.0.0.1:11434";
        internal const string Model = "qwen2.5vl:7b";
        internal const int ReadyTimeoutSeconds = 5;
        internal const int GenerateTimeoutSeconds = 120;
        private const string KeepAlive = "2m";

        private static string TagsUrl => BaseUrl + "/api/tags";
        private static string GenerateUrl => BaseUrl + "/api/generate";

        internal static void CheckReady(Func<bool> cancellationRequested, Action<OllamaReadyResult> completed)
        {
            TilePaletteVisionHttpClient.Send(TagsUrl, UnityWebRequest.kHttpVerbGET, string.Empty, ReadyTimeoutSeconds, cancellationRequested, result =>
            {
                if (result.Status == HttpRequestStatus.Cancelled)
                {
                    completed(new OllamaReadyResult { Status = OllamaClientStatus.Cancelled, Error = "Analysis cancelled.", ElapsedMilliseconds = result.ElapsedMilliseconds });
                    return;
                }
                if (result.Status != HttpRequestStatus.Success)
                {
                    completed(new OllamaReadyResult
                    {
                        Status = OllamaClientStatus.RuntimeUnavailable,
                        Error = string.IsNullOrWhiteSpace(result.Error) ? "Cannot connect to the local Ollama service." : result.Error,
                        ElapsedMilliseconds = result.ElapsedMilliseconds
                    });
                    return;
                }
                try
                {
                    if (!HasRequiredModel(result.Body))
                    {
                        completed(new OllamaReadyResult { Status = OllamaClientStatus.ModelMissing, Error = "Required Ollama model is not installed: " + Model, ElapsedMilliseconds = result.ElapsedMilliseconds });
                        return;
                    }
                    completed(new OllamaReadyResult { Status = OllamaClientStatus.Success, ElapsedMilliseconds = result.ElapsedMilliseconds });
                }
                catch (Exception exception)
                {
                    completed(new OllamaReadyResult { Status = OllamaClientStatus.InvalidResponse, Error = "Cannot parse Ollama model list: " + exception.Message, ElapsedMilliseconds = result.ElapsedMilliseconds });
                }
            });
        }

        internal static void Generate(string systemPrompt, string prompt, byte[] pngBytes, string responseSchemaJson, Func<bool> cancellationRequested, Action<OllamaGenerateResult> completed)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                completed(new OllamaGenerateResult { Status = OllamaClientStatus.RequestFailed, Error = "Contact sheet image is empty." });
                return;
            }
            string json;
            try
            {
                json = BuildGenerateRequestJson(systemPrompt, prompt, pngBytes, responseSchemaJson);
            }
            catch (Exception exception)
            {
                completed(new OllamaGenerateResult { Status = OllamaClientStatus.RequestFailed, Error = "Cannot build Ollama request: " + exception.Message });
                return;
            }
            TilePaletteVisionHttpClient.Send(GenerateUrl, UnityWebRequest.kHttpVerbPOST, json, GenerateTimeoutSeconds, cancellationRequested, result =>
            {
                if (result.Status == HttpRequestStatus.Cancelled)
                {
                    completed(new OllamaGenerateResult { Status = OllamaClientStatus.Cancelled, Error = "Analysis cancelled.", ElapsedMilliseconds = result.ElapsedMilliseconds });
                    return;
                }
                if (result.Status != HttpRequestStatus.Success)
                {
                    completed(new OllamaGenerateResult
                    {
                        Status = OllamaClientStatus.RequestFailed,
                        Error = string.IsNullOrWhiteSpace(result.Error) ? "Ollama request failed." : result.Error,
                        ElapsedMilliseconds = result.ElapsedMilliseconds
                    });
                    return;
                }
                try
                {
                    OllamaGenerateResult parsed = ParseGenerateResponse(result.Body);
                    parsed.ElapsedMilliseconds = result.ElapsedMilliseconds;
                    completed(parsed);
                }
                catch (Exception exception)
                {
                    completed(new OllamaGenerateResult { Status = OllamaClientStatus.InvalidResponse, Error = "Cannot parse Ollama response: " + exception.Message, ElapsedMilliseconds = result.ElapsedMilliseconds });
                }
            });
        }

        internal static void ReleaseModel()
        {
            string json = JsonUtility.ToJson(new UnloadRequest { model = Model, keep_alive = 0 });
            TilePaletteVisionHttpClient.Send(GenerateUrl, UnityWebRequest.kHttpVerbPOST, json, 10, null, _ => { });
        }

        internal static bool HasRequiredModel(string responseJson)
        {
            TagsResponse response = JsonUtility.FromJson<TagsResponse>(responseJson);
            if (response?.models == null) return false;
            foreach (ModelInfo model in response.models)
            {
                if (model == null) continue;
                if (string.Equals(model.name, Model, StringComparison.OrdinalIgnoreCase) || string.Equals(model.model, Model, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static string BuildGenerateRequestJson(string systemPrompt, string prompt, byte[] pngBytes, string responseSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(responseSchemaJson))
                throw new InvalidOperationException("Response schema is empty.");
            GenerateRequest request = new GenerateRequest
            {
                model = Model,
                system = systemPrompt ?? string.Empty,
                prompt = prompt ?? string.Empty,
                images = new[] { Convert.ToBase64String(pngBytes) },
                stream = false,
                think = false,
                keep_alive = KeepAlive,
                options = new GenerateOptions { temperature = 0f }
            };
            string json = JsonUtility.ToJson(request);
            return AddRawProperty(json, "format", responseSchemaJson);
        }

        internal static OllamaGenerateResult ParseGenerateResponse(string responseJson)
        {
            GenerateResponse response = JsonUtility.FromJson<GenerateResponse>(responseJson);
            if (response == null) throw new InvalidOperationException("Response is empty.");
            if (!string.IsNullOrWhiteSpace(response.error)) throw new InvalidOperationException(response.error);
            if (string.IsNullOrWhiteSpace(response.response)) throw new InvalidOperationException("Response does not contain generated content.");
            return new OllamaGenerateResult
            {
                Status = OllamaClientStatus.Success,
                Content = response.response,
                TotalDurationNanoseconds = response.total_duration,
                LoadDurationNanoseconds = response.load_duration
            };
        }

        private static string AddRawProperty(string json, string name, string rawValue)
        {
            if (string.IsNullOrEmpty(json) || json[json.Length - 1] != '}') throw new InvalidOperationException("Cannot build Ollama request JSON.");
            return json.Substring(0, json.Length - 1) + ",\"" + name + "\":" + rawValue + "}";
        }

        [Serializable] private sealed class GenerateRequest
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
        [Serializable] private sealed class GenerateOptions { public float temperature; }
        [Serializable] private sealed class GenerateResponse
        {
            public string response;
            public bool done;
            public string error;
            public long total_duration;
            public long load_duration;
        }
        [Serializable] private sealed class TagsResponse { public ModelInfo[] models; }
        [Serializable] private sealed class ModelInfo { public string name; public string model; }
        [Serializable] private sealed class UnloadRequest { public string model; public int keep_alive; }
    }
}
