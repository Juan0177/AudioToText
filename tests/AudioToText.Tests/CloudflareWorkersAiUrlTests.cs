using AudioToText.Web.Services;

namespace AudioToText.Tests;

public class CloudflareWorkersAiUrlTests
{
    [Fact]
    public void BuildRunUrl_MatchesCloudflareAiRunPath()
    {
        var url = CloudflareWorkersAiTranscriptionService.BuildRunUrl(
            "https://api.cloudflare.com/client/v4",
            "acct123",
            "@cf/openai/whisper");

        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/acct123/ai/run/@cf/openai/whisper",
            url);
        Assert.DoesNotContain("%2F", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%40", url);
    }

    [Fact]
    public void BuildRunUrl_TrimsTrailingBaseSlash()
    {
        var url = CloudflareWorkersAiTranscriptionService.BuildRunUrl(
            "https://api.cloudflare.com/client/v4/",
            "acct123",
            "@cf/openai/whisper-large-v3-turbo");

        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/acct123/ai/run/@cf/openai/whisper-large-v3-turbo",
            url);
    }
}
