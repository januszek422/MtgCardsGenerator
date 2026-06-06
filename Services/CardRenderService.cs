using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using AiMagicCardsGenerator.Models.Entities;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.Extensions.Logging;
using static AiMagicCardsGenerator.Services.CardRenderConfig;
namespace AiMagicCardsGenerator.Services;

public class CardRenderService : ICardRenderService {
    private readonly IWebHostEnvironment    _environment;
    private readonly IImageGeneratorService _imageGenerator;
    private readonly ILogger<CardRenderService> _logger;
    private readonly FontFamily             _nameFont;
    private readonly FontFamily             _typeFont;
    private readonly FontFamily             _textFont;
    private readonly FontFamily             _flavorFont;

    public CardRenderService(IWebHostEnvironment environment, IImageGeneratorService imageGenerator, ILogger<CardRenderService> logger) {
        _environment    = environment;
        _imageGenerator = imageGenerator;
        _logger         = logger;

        var fontsPath = Path.Combine(_environment.WebRootPath, "assets", "fonts");

        var collection = new FontCollection();
        _nameFont   = collection.Add(Path.Combine(fontsPath, "Beleren Small Caps.ttf"));
        _typeFont   = collection.Add(Path.Combine(fontsPath, "Matrix-Bold.ttf"));
        _textFont   = collection.Add(Path.Combine(fontsPath, "PlantinMTProRg.TTF"));
        _flavorFont = collection.Add(Path.Combine(fontsPath, "PlantinMTProRgIt.TTF"));
    }

    public async Task<byte[]> RenderCardAsync(Card card) {
        var color     = GetCardColor(card);
        var framePath = Path.Combine(_environment.WebRootPath, "assets", "frames", $"{color}.png");

        if (!File.Exists(framePath))
            framePath = Path.Combine(_environment.WebRootPath, "assets", "frames", "C.png");

        using var frame = await Image.LoadAsync<Rgba32>(framePath);

        using var image = new Image<Rgba32>(frame.Width, frame.Height);

        try {
            _logger.LogInformation("Attempting to generate artwork for card: {CardName}", card.Name);
            var artBytes = await _imageGenerator.GenerateCardArtAsync(card.Name, card.TypeLine, card.OracleText);
            _logger.LogInformation("Successfully generated artwork, received {ByteCount} bytes", artBytes.Length);

            using var artImage = Image.Load<Rgba32>(artBytes);
            _logger.LogInformation("Successfully loaded artwork image");

            artImage.Mutate(ctx => ctx.Resize(ART_WIDTH, ART_HEIGHT));
            image.Mutate(ctx => ctx.DrawImage(artImage, new Point(ART_X, ART_Y), 1f));
            _logger.LogInformation("Successfully rendered artwork on card");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to generate or render artwork for card: {CardName}. Type: {TypeLine}",
                card.Name, card.TypeLine);
        }

        image.Mutate(ctx => ctx.DrawImage(frame, new Point(0, 0), 1f));

        var nameFont   = _nameFont.CreateFont(NAME_FONT_SIZE, FontStyle.Regular);
        var typeFont   = _typeFont.CreateFont(TYPE_FONT_SIZE, FontStyle.Bold);
        var textFont   = _textFont.CreateFont(ORACLE_FONT_SIZE, FontStyle.Regular);
        var flavorFont = _flavorFont.CreateFont(FLAVOR_FONT_SIZE, FontStyle.Italic);
        var ptFont     = _nameFont.CreateFont(PT_FONT_SIZE, FontStyle.Regular);

        // Card name
        image.Mutate(ctx => ctx.DrawText(card.Name, nameFont, Color.White, new PointF(NAME_X, NAME_Y)));

        // Type line
        image.Mutate(ctx => ctx.DrawText(card.TypeLine, typeFont, Color.White, new PointF(TYPE_X, TYPE_Y)));

        // Oracle text
        if (!string.IsNullOrEmpty(card.OracleText)) {
            await DrawOracleTextAsync(image, card.OracleText, textFont);
        }

        // Flavor text
        if (!string.IsNullOrEmpty(card.FlavorText)) {
            var flavorOptions = new RichTextOptions(flavorFont) {
                Origin         = new PointF(FLAVOR_X, FLAVOR_Y),
                WrappingLength = ORACLE_WIDTH
            };
            image.Mutate(ctx => ctx.DrawText(flavorOptions, card.FlavorText, Color.White));
        }

        // Power/Toughness
        if (!string.IsNullOrEmpty(card.Power) && !string.IsNullOrEmpty(card.Toughness)) {
            var pt = $"{card.Power}/{card.Toughness}";
            image.Mutate(ctx => ctx.DrawText(pt, ptFont, Color.White, new PointF(PT_X, PT_Y)));
        }

        // Mana cost
        await DrawManaSymbolsAsync(image, card.ManaCost);

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        return ms.ToArray();
    }



    private async Task DrawOracleTextAsync(Image image, string oracleText, Font font) {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Deathtouch", "Defender", "Double strike", "First strike", "Flash",
            "Flying", "Haste", "Hexproof", "Indestructible", "Lifelink",
            "Menace", "Reach", "Shroud", "Trample", "Vigilance",
            "Ward", "Fear", "Intimidate", "Prowess", "Wither",
            "Infect", "Undying", "Persist", "Convoke", "Delve",
            "Cascade", "Afflict", "Afterlife", "Annihilator", "Battle cry",
            "Changeling", "Devoid", "Exalted", "Flanking", "Horsemanship",
            "Protection", "Shadow", "Skulk", "Myriad", "Melee",
            "Crew", "Fabricate", "Embalm", "Eternalize", "Riot",
            "Spectacle", "Escape", "Mutate", "Foretell", "Daybound",
            "Nightbound", "Cleave", "Training", "Toxic", "Backup",
            "Bargain", "Enchant creature", "Enchant permanent", "Enchant land",
            "Enchant artifact", "Enchant player", "Equip"
        };

        var processedText = oracleText;

        // Split by periods and newlines to process sentences
        var sentences = Regex.Split(processedText, @"(?<=\.)\s+");
        var result    = new List<string>();

        foreach (var sentence in sentences) {
            var trimmed = sentence.Trim();

            // Check if sentence is just keywords (possibly comma-separated)
            var isKeywordLine = false;
            var parts         = trimmed.TrimEnd('.').Split(',').Select(p => p.Trim()).ToList();

            if (parts.All(p => keywords.Contains(p))) {
                // All parts are keywords - each on its own line
                foreach (var part in parts) {
                    result.Add(part);
                }

                isKeywordLine = true;
            }

            if (!isKeywordLine) {
                // Check if starts with keyword followed by period
                var startsWithKeyword = keywords.FirstOrDefault(k =>
                    trimmed.StartsWith(k + ".", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Length == k.Length + 1);

                if (startsWithKeyword != null &&
                    trimmed.Equals(startsWithKeyword + ".", StringComparison.OrdinalIgnoreCase)) {
                    result.Add(startsWithKeyword);
                }
                else {
                    result.Add(trimmed);
                }
            }
        }

        // Add newline before activated abilities
        var finalText = string.Join("\n", result);
        finalText = Regex.Replace(finalText, @"(?<!\n)((?:\{[^}]+\},?\s*)+:)", "\n$1");

        var lines = finalText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var y     = (float)ORACLE_Y;

        foreach (var line in lines) {
            var x     = (float)ORACLE_X;
            var parts = Regex.Split(line.Trim(), @"(\{[^}]+\})");

            foreach (var part in parts) {
                if (string.IsNullOrEmpty(part)) continue;

                var symbolMatch = Regex.Match(part, @"\{([^}]+)\}");

                if (symbolMatch.Success) {
                    var symbol = symbolMatch.Groups[1].Value;
                    await DrawSymbolAsync(image, symbol, (int)x, (int)y + 5, ORACLE_SYMBOL_SIZE);
                    x += ORACLE_SYMBOL_SIZE + 4;
                }
                else {
                    var words = part.Split(' ');
                    foreach (var word in words) {
                        if (string.IsNullOrEmpty(word)) continue;

                        var wordWidth = MeasureText(word, font);

                        if (x + wordWidth > ORACLE_X + ORACLE_WIDTH) {
                            x =  ORACLE_X;
                            y += ORACLE_LINE_HEIGHT;
                        }

                        image.Mutate(ctx => ctx.DrawText(word, font, Color.White, new PointF(x, y)));
                        x += wordWidth + ORACLE_SPACE_WIDTH;
                    }
                }
            }

            y += ORACLE_LINE_HEIGHT;
        }
    }

    private float MeasureText(string text, Font font) {
        var options = new TextOptions(font);
        var size    = TextMeasurer.MeasureSize(text, options);
        return size.Width;
    }

    private async Task DrawSymbolAsync(Image image, string symbol, int x, int y, int size) {
        var symbolPath = Path.Combine(_environment.WebRootPath, "assets", "symbols", $"{symbol}.png");

        if (!File.Exists(symbolPath)) {
            _logger.LogWarning("Mana/text symbol asset not found: {Symbol} ({Path})", symbol, symbolPath);
            return;
        }

        try {
            using var symbolImage = await Image.LoadAsync<Rgba32>(symbolPath);
            symbolImage.Mutate(ctx => ctx.Resize(size, size));
            image.Mutate(ctx => ctx.DrawImage(symbolImage, new Point(x, y), 1f));
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to render symbol {Symbol}", symbol);
        }
    }

    private async Task DrawManaSymbolsAsync(Image image, string? manaCost) {
        if (string.IsNullOrEmpty(manaCost)) return;

        var symbols = Regex.Matches(manaCost, @"\{([^}]+)\}")
            .Select(m => m.Groups[1].Value)
            .Reverse()
            .ToList();

        var x = MANA_START_X;

        foreach (var symbol in symbols) {
            await DrawSymbolAsync(image, symbol, x - MANA_SYMBOL_SIZE, MANA_Y, MANA_SYMBOL_SIZE);
            x -= MANA_SYMBOL_SIZE + MANA_SPACING;
        }
    }

    public string GetCardColor(Card card) {
        var colors = new List<string>();

        if (!string.IsNullOrEmpty(card.Colors)) {
            var colorsStr = card.Colors.Trim();
            if (colorsStr.StartsWith("[")) {
                var matches = Regex.Matches(colorsStr, @"""(\w)""");
                foreach (Match m in matches) {
                    colors.Add(m.Groups[1].Value);
                }
            }
            else {
                colors.Add(colorsStr);
            }
        }

        if (!string.IsNullOrEmpty(card.ManaCost)) {
            if (card.ManaCost.Contains("{W}") && !colors.Contains("W")) colors.Add("W");
            if (card.ManaCost.Contains("{U}") && !colors.Contains("U")) colors.Add("U");
            if (card.ManaCost.Contains("{B}") && !colors.Contains("B")) colors.Add("B");
            if (card.ManaCost.Contains("{R}") && !colors.Contains("R")) colors.Add("R");
            if (card.ManaCost.Contains("{G}") && !colors.Contains("G")) colors.Add("G");
        }

        if (card.TypeLine.Contains("Land"))
            return "C";

        if (card.TypeLine.Contains("Artifact") && colors.Count == 0)
            return "C";

        if (colors.Count > 1)
            return "M";

        if (colors.Count == 1)
            return colors[0];

        return "C";
    }
}