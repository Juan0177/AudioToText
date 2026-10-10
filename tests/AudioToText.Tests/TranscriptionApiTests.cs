using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AudioToText.Tests;

public class TranscriptionApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TranscriptionApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Transcription:Provider", "Fake");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
        }).CreateClient();
    }

    [Fact]
    public async Task Post_WithoutFile_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        var response = await _client.PostAsync("/api/transcriptions", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithAudio_ReturnsFakeTranscript()
    {
        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(bytes, "file", "sample.mp3");
        content.Add(new StringContent("italian"), "language");

        var response = await _client.PostAsync("/api/transcriptions", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.Contains("Fake", text);
        Assert.Contains("sample.mp3", text);
    }

    [Fact]
    public async Task Get_Index_ReturnsHtml()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("AudioToText", html);
    }
}
