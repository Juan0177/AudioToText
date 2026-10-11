using Whisper.net.Ggml;

namespace AudioToText.Web.Services;

public static class WhisperModelCatalog
{
    public static GgmlType ToGgmlType(string model) =>
        model.Trim().ToLowerInvariant() switch
        {
            "tiny" => GgmlType.Tiny,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            _ => GgmlType.Base
        };

    public static string ToFileName(string model) =>
        model.Trim().ToLowerInvariant() switch
        {
            "tiny" => "ggml-tiny.bin",
            "small" => "ggml-small.bin",
            "medium" => "ggml-medium.bin",
            _ => "ggml-base.bin"
        };
}
