using AudioToText.Web.Options;
using AudioToText.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AudioToText.Tests;

public class AudioUploadValidatorTests
{
    private readonly AudioUploadValidator _validator = new(
        Options.Create(new TranscriptionOptions
        {
            MaxUploadBytes = 1024,
            AllowedExtensions = [".mp3", ".ogg"]
        }));

    [Fact]
    public void Validate_NullFile_ReturnsError()
    {
        var error = _validator.Validate(null);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_UnsupportedExtension_ReturnsError()
    {
        var file = CreateFile("note.txt", "text/plain", 10);
        var error = _validator.Validate(file);
        Assert.Contains("Formato non supportato", error);
    }

    [Fact]
    public void Validate_TooLarge_ReturnsError()
    {
        var file = CreateFile("note.mp3", "audio/mpeg", 2048);
        var error = _validator.Validate(file);
        Assert.Contains("troppo grande", error);
    }

    [Fact]
    public void Validate_ValidFile_ReturnsNull()
    {
        var file = CreateFile("note.ogg", "audio/ogg", 64);
        Assert.Null(_validator.Validate(file));
    }

    private static IFormFile CreateFile(string name, string contentType, int length)
    {
        var bytes = new byte[length];
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
