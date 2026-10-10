using AudioToText.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudioToText.Web.Controllers;

[ApiController]
[Route("api/transcriptions")]
public sealed class TranscriptionsController : ControllerBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly AudioUploadValidator _validator;
    private readonly ILogger<TranscriptionsController> _logger;

    public TranscriptionsController(
        ITranscriptionService transcriptionService,
        AudioUploadValidator validator,
        ILogger<TranscriptionsController> logger)
    {
        _transcriptionService = transcriptionService;
        _validator = validator;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 30 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        IFormFile? file,
        [FromForm] string? language,
        CancellationToken cancellationToken)
    {
        var validationError = _validator.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var normalizedLanguage = NormalizeLanguage(language);

        try
        {
            await using var stream = file!.OpenReadStream();
            var result = await _transcriptionService.TranscribeAsync(
                stream,
                file.FileName,
                file.ContentType,
                normalizedLanguage,
                cancellationToken);

            return Ok(new
            {
                text = result.Text,
                language = result.DetectedLanguage,
                wordCount = result.WordCount
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Transcription rejected for {File}", file!.FileName);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected transcription failure for {File}", file!.FileName);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Errore durante la trascrizione. Riprova tra poco."
            });
        }
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Accept UI values: italian | it | en | ...
        return language.Trim().ToLowerInvariant() switch
        {
            "italian" => "it",
            "italiano" => "it",
            var code when code.Length is >= 2 and <= 8 => code,
            _ => null
        };
    }
}
