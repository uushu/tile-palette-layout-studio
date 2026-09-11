using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine.Networking;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteVisionHttpClient
    {
        private const int MaximumAttempts = 2;

        public static void Send(
            VisionProviderRequest request,
            Action<string, string, long> completed,
            Func<bool> cancellationRequested = null)
        {
            if (request == null)
            {
                completed(string.Empty, "Vision request is empty.", 0);
                return;
            }

            SendAttempt(
                request,
                completed,
                cancellationRequested,
                1,
                0);
        }

        private static void SendAttempt(
            VisionProviderRequest request,
            Action<string, string, long> completed,
            Func<bool> cancellationRequested,
            int attempt,
            long previousElapsedMilliseconds)
        {
            if (cancellationRequested?.Invoke() == true)
            {
                completed(
                    string.Empty,
                    "Analysis cancelled.",
                    previousElapsedMilliseconds);
                return;
            }

            string proxyAddress = ApplySystemProxy(request.Url);
            UnityWebRequest webRequest = new UnityWebRequest(
                request.Url,
                UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(
                    Encoding.UTF8.GetBytes(request.Json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = request.TimeoutSeconds
            };
            webRequest.SetRequestHeader("Content-Type", "application/json");
            if (request.Headers != null)
            {
                foreach (KeyValuePair<string, string> header in request.Headers)
                    webRequest.SetRequestHeader(header.Key, header.Value);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();
            void Poll()
            {
                if (!operation.isDone)
                {
                    if (cancellationRequested?.Invoke() != true) return;

                    EditorApplication.update -= Poll;
                    stopwatch.Stop();
                    webRequest.Abort();
                    long cancelledElapsed =
                        previousElapsedMilliseconds +
                        stopwatch.ElapsedMilliseconds;
                    webRequest.Dispose();
                    completed(
                        string.Empty,
                        "Analysis cancelled.",
                        cancelledElapsed);
                    return;
                }

                EditorApplication.update -= Poll;
                stopwatch.Stop();
                long elapsed =
                    previousElapsedMilliseconds +
                    stopwatch.ElapsedMilliseconds;
                try
                {
                    if (cancellationRequested?.Invoke() == true)
                    {
                        completed(
                            string.Empty,
                            "Analysis cancelled.",
                            elapsed);
                        return;
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        completed(
                            webRequest.downloadHandler.text,
                            string.Empty,
                            elapsed);
                        return;
                    }

                    if (attempt < MaximumAttempts &&
                        IsRetryable(webRequest.responseCode))
                    {
                        int delaySeconds =
                            ResolveRetryDelaySeconds(webRequest, attempt);
                        ScheduleRetry(
                            () => SendAttempt(
                                request,
                                completed,
                                cancellationRequested,
                                attempt + 1,
                                elapsed),
                            delaySeconds,
                            cancellationRequested);
                        return;
                    }

                    string response = webRequest.downloadHandler?.text;
                    if (!string.IsNullOrWhiteSpace(response) &&
                        response.Length > 500)
                        response = response.Substring(0, 500);
                    string failure = FormatFailure(
                        request.Url,
                        webRequest.responseCode,
                        webRequest.error,
                        response,
                        proxyAddress);
                    if (attempt > 1)
                        failure += $" (attempts: {attempt})";
                    completed(string.Empty, failure, elapsed);
                }
                catch (Exception exception)
                {
                    completed(
                        string.Empty,
                        "Vision request failed: " + exception.Message,
                        elapsed);
                }
                finally
                {
                    webRequest.Dispose();
                }
            }

            EditorApplication.update += Poll;
        }

        private static bool IsRetryable(long responseCode) =>
            responseCode == 0 ||
            responseCode == 408 ||
            responseCode == 429 ||
            responseCode == 500 ||
            responseCode == 502 ||
            responseCode == 503 ||
            responseCode == 504;

        private static int ResolveRetryDelaySeconds(
            UnityWebRequest request,
            int attempt)
        {
            if (request.responseCode == 429)
            {
                string retryAfter = request.GetResponseHeader("Retry-After");
                if (int.TryParse(retryAfter, out int seconds))
                    return Math.Max(1, Math.Min(seconds, 30));
            }

            return Math.Max(1, attempt * 2);
        }

        private static void ScheduleRetry(
            Action action,
            int delaySeconds,
            Func<bool> cancellationRequested)
        {
            double readyAt =
                EditorApplication.timeSinceStartup + delaySeconds;
            void Wait()
            {
                if (cancellationRequested?.Invoke() == true)
                {
                    EditorApplication.update -= Wait;
                    action();
                    return;
                }

                if (EditorApplication.timeSinceStartup < readyAt) return;
                EditorApplication.update -= Wait;
                action();
            }
            EditorApplication.update += Wait;
        }

        private static string ApplySystemProxy(string requestUrl)
        {
            if (!Uri.TryCreate(
                    requestUrl,
                    UriKind.Absolute,
                    out Uri requestUri))
                return string.Empty;

            string environmentName =
                requestUri.Scheme == Uri.UriSchemeHttps
                    ? "HTTPS_PROXY"
                    : "HTTP_PROXY";
            string existingProxy =
                Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(existingProxy))
                return SanitizeProxyAddress(existingProxy);

            try
            {
                IWebProxy systemProxy =
                    WebRequest.GetSystemWebProxy();
                Uri proxyUri = systemProxy?.GetProxy(requestUri);
                if (proxyUri == null ||
                    systemProxy.IsBypassed(requestUri) ||
                    proxyUri == requestUri)
                    return string.Empty;

                string proxyAddress =
                    proxyUri.AbsoluteUri.TrimEnd('/');
                Environment.SetEnvironmentVariable(
                    "HTTP_PROXY",
                    proxyAddress,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(
                    "HTTPS_PROXY",
                    proxyAddress,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(
                    "http_proxy",
                    proxyAddress,
                    EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(
                    "https_proxy",
                    proxyAddress,
                    EnvironmentVariableTarget.Process);
                return SanitizeProxyAddress(proxyAddress);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatFailure(
            string requestUrl,
            long responseCode,
            string requestError,
            string response,
            string proxyAddress)
        {
            if (responseCode != 0)
                return $"HTTP {responseCode} {requestError}: {response}";

            string host =
                Uri.TryCreate(
                    requestUrl,
                    UriKind.Absolute,
                    out Uri requestUri)
                    ? requestUri.Host
                    : requestUrl;
            string proxyMessage =
                string.IsNullOrWhiteSpace(proxyAddress)
                    ? "No system proxy was detected."
                    : $"System proxy {proxyAddress} was detected; " +
                      "verify that its current route can access this host.";
            return $"Cannot connect to {host} ({requestError}). " +
                   proxyMessage;
        }

        private static string SanitizeProxyAddress(string proxyAddress)
        {
            if (!Uri.TryCreate(
                    proxyAddress,
                    UriKind.Absolute,
                    out Uri proxyUri))
                return "configured";
            return proxyUri.IsDefaultPort
                ? $"{proxyUri.Scheme}://{proxyUri.Host}"
                : $"{proxyUri.Scheme}://{proxyUri.Host}:{proxyUri.Port}";
        }
    }
}
