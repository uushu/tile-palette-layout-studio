using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine.Networking;

namespace TilePaletteLayoutStudio
{
    internal enum HttpRequestStatus
    {
        Success,
        Cancelled,
        Failed
    }

    internal sealed class HttpRequestResult
    {
        public HttpRequestStatus Status;
        public long StatusCode;
        public string Body = string.Empty;
        public string Error = string.Empty;
        public long ElapsedMilliseconds;
    }

    internal static class TilePaletteVisionHttpClient
    {
        internal static void Send(
            string url,
            string method,
            string json,
            int timeoutSeconds,
            Func<bool> cancellationRequested,
            Action<HttpRequestResult> completed)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                completed(new HttpRequestResult
                {
                    Status = HttpRequestStatus.Failed,
                    Error = "Request URL is empty."
                });
                return;
            }

            if (cancellationRequested?.Invoke() == true)
            {
                completed(new HttpRequestResult
                {
                    Status = HttpRequestStatus.Cancelled,
                    Error = "Analysis cancelled."
                });
                return;
            }

            UnityWebRequest request = new UnityWebRequest(
                url,
                string.IsNullOrWhiteSpace(method)
                    ? UnityWebRequest.kHttpVerbGET
                    : method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Math.Max(1, timeoutSeconds)
            };

            if (!string.IsNullOrEmpty(json))
            {
                request.uploadHandler = new UploadHandlerRaw(
                    Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            void Finish(HttpRequestResult result)
            {
                EditorApplication.update -= Poll;
                stopwatch.Stop();
                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                request.Dispose();
                completed(result);
            }

            void Poll()
            {
                if (!operation.isDone)
                {
                    if (cancellationRequested?.Invoke() != true) return;

                    request.Abort();
                    Finish(new HttpRequestResult
                    {
                        Status = HttpRequestStatus.Cancelled,
                        Error = "Analysis cancelled."
                    });
                    return;
                }

                if (cancellationRequested?.Invoke() == true)
                {
                    Finish(new HttpRequestResult
                    {
                        Status = HttpRequestStatus.Cancelled,
                        Error = "Analysis cancelled."
                    });
                    return;
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Finish(new HttpRequestResult
                    {
                        Status = HttpRequestStatus.Success,
                        StatusCode = request.responseCode,
                        Body = request.downloadHandler?.text ?? string.Empty
                    });
                    return;
                }

                string body = request.downloadHandler?.text ?? string.Empty;
                Finish(new HttpRequestResult
                {
                    Status = HttpRequestStatus.Failed,
                    StatusCode = request.responseCode,
                    Body = body,
                    Error = FormatFailure(
                        request.responseCode,
                        request.error,
                        body)
                });
            }

            EditorApplication.update += Poll;
        }

        private static string FormatFailure(
            long statusCode,
            string requestError,
            string body)
        {
            string trimmed = body?.Trim() ?? string.Empty;
            if (trimmed.Length > 1000)
                trimmed = trimmed.Substring(0, 1000);

            if (statusCode > 0)
            {
                return string.IsNullOrWhiteSpace(trimmed)
                    ? $"HTTP {statusCode}: {requestError}"
                    : $"HTTP {statusCode}: {trimmed}";
            }

            return string.IsNullOrWhiteSpace(requestError)
                ? "Cannot connect to the local Ollama service."
                : "Cannot connect to the local Ollama service: " + requestError;
        }
    }
}
