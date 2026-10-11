using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioToText.Web.Services;

/// <summary>
/// Converts uploaded audio to 16 kHz mono PCM WAV for Whisper.net.
/// Prefers ffmpeg (WhatsApp ogg/opus); falls back to NAudio for WAV/MP3 where available.
/// </summary>
public sealed class AudioPcmConverter
{
    private readonly ILogger<AudioPcmConverter> _logger;

    public AudioPcmConverter(ILogger<AudioPcmConverter> logger)
    {
        _logger = logger;
    }

    public bool IsFfmpegAvailable() => ResolveFfmpegPath() is not null;

    public async Task<byte[]> ToWav16kMonoAsync(
        Stream input,
        string fileName,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
        var tempIn = Path.Combine(Path.GetTempPath(), $"att-in-{Guid.NewGuid():N}{extension}");
        var tempOut = Path.Combine(Path.GetTempPath(), $"att-out-{Guid.NewGuid():N}.wav");

        try
        {
            await using (var fs = File.Create(tempIn))
            {
                await input.CopyToAsync(fs, cancellationToken);
            }

            var ffmpeg = ResolveFfmpegPath();
            if (ffmpeg is not null)
            {
                await RunFfmpegAsync(ffmpeg, tempIn, tempOut, cancellationToken);
                return await File.ReadAllBytesAsync(tempOut, cancellationToken);
            }

            if (extension is ".wav" or ".mp3")
            {
                return ConvertWithNAudio(tempIn);
            }

            throw new InvalidOperationException(
                "ffmpeg non trovato. Serve per vocali WhatsApp (ogg/opus/m4a/webm). " +
                "Installa ffmpeg e aggiungilo al PATH, oppure usa un file WAV/MP3.");
        }
        finally
        {
            TryDelete(tempIn);
            TryDelete(tempOut);
        }
    }

    private static byte[] ConvertWithNAudio(string path)
    {
        using var reader = new AudioFileReader(path);
        var resampler = new WdlResamplingSampleProvider(reader, 16000);
        var mono = resampler.ToMono();
        using var ms = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(ms, new SampleToWaveProvider16(mono));
        return ms.ToArray();
    }

    private async Task RunFfmpegAsync(
        string ffmpeg,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            ArgumentList =
            {
                "-y",
                "-i", inputPath,
                "-ar", "16000",
                "-ac", "1",
                "-c:a", "pcm_s16le",
                outputPath
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare ffmpeg.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.LogWarning("ffmpeg failed (exit {Code}): {Err}", process.ExitCode, Truncate(stderr, 400));
            throw new InvalidOperationException(
                "Conversione audio fallita (ffmpeg). Prova un altro formato o reinstalla ffmpeg.");
        }
    }

    private static string? ResolveFfmpegPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
