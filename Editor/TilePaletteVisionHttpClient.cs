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
        private const int MaximumAttempts = 3;

        public static void Send(
            VisionProviderRequest request,
            Action<string, string, long> completed)
        {
            if (request == null)
            {
                completed(string.Empty, "Vision request is empty.", 0);
                return;
            }

            SendAttempt(request, completed, 1, 0);
        }

        private static void SendAttempt(
            VisionProviderRequest request,
            Action<string, string, long> completed,
            int attempt,
            long previousElapsedMilliseconds)
        {
            string proxyAddress = ApplySystemProxy(request.Url);
            UnityWebRequest webRequest = new UnityWebRequest(request.Url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Json)),
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
                if (!operation.isDone) return;
                EditorApplication.update -= Poll;
                stopwatch.Stop();
                long elapsed = previousElapsedMilliseconds + stopwatch.ElapsedMilliseconds;
                try
                {
                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        completed(webRequest.downloadHandler.text, string.Empty, elapsed);
                        return;
                    }

                    if (attempt < MaximumAttempts && IsRetryable(webRequest.responseCode))
                    {
                        int delaySeconds = attempt * 2;
                        ScheduleRetry(
                            () => SendAttempt(request, completed, attempt + 1, elapsed),
                            delaySeconds);
                        return;
                    }

                    string response = webRequest.downloadHandler?.text;
                    if (!string.IsNullOrWhiteSpace(response) && response.Length > 500)
                        response = response.Substring(0, 500);
                    string failure = FormatFailure(
                        request.Url,
                        webRequest.responseCode,
                        webRequest.error,
                        response,
                        proxyAddress);
                    if (attempt > 1) failure += $" (attempts: {attempt})";
                    completed(string.Empty, failure, elapsed);
                }
                catch (Exception exception)
                {
                    completed(string.Empty, "Vision request failed: " + exception.Message, elapsed);
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

        private static void ScheduleRetry(Action action, int delaySeconds)
        {
            double readyAt = EditorApplication.timeSinceStartup + delaySeconds;
            void Wait()
            {
                if (EditorApplication.timeSinceStartup < readyAt) return;
                EditorApplication.update -= Wait;
                action();
            }
            EditorApplication.update += Wait;
        }

        private static string ApplySystemProxy(string requestUrl)
        {
            if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri requestUri))
                return string.Empty;

            string environmentName =
                requestUri.Scheme == Uri.UriSchemeHttps ? "HTTPS_PROXY" : "HTTP_PROXY";
            string existingProxy = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(existingProxy))
                return SanitizeProxyAddress(existingProxy);

            try
            {
                IWebProxy systemProxy = WebRequest.GetSystemWebProxy();
                Uri proxyUri = systemProxy?.GetProxy(requestUri);
                if (proxyUri == null ||
                    systemProxy.IsBypassed(requestUri) ||
                    proxyUri == requestUri)
                    return string.Empty;

                string proxyAddress = proxyUri.AbsoluteUri.TrimEnd('/');
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

            string host = Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri requestUri)
                ? requestUri.Host
                : requestUrl;
            string proxyMessage = string.IsNullOrWhiteSpace(proxyAddress)
                ? "No system proxy was detected."
                : $"System proxy {proxyAddress} was detected; verify that its current route can access this host.";
            return $"Cannot connect to {host} ({requestError}). {proxyMessage}";
        }

        private static string SanitizeProxyAddress(string proxyAddress)
        {
            if (!Uri.TryCreate(proxyAddress, UriKind.Absolute, out Uri proxyUri))
                return "configured";
            return proxyUri.IsDefaultPort
                ? $"{proxyUri.Scheme}://{proxyUri.Host}"
                : $"{proxyUri.Scheme}://{proxyUri.Host}:{proxyUri.Port}";
        }
    }
}
