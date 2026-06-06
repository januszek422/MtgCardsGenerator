using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AiMagicCardsGenerator.Services;

public class ImageGeneratorService : IImageGeneratorService {
    private readonly HttpClient                     _httpClient;
    private readonly ILogger<ImageGeneratorService> _logger;
    private readonly string                         _apiToken;

    private const string ModelUrl = "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell";

    public ImageGeneratorService(HttpClient     httpClient, ILogger<ImageGeneratorService> logger,
                                 IConfiguration configuration) {
        _httpClient = httpClient;
        _logger     = logger;
        _apiToken = configuration["HuggingFace:ApiKey"]
            ?? throw new InvalidOperationException("HuggingFace:ApiKey is not configured");
    }

    public async Task<byte[]> GenerateCardArtAsync(string cardName, string typeLine, string? oracleText) {
        var prompt = BuildPrompt(cardName, typeLine, oracleText);
        _logger.LogInformation("Generated prompt for {CardName}: {Prompt}", cardName, prompt);

        const int maxRetries  = 3;
        var       retryDelays = new[] { 2000, 5000, 10000 };

        for (int attempt = 0; attempt < maxRetries; attempt++) {
            try {
                _logger.LogInformation("Calling HuggingFace API (attempt {Attempt}/{Max})", attempt + 1, maxRetries);

                var request = new HttpRequestMessage(HttpMethod.Post, ModelUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

                var payload = new {
                    inputs = prompt,
                    parameters = new {
                        width               = 1024,
                        height              = 768,
                        // FLUX.1-schnell is a timestep-distilled model: it targets ~4 steps
                        // and ignores guidance_scale, so we keep steps low to stay fast.
                        num_inference_steps = 4
                    }
                };

                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode) {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    _logger.LogInformation("Successfully received image from API, size: {Size} bytes",
                        imageBytes.Length);
                    return imageBytes;
                }

                var statusCode   = (int)response.StatusCode;
                var responseBody = await response.Content.ReadAsStringAsync();

                // Model loading - HuggingFace returns 503 when model is loading
                if (statusCode == 503 && responseBody.Contains("loading")) {
                    var estimatedTime = TryParseEstimatedTime(responseBody);
                    var delay         = estimatedTime > 0 ? (int)(estimatedTime * 1000) + 1000 : retryDelays[attempt];

                    _logger.LogWarning("Model is loading. Waiting {Delay}ms before retry...", delay);
                    await Task.Delay(delay);
                    continue;
                }

                // Retry on server errors (5xx) or rate limit (429)
                if ((statusCode >= 500 || statusCode == 429) && attempt < maxRetries - 1) {
                    var delay = retryDelays[attempt];
                    _logger.LogWarning(
                        "Received {StatusCode} from HuggingFace API. Retrying in {Delay}ms. Response: {Response}",
                        statusCode, delay, responseBody);
                    await Task.Delay(delay);
                    continue;
                }

                _logger.LogError(
                    "Failed to fetch image from HuggingFace API. Status: {StatusCode}, Response: {Response}",
                    statusCode, responseBody);
                throw new HttpRequestException($"HuggingFace API error: {statusCode} - {responseBody}");
            }
            catch (HttpRequestException) when (attempt < maxRetries - 1) {
                var delay = retryDelays[attempt];
                _logger.LogWarning("HTTP request failed (attempt {Attempt}/{Max}). Retrying in {Delay}ms...",
                    attempt + 1, maxRetries, delay);
                await Task.Delay(delay);
            }
            catch (HttpRequestException ex) {
                _logger.LogError(ex,
                    "Failed to fetch image from HuggingFace API for card: {CardName} after {Attempts} attempts",
                    cardName, maxRetries);
                throw;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Unexpected error while fetching image from HuggingFace API for card: {CardName}",
                    cardName);
                throw;
            }
        }

        throw new HttpRequestException($"Failed to fetch image after {maxRetries} attempts");
    }

    private double TryParseEstimatedTime(string responseBody) {
        try {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("estimated_time", out var estimatedTime)) {
                return estimatedTime.GetDouble();
            }
        }
        catch { }

        return 0;
    }

    private string BuildPrompt(string cardName, string typeLine, string? oracleText) {
        var creatureType = "";
        var typeMatch    = Regex.Match(typeLine, @"—\s*(.+)$");
        if (typeMatch.Success) {
            creatureType = typeMatch.Groups[1].Value.Trim();
        }

        var prompt = $"Fantasy art, Magic the Gathering card art style, {cardName}";

        if (!string.IsNullOrEmpty(creatureType)) {
            prompt += $", {creatureType}";
        }

        if (typeLine.Contains("Creature")) {
            prompt += ", creature portrait, dramatic pose";
        }
        else if (typeLine.Contains("Instant") || typeLine.Contains("Sorcery")) {
            prompt += ", magical spell effect, energy, mystical";
        }
        else if (typeLine.Contains("Enchantment")) {
            prompt += ", magical aura, ethereal glow";
        }
        else if (typeLine.Contains("Artifact")) {
            prompt += ", magical item, detailed object";
        }
        else if (typeLine.Contains("Land")) {
            prompt += ", landscape, environment, scenic";
        }

        prompt += ", high fantasy, detailed, epic lighting, professional illustration";

        return prompt;
    }
}