using AudioToText.Web.Options;
using Microsoft.Extensions.Options;

namespace AudioToText.Web.Services;

public sealed class AudioUploadValidator
{
    private readonly TranscriptionOptions _options;

    public AudioUploadValidator(IOptions<TranscriptionOptions> options)
    {
        _options = options.Value;
    }

    public string? Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Seleziona un file audio.";
        }

        if (file.Length > _options.MaxUploadBytes)
        {
            var mb = _options.MaxUploadBytes / (1024.0 * 1024.0);
            return $"File troppo grande (max {mb:0.#} MB).";
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(extension) ||
            !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return $"Formato non supportato ({extension}). Usa ogg, opus, m4a, mp3, wav o webm.";
        }

        return null;
    }
}
