using AudioToText.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudioToText.Web.Controllers;

[ApiController]
[Route("api/transcriptions")]
public sealed class TranscriptionsController : ControllerBase
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITldrService _tldr;
    private readonly AudioUploadValidator _validator;
    private readonly ILogger<TranscriptionsController> _logger;

    public TranscriptionsController(
        ITranscriptionService transcriptionService,
        ITldrService tldr,
        AudioUploadValidator validator,
        ILogger<TranscriptionsController> logger)
    {
        _transcriptionService = transcriptionService;
        _tldr = tldr;
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

            // Never block transcription on TLDR failures.
            TldrResult? tldr = null;
            try
            {
                tldr = _tldr.Summarize(result.Text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TLDR generation failed; returning full text only.");
            }

            return Ok(new
            {
                text = result.Text,
                language = result.DetectedLanguage,
                wordCount = result.WordCount,
                tldr
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

    /// <summary>
    /// Regenerates a local extractive TLDR from existing transcript text (offline).
    /// </summary>
    [HttpPost("tldr")]
    public ActionResult<object> CreateTldr([FromBody] TldrRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Testo mancante per il riassunto." });
        }

        if (request.Text.Length > 200_000)
        {
            return BadRequest(new { error = "Testo troppo lungo per il riassunto." });
        }

        var tldr = _tldr.Summarize(request.Text);
        return Ok(new { tldr });
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

public sealed class TldrRequest
{
    public string? Text { get; set; }
}
