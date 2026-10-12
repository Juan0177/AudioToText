using AudioToText.Web.Services;

namespace AudioToText.Tests;

public class ExtractiveTldrServiceTests
{
    private readonly ExtractiveTldrService _sut = new();

    [Fact]
    public void Summarize_Empty_ReturnsEmpty()
    {
        var result = _sut.Summarize("   ");
        Assert.Equal(string.Empty, result.Summary);
        Assert.Empty(result.Bullets);
        Assert.Equal("extractive", result.Method);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Summarize_ShortText_DoesNotEchoFullTranscription()
    {
        var text = "Ciao, ti chiamo più tardi. Ok grazie.";
        var result = _sut.Summarize(text);

        Assert.Equal(string.Empty, result.Summary);
        Assert.Empty(result.Bullets);
        Assert.Equal(ExtractiveTldrService.TooShortMessage, result.Message);
        Assert.NotEqual(Normalize(text), Normalize(result.Summary));
    }

    [Fact]
    public void Summarize_MultiSentence_IsShorterAndNotEqualToFullText()
    {
        var text =
            "Ciao Marco, ti scrivo per il viaggio di sabato. Partiamo alle nove dalla stazione centrale. " +
            "Porta il biglietto stampato e una bottiglia d'acqua. Il treno arriva a Firenze verso mezzogiorno. " +
            "Poi andiamo a piedi all'hotel vicino al Duomo. La cena è prenotata alle otto in un ristorante tipico. " +
            "Se piove portiamo gli ombrelli. Fammi sapere se hai bisogno di altro prima di sabato.";

        var result = _sut.Summarize(text);

        Assert.Null(result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.NotEqual(Normalize(text), Normalize(result.Summary));
        Assert.True(result.Summary.Length < text.Length * 0.5, $"Summary too long: {result.Summary.Length} vs {text.Length}");
        Assert.True(result.Bullets.Count is >= 2 and <= 4);
        Assert.All(result.Bullets, b => Assert.True(b.Length < text.Length));
    }

    [Fact]
    public void Summarize_RunOnItalianSttWithoutCapitals_StillSummarizes()
    {
        var text =
            "allora ti racconto, ieri ho giocato alla playstation e ho speso un sacco di gemme. " +
            "volevo comprare il nuovo personaggio ma non mi bastavano i crediti. " +
            "poi mia mamma mi ha detto di spegnere e fare i compiti. " +
            "alla fine ho usato il tablet per studiare matematica. " +
            "fammi sapere se sabato usciamo al parco oppure restiamo a casa.";

        var result = _sut.Summarize(text);

        Assert.Null(result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.NotEqual(Normalize(text), Normalize(result.Summary));
        Assert.True(result.Summary.Length <= (int)(text.Length * 0.45) + 20);
        Assert.True(result.Bullets.Count <= 4);
        Assert.DoesNotContain(
            "fammi sapere se sabato usciamo al parco oppure restiamo a casa",
            result.Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summarize_SpokenWhatsAppItalian_PlayStationVsTabletGems_IsCoherentNotMidClauseFragments()
    {
        // Run-on WhatsApp-style STT: few periods, discourse markers, contrast PS vs tablet gem rewards.
        var text =
            "allora ti dico della playstation cioè ieri ho comprato le gemme sulla playstation " +
            "e mi hanno dato il premio doppio però sul tablet invece la prima volta le gemme erano " +
            "moltiplicate per due no la prima volta mi sembra che abbiano cambiato però mi è sembrato " +
            "strano perché sulla playstation sì e sul tablet no quindi non so se conviene comprare " +
            "sul tablet o sulla playstation fammi sapere tu cosa faresti perché non voglio buttare soldi";

        var result = _sut.Summarize(text);

        Assert.Null(result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.True(result.Summary.Length < text.Length * 0.5,
            $"Expected shorter summary, got {result.Summary.Length} vs source {text.Length}: {result.Summary}");
        Assert.True(result.Bullets.Count is >= 2 and <= 4,
            $"Expected 2–4 bullets, got {result.Bullets.Count}: {string.Join(" | ", result.Bullets)}");

        // Must not be useless mid-clause fragments from the old splitter.
        Assert.All(result.Bullets, b =>
        {
            var first = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            Assert.False(
                first.Equals("che", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("tot", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("no", StringComparison.OrdinalIgnoreCase),
                $"Mid-clause fragment bullet: {b}");
            Assert.DoesNotContain("tot primo gem", b, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("che hanno cambiato però mi è sembrato strano", b, StringComparison.OrdinalIgnoreCase);
        });

        // Should capture the core contrast / topic (gems + platforms), not random slices.
        var joined = string.Join(' ', result.Bullets).ToLowerInvariant();
        Assert.True(
            joined.Contains("gem") || joined.Contains("premio") || joined.Contains("moltiplic"),
            $"Missing gem/reward topic: {joined}");
        Assert.True(
            joined.Contains("playstation") || joined.Contains("tablet"),
            $"Missing platform contrast: {joined}");

        // No bullet should equal a random interior substring that starts mid-thought with weak words.
        Assert.DoesNotContain(
            result.Bullets,
            b => Normalize(b).StartsWith("che hanno", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Bullets,
            b => Normalize(b).StartsWith("no la prima", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
