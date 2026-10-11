using System.Text;
using System.Text.RegularExpressions;

namespace AudioToText.Web.Services;

public sealed class TldrResult
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Bullets { get; init; } = Array.Empty<string>();
    public string Method { get; init; } = "extractive";
    public string MethodLabel { get; init; } = "Riassunto locale (estrattivo)";
}

public interface ITldrService
{
    TldrResult Summarize(string? text);
}

/// <summary>
/// Offline extractive TLDR: frequency-scored sentences, no cloud/LLM.
/// </summary>
public sealed class ExtractiveTldrService : ITldrService
{
    private static readonly Regex SentenceSplit = new(
        @"(?<=[\.!\?…])\s+(?=[A-ZÀ-ÖØ-Þ«""'\(\[]|\d)|(?<=[\.!\?…])\s*\n+",
        RegexOptions.Compiled);

    private static readonly Regex WordSplit = new(@"[^\p{L}\p{N}']+", RegexOptions.Compiled);

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Italian
        "a", "ad", "al", "allo", "ai", "agli", "all", "agl", "alla", "alle", "con", "col", "coi",
        "da", "dal", "dallo", "dai", "dagli", "dall", "dagl", "dalla", "dalle", "di", "del",
        "dello", "dei", "degli", "dell", "degl", "della", "delle", "in", "nel", "nello", "nei",
        "negli", "nell", "negl", "nella", "nelle", "su", "sul", "sullo", "sui", "sugli", "sull",
        "sugl", "sulla", "sulle", "per", "tra", "fra", "e", "ed", "o", "od", "ma", "se", "che",
        "chi", "cui", "non", "più", "piu", "come", "dove", "quando", "quanto", "quale", "quali",
        "questo", "questa", "questi", "queste", "quello", "quella", "quelli", "quelle", "un",
        "uno", "una", "uno", "il", "lo", "la", "i", "gli", "le", "mi", "ti", "ci", "vi", "si",
        "io", "tu", "lui", "lei", "noi", "voi", "loro", "sono", "sei", "è", "e'", "siamo",
        "siete", "essere", "ho", "hai", "ha", "abbiamo", "avete", "hanno", "avere", "fare",
        "fatto", "anche", "già", "gia", "ancora", "molto", "poco", "tutto", "tutti", "tutte",
        "cosa", "cose", "qui", "qua", "li", "là", "la", "poi", "dopo", "prima", "ora", "oggi",
        "ieri", "domani", "bene", "male", "così", "cosi", "perché", "perche", "perciò",
        "quindi", "invece", "però", "pero", "cioè", "cioe", "degli", "degli",
        // English (mixed transcripts)
        "the", "a", "an", "and", "or", "but", "if", "then", "so", "to", "of", "in", "on", "for",
        "with", "at", "by", "from", "as", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "this", "that", "these", "those", "it", "its",
        "we", "you", "they", "he", "she", "i", "me", "my", "our", "your", "their", "not", "no",
        "yes", "just", "about", "into", "over", "after", "before", "also", "very", "can", "will"
    };

    public TldrResult Summarize(string? text)
    {
        var cleaned = Normalize(text);
        if (cleaned.Length == 0)
        {
            return new TldrResult
            {
                Summary = string.Empty,
                Bullets = Array.Empty<string>(),
                Method = "extractive",
                MethodLabel = "Riassunto locale (estrattivo)"
            };
        }

        var sentences = SplitSentences(cleaned);
        if (sentences.Count == 0)
        {
            sentences = new List<string> { cleaned };
        }

        // Short texts: return as compact summary (no artificial "summary").
        if (cleaned.Length <= 280 || sentences.Count <= 2)
        {
            var shortSummary = string.Join(" ", sentences.Take(2));
            return new TldrResult
            {
                Summary = shortSummary,
                Bullets = sentences.Count == 1
                    ? Array.Empty<string>()
                    : sentences.Take(3).Select(TrimBullet).Where(s => s.Length > 0).ToArray(),
                Method = "extractive",
                MethodLabel = "Riassunto locale (estrattivo)"
            };
        }

        var scores = ScoreSentences(sentences);
        var targetCount = Math.Clamp(sentences.Count / 4, 2, 4);
        var selectedIndexes = scores
            .Select((score, index) => (score, index))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Take(targetCount)
            .Select(x => x.index)
            .OrderBy(i => i)
            .ToList();

        var selected = selectedIndexes.Select(i => sentences[i]).ToList();
        var summary = string.Join(" ", selected);
        var bullets = selected.Select(TrimBullet).Where(s => s.Length > 0).Take(4).ToArray();

        return new TldrResult
        {
            Summary = summary,
            Bullets = bullets,
            Method = "extractive",
            MethodLabel = "Riassunto locale (estrattivo)"
        };
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var prevSpace = false;
        foreach (var ch in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (ch is '\n' or '\t')
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            sb.Append(ch);
            prevSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static List<string> SplitSentences(string text)
    {
        var parts = SentenceSplit.Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (parts.Count <= 1 && text.Contains('\n'))
        {
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToList();
        }

        // Fallback: split on "; " for long run-on Italian speech-to-text.
        if (parts.Count == 1 && text.Length > 400)
        {
            var bySemi = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 20)
                .ToList();
            if (bySemi.Count >= 2)
            {
                return bySemi;
            }
        }

        return parts;
    }

    private static double[] ScoreSentences(IReadOnlyList<string> sentences)
    {
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tokenized = new List<string[]>(sentences.Count);

        foreach (var sentence in sentences)
        {
            var tokens = Tokenize(sentence);
            tokenized.Add(tokens);
            foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                docFreq[token] = docFreq.GetValueOrDefault(token) + 1;
            }
        }

        var scores = new double[sentences.Count];
        for (var i = 0; i < sentences.Count; i++)
        {
            var tokens = tokenized[i];
            if (tokens.Length == 0)
            {
                scores[i] = 0;
                continue;
            }

            double score = 0;
            foreach (var token in tokens)
            {
                score += 1.0 + Math.Log(1 + docFreq.GetValueOrDefault(token));
            }

            // Prefer slightly earlier sentences (WhatsApp vocals often front-load intent).
            var positionBoost = 1.0 + (0.15 * (1.0 - (double)i / Math.Max(1, sentences.Count - 1)));
            // Mild length preference: avoid tiny fragments and mega-sentences.
            var len = sentences[i].Length;
            var lengthFactor = len < 40 ? 0.6 : len > 320 ? 0.75 : 1.0;
            scores[i] = (score / tokens.Length) * positionBoost * lengthFactor;
        }

        return scores;
    }

    private static string[] Tokenize(string sentence) =>
        WordSplit.Split(sentence)
            .Where(t => t.Length >= 3 && !Stopwords.Contains(t))
            .ToArray();

    private static string TrimBullet(string sentence)
    {
        var s = sentence.Trim();
        if (s.Length > 180)
        {
            s = s[..177].TrimEnd() + "…";
        }

        return s;
    }
}
