using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    internal static class TilePaletteVisionSettings
    {
        private const string TimeoutPreference = "TilePaletteLayoutStudio.VisionTimeout";
        public const int DefaultTimeoutSeconds = 120;

        public static int TimeoutSeconds
        {
            get => EditorPrefs.GetInt(TimeoutPreference, DefaultTimeoutSeconds);
            set => EditorPrefs.SetInt(TimeoutPreference, Mathf.Clamp(value, 10, 600));
        }

        public static string ProviderName
        {
            get
            {
                ITilePaletteVisionProvider provider =
                    TilePaletteVisionProviderRegistry.FindFirstConfigured(HasApiKey);
                return provider?.DisplayName ?? "Vision AI";
            }
        }

        public static bool TryResolve(
            out ITilePaletteVisionProvider provider,
            out string apiKey,
            out string error)
        {
            provider = TilePaletteVisionProviderRegistry.FindFirstConfigured(HasApiKey);
            apiKey = string.Empty;
            if (provider == null)
            {
                string expected = string.Join(", ", TilePaletteVisionProviderRegistry.Providers
                    .Select(value => value.ApiKeyEnvironment)
                    .Distinct(StringComparer.Ordinal));
                error = TilePaletteVisionProviderRegistry.Providers.Count == 0
                    ? "No vision provider was discovered."
                    : "No local API Key was found. Expected: " + expected;
                return false;
            }

            if (string.IsNullOrWhiteSpace(provider.Id))
            {
                error = provider.DisplayName + " has no provider ID.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(provider.Endpoint) ||
                !Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                error = provider.DisplayName + " has an invalid endpoint.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(provider.Model))
            {
                error = provider.DisplayName + " has no model ID.";
                return false;
            }
            if (!TryGetApiKey(provider.ApiKeyEnvironment, out apiKey))
            {
                error = "No local API Key was found. Expected: " + provider.ApiKeyEnvironment;
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool TryGetApiKey(string environmentVariable, out string value)
        {
            value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value)) return true;

            string path = LocalEnvironmentFilePath();
            if (!File.Exists(path)) return false;
            foreach (string line in File.ReadAllLines(path))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0 ||
                    !string.Equals(
                        line.Substring(0, equals).Trim(),
                        environmentVariable,
                        StringComparison.Ordinal))
                    continue;
                value = line.Substring(equals + 1).Trim().Trim('"');
                return !string.IsNullOrWhiteSpace(value);
            }
            return false;
        }

        public static bool TryGetProviderMissingApiKey(
            out ITilePaletteVisionProvider provider)
        {
            provider = TilePaletteVisionProviderRegistry.Providers.FirstOrDefault();
            return provider != null &&
                   !TryGetApiKey(provider.ApiKeyEnvironment, out _);
        }

        public static void SaveApiKey(string environmentVariable, string value)
        {
            string key = value?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("API Key is empty.");
            if (key.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new InvalidOperationException("API Key contains an invalid line break.");

            string path = LocalEnvironmentFilePath();
            string folder = Path.GetDirectoryName(path) ??
                            throw new InvalidOperationException("Cannot resolve the local settings folder.");
            Directory.CreateDirectory(folder);
            List<string> lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : new List<string>();
            string prefix = environmentVariable + "=";
            int existing = lines.FindIndex(line =>
                line.StartsWith(prefix, StringComparison.Ordinal));
            if (existing >= 0) lines[existing] = prefix + key;
            else lines.Add(prefix + key);
            File.WriteAllLines(path, lines);
            Environment.SetEnvironmentVariable(
                environmentVariable,
                key,
                EnvironmentVariableTarget.Process);
        }

        private static bool HasApiKey(string environmentVariable) =>
            TryGetApiKey(environmentVariable, out _);

        private static string LocalEnvironmentFilePath()
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ??
                          throw new InvalidOperationException("Cannot resolve the Unity project root.");
            return Path.Combine(
                root,
                StudioConstants.LocalEnvironmentPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
