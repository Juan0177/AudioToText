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
        // Whisper-style: little capitalization after periods — previously returned full text.
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
        Assert.DoesNotContain("fammi sapere se sabato usciamo al parco oppure restiamo a casa", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
