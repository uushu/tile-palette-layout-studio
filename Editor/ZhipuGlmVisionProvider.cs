using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TilePaletteLayoutStudio
{
    public sealed class ZhipuGlmVisionProvider : ITilePaletteVisionProvider
    {
        public string Id => "zhipu-glm-4.6v-flash";
        public string DisplayName => "Zhipu GLM-4.6V-Flash";
        public int Priority => 1000;
        public string ApiKeyEnvironment => "ZHIPUAI_API_KEY";
        public string Endpoint => "https://open.bigmodel.cn/api/paas/v4/chat/completions";
        public string Model => "glm-4.6v-flash";

        public VisionProviderRequest CreateRequest(
            string apiKey,
            string prompt,
            IReadOnlyList<byte[]> images,
            int timeoutSeconds)
        {
            List<string> content = new List<string>
            {
                JsonUtility.ToJson(new TextContent { type = "text", text = prompt })
            };
            content.AddRange(images.Select(image => JsonUtility.ToJson(new ImageContent
            {
                type = "image_url",
                image_url = new ImageUrl
                {
                    url = "data:image/png;base64," + Convert.ToBase64String(image)
                }
            })));

            string header = JsonUtility.ToJson(new RequestHeader { model = Model });
            string message = "{\"role\":\"user\",\"content\":[" + string.Join(",", content) + "]}";
            return new VisionProviderRequest
            {
                Url = Endpoint,
                Json = AddRawProperty(header, "messages", "[" + message + "]"),
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer " + apiKey
                },
                TimeoutSeconds = timeoutSeconds
            };
        }

        public string ExtractResponseText(string responseJson)
        {
            Response response = JsonUtility.FromJson<Response>(responseJson);
            string content = response?.choices != null && response.choices.Length > 0
                ? response.choices[0].message?.content
                : null;
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Response does not contain choices[0].message.content.");
            return content;
        }

        private static string AddRawProperty(string json, string name, string rawValue)
        {
            if (string.IsNullOrEmpty(json) || json[json.Length - 1] != '}')
                throw new InvalidOperationException("Cannot build GLM request JSON.");
            return json.Substring(0, json.Length - 1) + ",\"" + name + "\":" + rawValue + "}";
        }

        [Serializable] private sealed class RequestHeader { public string model; }
        [Serializable] private sealed class TextContent { public string type; public string text; }
        [Serializable] private sealed class ImageContent { public string type; public ImageUrl image_url; }
        [Serializable] private sealed class ImageUrl { public string url; }
        [Serializable] private sealed class Response { public Choice[] choices; }
        [Serializable] private sealed class Choice { public ResponseMessage message; }
        [Serializable] private sealed class ResponseMessage { public string content; }
    }
}
