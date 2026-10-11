using AudioToText.Web.Services;

namespace AudioToText.Tests;

public class WhisperModelCatalogTests
{
    [Theory]
    [InlineData("tiny", 77_691_713)]
    [InlineData("base", 147_951_465)]
    [InlineData("small", 487_601_967)]
    [InlineData("medium", 1_533_763_059)]
    public void Get_HasExpectedApproxBytes(string id, long bytes)
    {
        var info = WhisperModelCatalog.Get(id);
        Assert.Equal(id, info.Id);
        Assert.Equal(bytes, info.ApproxBytes);
        Assert.False(string.IsNullOrWhiteSpace(info.DownloadSizeLabel));
        Assert.False(string.IsNullOrWhiteSpace(info.RamHint));
        Assert.False(string.IsNullOrWhiteSpace(info.VramHint));
    }

    [Fact]
    public void All_ContainsFourModels()
    {
        Assert.Equal(4, WhisperModelCatalog.All.Count);
    }
}
