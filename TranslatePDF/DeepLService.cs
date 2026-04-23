using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TranslatePDF.Services
{
    public static class DeepLService
    {
        private static HttpClient? _client;
        private static string url;
        private static readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(1, 1);

        public static bool IsApiKeySet =>
            !string.IsNullOrWhiteSpace(
                Properties.Settings.Default.DeepLApiKey);

        public static void Initialize()
        {
            if (_client != null)
                return;

            var apiKey = Properties.Settings.Default.DeepLApiKey?.Trim();
            var isFree = apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase);
            url = isFree
                    ? "https://api-free.deepl.com/v2/translate"
                    : "https://api.deepl.com/v2/translate";
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("DeepLApiKey 未設定");

            _client = new HttpClient();

            _client.DefaultRequestHeaders.Clear();

            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Authorization",
                $"DeepL-Auth-Key {apiKey}");
            _client.Timeout = TimeSpan.FromSeconds(60);
        }

        public static async Task<string> TranslateAsync(string text, string targetLang, CancellationToken token)
        {
            await _rateLimiter.WaitAsync(token);

            try
            {
                await Task.Delay(1, token);

                if (_client == null)
                    throw new InvalidOperationException("DeepLService が初期化されていません");

                if (string.IsNullOrWhiteSpace(text))
                    return "";

                var payload = new
                {
                    text = new[] { text },
                    target_lang = targetLang
                };

                var json = JsonSerializer.Serialize(payload);

                int maxRetries = 5;
                int delayMs = 1000;

                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        using var content = new StringContent(
                            json,
                            Encoding.UTF8,
                            "application/json");

                        var response = await _client.PostAsync(
                            url,
                            content,
                            token);

                        var body =
                            await response.Content.ReadAsStringAsync(token);

                        if (response.IsSuccessStatusCode)
                        {
                            using var doc = JsonDocument.Parse(body);

                            return doc.RootElement
                                .GetProperty("translations")[0]
                                .GetProperty("text")
                                .GetString() ?? "";
                        }
                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            await Task.Delay(delayMs, token);
                            delayMs *= 2;
                            continue;
                        }
                        if ((int)response.StatusCode >= 500)
                        {
                            if (attempt == maxRetries - 1)
                                return ""; // エラー表示しない

                            await Task.Delay(delayMs, token);
                            delayMs *= 2;
                            continue;
                        }
                        if ((int)response.StatusCode == 456)
                        {
                            throw new Exception("QUOTA_EXCEEDED");
                        }

                        throw new Exception(
                            $"Status: {(int)response.StatusCode}\n{body}");
                    }
                    catch (HttpRequestException)
                    {
                        if (attempt == maxRetries - 1)
                            throw;

                        await Task.Delay(delayMs, token);
                        delayMs *= 2;
                    }
                    catch (TaskCanceledException)
                    {
                        if (token.IsCancellationRequested)
                            throw;

                        if (attempt == maxRetries - 1)
                            throw;

                        await Task.Delay(delayMs, token);
                        delayMs *= 2;
                    }
                }

                throw new Exception("DeepL 翻訳に失敗しました");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        public static void UpdateApiKey(string newKey)
        {
            var trimmed = newKey.Trim();

            if (Properties.Settings.Default.DeepLApiKey != trimmed)
            {
                Properties.Settings.Default.DeepLApiKey = trimmed;
                Properties.Settings.Default.Save();
            }
        }

        public static void Reset()
        {
            _client?.Dispose();
            _client = null;
        }
    }
}