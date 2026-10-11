using Whisper.net.Ggml;

namespace AudioToText.Web.Services;

public sealed record WhisperModelInfo(
    string Id,
    string FileName,
    long ApproxBytes,
    string DownloadSizeLabel,
    string RamHint,
    string VramHint,
    string Recommend);

public static class WhisperModelCatalog
{
    // Exact ggml sizes from huggingface.co/ggerganov/whisper.cpp
    public static readonly IReadOnlyList<WhisperModelInfo> All =
    [
        new("tiny", "ggml-tiny.bin", 77_691_713, "~74 MB", "≥1 GB RAM", "≥1 GB VRAM (opz.)",
            "1050 Ti / CPU: ok"),
        new("base", "ggml-base.bin", 147_951_465, "~141 MB", "≥2 GB RAM", "≥2 GB VRAM (opz.)",
            "1050 Ti: consigliato; 5070: ok"),
        new("small", "ggml-small.bin", 487_601_967, "~465 MB", "≥4 GB RAM", "≥4 GB VRAM",
            "1050 Ti: rischioso; 5070: consigliato"),
        new("medium", "ggml-medium.bin", 1_533_763_059, "~1,4 GB", "≥8 GB RAM", "≥8 GB VRAM",
            "Solo GPU recenti (es. 5070); non per 1050 Ti")
    ];

    public static WhisperModelInfo Get(string model)
    {
        var id = (model ?? "base").Trim().ToLowerInvariant();
        return All.FirstOrDefault(m => m.Id == id) ?? All.First(m => m.Id == "base");
    }

    public static GgmlType ToGgmlType(string model) =>
        Get(model).Id switch
        {
            "tiny" => GgmlType.Tiny,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            _ => GgmlType.Base
        };

    public static string ToFileName(string model) => Get(model).FileName;

    public static string HuggingFaceUrl(string model) =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{ToFileName(model)}";
}
