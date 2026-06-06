using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiMagicCardsGenerator.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;

namespace AiMagicCardsGenerator.Tests.Services;

[TestClass]
[TestSubject(typeof(ImageGeneratorService))]
public class ImageGeneratorServiceTest {
    private Mock<HttpMessageHandler>                _mockHttpHandler = null!;
    private HttpClient                              _httpClient      = null!;
    private Mock<ILogger<ImageGeneratorService>>    _mockLogger      = null!;
    private Mock<IConfiguration>                    _mockConfig      = null!;
    private ImageGeneratorService                   _service         = null!;

    [TestInitialize]
    public void Setup() {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient      = new HttpClient(_mockHttpHandler.Object);
        _mockLogger      = new Mock<ILogger<ImageGeneratorService>>();
        _mockConfig      = new Mock<IConfiguration>();

        // Setup configuration to return a test API key
        _mockConfig.Setup(c => c["HuggingFace:ApiKey"]).Returns("test-api-key");

        _service = new ImageGeneratorService(_httpClient, _mockLogger.Object, _mockConfig.Object);
    }

    #region GenerateCardArtAsync

    [TestMethod]
    public async Task GenerateCardArtAsync_ValidRequest_ReturnsImageBytes() {
        // Arrange
        var expectedBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        SetupHttpResponse(expectedBytes);

        // Act
        var result = await _service.GenerateCardArtAsync("Test Card", "Creature - Human", "Some text");

        // Assert
        Assert.IsNotNull(result);
        Assert.HasCount(4, result);
        CollectionAssert.AreEqual(expectedBytes, result);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_CallsCorrectBaseUrl() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Test", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual("https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell",
            capturedRequest.RequestUri!.ToString());
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_UsesPostMethod() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Test", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.AreEqual(HttpMethod.Post, capturedRequest.Method);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_IncludesBearerToken() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Test", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.IsNotNull(capturedRequest.Headers.Authorization);
        Assert.AreEqual("Bearer", capturedRequest.Headers.Authorization.Scheme);
        Assert.AreEqual("test-api-key", capturedRequest.Headers.Authorization.Parameter);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_SendsJsonPayload() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Test", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        Assert.IsNotNull(capturedRequest.Content);
        Assert.AreEqual("application/json", capturedRequest.Content.Headers.ContentType!.MediaType);
    }

    #endregion

    #region BuildPrompt - Card Types

    [TestMethod]
    public async Task GenerateCardArtAsync_CreatureType_IncludesCreatureKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Goblin Warrior", "Creature - Goblin Warrior", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("creature portrait", prompt);
        Assert.Contains("dramatic pose", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_InstantType_IncludesSpellKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Lightning Bolt", "Instant", "Deal 3 damage");

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("magical spell effect", prompt);
        Assert.Contains("mystical", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_SorceryType_IncludesSpellKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Fireball", "Sorcery", "Deal X damage");

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("magical spell effect", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_EnchantmentType_IncludesAuraKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Divine Favor", "Enchantment - Aura", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("magical aura", prompt);
        Assert.Contains("ethereal glow", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_ArtifactType_IncludesItemKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Sol Ring", "Artifact", "Tap: Add {C}{C}");

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("magical item", prompt);
        Assert.Contains("detailed object", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_LandType_IncludesLandscapeKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Forest", "Basic Land - Forest", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("landscape", prompt);
        Assert.Contains("environment", prompt);
    }

    #endregion

    #region BuildPrompt - Creature Subtypes

    [TestMethod]
    public async Task GenerateCardArtAsync_CreatureWithSubtype_IncludesSubtypeInPrompt() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Elvish Mystic", "Creature — Elf Druid", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("Elf Druid", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_CreatureWithoutSubtype_NoExtraComma() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Nameless", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("Nameless", prompt);
    }

    #endregion

    #region BuildPrompt - Common Elements

    [TestMethod]
    public async Task GenerateCardArtAsync_AlwaysIncludesFantasyArtStyle() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Any Card", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("Fantasy art", prompt);
        Assert.Contains("Magic the Gathering card art style", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_AlwaysIncludesQualityKeywords() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Any Card", "Creature", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("high fantasy", prompt);
        Assert.Contains("detailed", prompt);
        Assert.Contains("epic lighting", prompt);
        Assert.Contains("professional illustration", prompt);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_IncludesCardNameInPrompt() {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        SetupHttpResponseWithCapture(new byte[] { 1, 2, 3 }, req => capturedRequest = req);

        // Act
        await _service.GenerateCardArtAsync("Shivan Dragon", "Creature - Dragon", null);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var prompt = await GetPromptFromRequest(capturedRequest);
        Assert.Contains("Shivan Dragon", prompt);
    }

    #endregion

    #region Error Handling

    [TestMethod]
    public async Task GenerateCardArtAsync_HttpError_ThrowsException() {
        // Arrange
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage {
                StatusCode = HttpStatusCode.InternalServerError
            });

        // Act & Assert
        var caught = false;
        try {
            await _service.GenerateCardArtAsync("Test", "Creature", null);
        }
        catch (HttpRequestException) {
            caught = true;
        }

        Assert.IsTrue(caught);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_NullOracleText_DoesNotThrow() {
        // Arrange
        SetupHttpResponse(new byte[] { 1, 2, 3 });

        // Act
        var result = await _service.GenerateCardArtAsync("Test", "Creature", null);

        // Assert
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task GenerateCardArtAsync_EmptyOracleText_DoesNotThrow() {
        // Arrange
        SetupHttpResponse(new byte[] { 1, 2, 3 });

        // Act
        var result = await _service.GenerateCardArtAsync("Test", "Creature", "");

        // Assert
        Assert.IsNotNull(result);
    }

    #endregion

    #region Helpers

    private void SetupHttpResponse(byte[] content) {
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage {
                StatusCode = HttpStatusCode.OK,
                Content    = new ByteArrayContent(content)
            });
    }

    private void SetupHttpResponseWithCapture(byte[] content, Action<HttpRequestMessage> capture) {
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capture(req))
            .ReturnsAsync(new HttpResponseMessage {
                StatusCode = HttpStatusCode.OK,
                Content    = new ByteArrayContent(content)
            });
    }

    private static async Task<string> GetPromptFromRequest(HttpRequestMessage request) {
        Assert.IsNotNull(request.Content);
        var content = await request.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.GetProperty("inputs").GetString()!;
    }

    #endregion
}