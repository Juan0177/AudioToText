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
    }

    [Fact]
    public void Summarize_ShortItalianText_ReturnsCompactSummary()
    {
        var result = _sut.Summarize("Ciao, ti chiamo più tardi. Ok grazie.");
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.Contains("Ciao", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Riassunto locale (estrattivo)", result.MethodLabel);
    }

    [Fact]
    public void Summarize_LongText_ReturnsFewSentencesOrBullets()
    {
        var text =
            "Ciao Marco, ti scrivo per il viaggio di sabato. Partiamo alle nove dalla stazione centrale. " +
            "Porta il biglietto stampato e una bottiglia d'acqua. Il treno arriva a Firenze verso mezzogiorno. " +
            "Poi andiamo a piedi all'hotel vicino al Duomo. La cena è prenotata alle otto in un ristorante tipico. " +
            "Se piove portiamo gli ombrelli. Fammi sapere se hai bisogno di altro prima di sabato.";

        var result = _sut.Summarize(text);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.True(result.Summary.Length < text.Length);
        Assert.True(result.Bullets.Count is >= 0 and <= 4);
    }
}
