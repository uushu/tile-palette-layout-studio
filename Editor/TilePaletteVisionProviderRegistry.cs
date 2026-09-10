using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace TilePaletteLayoutStudio
{
    public sealed class VisionProviderRequest
    {
        public string Url { get; set; }
        public string Json { get; set; }
        public IReadOnlyDictionary<string, string> Headers { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    public interface ITilePaletteVisionProvider
    {
        string Id { get; }
        string DisplayName { get; }
        int Priority { get; }
        string ApiKeyEnvironment { get; }
        string Endpoint { get; }
        string Model { get; }

        VisionProviderRequest CreateRequest(
            string apiKey,
            string prompt,
            IReadOnlyList<byte[]> images,
            int timeoutSeconds);

        string ExtractResponseText(string responseJson);
    }

    internal static class TilePaletteVisionProviderRegistry
    {
        private static readonly IReadOnlyList<ITilePaletteVisionProvider> DiscoveredProviders = Discover();

        public static IReadOnlyList<ITilePaletteVisionProvider> Providers => DiscoveredProviders;

        public static ITilePaletteVisionProvider FindFirstConfigured(Func<string, bool> hasApiKey)
        {
            return DiscoveredProviders.FirstOrDefault(provider => hasApiKey(provider.ApiKeyEnvironment));
        }

        private static IReadOnlyList<ITilePaletteVisionProvider> Discover()
        {
            List<ITilePaletteVisionProvider> providers = TypeCache
                .GetTypesDerivedFrom<ITilePaletteVisionProvider>()
                .Where(type => !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null)
                .Select(type => (ITilePaletteVisionProvider)Activator.CreateInstance(type))
                .OrderByDescending(provider => provider.Priority)
                .ThenBy(provider => provider.DisplayName, StringComparer.Ordinal)
                .ToList();

            string duplicate = providers
                .GroupBy(provider => provider.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(duplicate))
                throw new InvalidOperationException("Duplicate vision provider ID: " + duplicate);

            return providers;
        }
    }
}
