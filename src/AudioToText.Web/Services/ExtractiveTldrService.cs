using System.Text;
using System.Text.RegularExpressions;

namespace AudioToText.Web.Services;

public sealed class TldrResult
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> Bullets { get; init; } = Array.Empty<string>();
    public string Method { get; init; } = "extractive";
    public string MethodLabel { get; init; } = "Riassunto locale (estrattivo)";
    /// <summary>User-facing note when no summary is produced (e.g. text too short).</summary>
    public string? Message { get; init; }
}

public interface ITldrService
{
    TldrResult Summarize(string? text);
}

/// <summary>
/// Offline extractive TLDR: clause/sentence scoring. Never returns the full source as the summary.
/// </summary>
public sealed class ExtractiveTldrService : ITldrService
{
    public const string TooShortMessage = "Testo troppo corto per un riassunto";

    private const int MinCharsForSummary = 120;
    private const int MinUnitsForSummary = 3;
    private const int MaxBullets = 4;
    private const double MaxSummaryRatio = 0.40;

    // Split after . ! ? … even when next letter is lowercase (common in Italian STT).
    private static readonly Regex StrongSentenceSplit = new(
        @"(?<=[\.!\?…])\s+",
        RegexOptions.Compiled);

    private static readonly Regex SoftClauseSplit = new(
        @"\s*[,;:]\s+|\s+[–—-]\s+|\s+\be\s+(?=\p{L})|\s+\bma\s+(?=\p{L})|\s+\bpoi\s+(?=\p{L})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WordSplit = new(@"[^\p{L}\p{N}']+", RegexOptions.Compiled);

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "ad", "al", "allo", "ai", "agli", "all", "agl", "alla", "alle", "con", "col", "coi",
        "da", "dal", "dallo", "dai", "dagli", "dall", "dagl", "dalla", "dalle", "di", "del",
        "dello", "dei", "degli", "dell", "degl", "della", "delle", "in", "nel", "nello", "nei",
        "negli", "nell", "negl", "nella", "nelle", "su", "sul", "sullo", "sui", "sugli", "sull",
        "sugl", "sulla", "sulle", "per", "tra", "fra", "e", "ed", "o", "od", "ma", "se", "che",
        "chi", "cui", "non", "più", "piu", "come", "dove", "quando", "quanto", "quale", "quali",
        "questo", "questa", "questi", "queste", "quello", "quella", "quelli", "quelle", "un",
        "uno", "una", "il", "lo", "la", "i", "gli", "le", "mi", "ti", "ci", "vi", "si",
        "io", "tu", "lui", "lei", "noi", "voi", "loro", "sono", "sei", "è", "siamo",
        "siete", "essere", "ho", "hai", "ha", "abbiamo", "avete", "hanno", "avere", "fare",
        "fatto", "anche", "già", "gia", "ancora", "molto", "poco", "tutto", "tutti", "tutte",
        "cosa", "cose", "qui", "qua", "li", "là", "poi", "dopo", "prima", "ora", "oggi",
        "ieri", "domani", "bene", "male", "così", "cosi", "perché", "perche", "perciò",
        "quindi", "invece", "però", "pero", "cioè", "cioe",
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
            return EmptyResult();
        }

        var units = SplitIntoUnits(cleaned);

        // Too short / not enough clauses → explicit message, never echo full text.
        if (cleaned.Length < MinCharsForSummary || units.Count < MinUnitsForSummary)
        {
            return new TldrResult
            {
                Summary = string.Empty,
                Bullets = Array.Empty<string>(),
                Method = "extractive",
                MethodLabel = "Riassunto locale (estrattivo)",
                Message = TooShortMessage
            };
        }

        var scores = ScoreUnits(units);
        var targetCount = Math.Clamp((int)Math.Ceiling(units.Count / 3.0), 2, MaxBullets);
        // Cap by character budget (~40% of source).
        var maxChars = Math.Max(80, (int)(cleaned.Length * MaxSummaryRatio));

        var selectedIndexes = scores
            .Select((score, index) => (score, index))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Select(x => x.index)
            .ToList();

        var picked = new List<int>();
        var usedChars = 0;
        foreach (var index in selectedIndexes)
        {
            if (picked.Count >= targetCount)
            {
                break;
            }

            var unit = units[index];
            if (picked.Count > 0 && usedChars + unit.Length > maxChars)
            {
                continue;
            }

            picked.Add(index);
            usedChars += unit.Length + 1;
        }

        if (picked.Count == 0)
        {
            picked.Add(selectedIndexes[0]);
        }

        picked.Sort();
        var selected = picked.Select(i => units[i]).Select(TrimBullet).Where(s => s.Length > 0).ToList();
        var summary = string.Join(" ", selected);

        // Hard guard: never equal / near-equal to the source.
        if (IsEssentiallySame(summary, cleaned) || summary.Length > maxChars + 40)
        {
            selected = selected.Take(Math.Max(1, Math.Min(2, selected.Count))).ToList();
            summary = string.Join(" ", selected);
            if (IsEssentiallySame(summary, cleaned))
            {
                summary = TrimBullet(selected[0]);
                selected = new List<string> { summary };
            }
        }

        if (IsEssentiallySame(summary, cleaned) || summary.Length >= cleaned.Length)
        {
            return new TldrResult
            {
                Summary = string.Empty,
                Bullets = Array.Empty<string>(),
                Method = "extractive",
                MethodLabel = "Riassunto locale (estrattivo)",
                Message = TooShortMessage
            };
        }

        return new TldrResult
        {
            Summary = summary,
            Bullets = selected.Count >= 2 ? selected.Take(MaxBullets).ToArray() : Array.Empty<string>(),
            Method = "extractive",
            MethodLabel = "Riassunto locale (estrattivo)"
        };
    }

    private static TldrResult EmptyResult() => new()
    {
        Summary = string.Empty,
        Bullets = Array.Empty<string>(),
        Method = "extractive",
        MethodLabel = "Riassunto locale (estrattivo)"
    };

    private static bool IsEssentiallySame(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0)
        {
            return false;
        }

        if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Near-equal: summary is ≥90% of source length and shares the same start.
        return na.Length >= (int)(nb.Length * 0.9) &&
               nb.StartsWith(na[..Math.Min(40, na.Length)], StringComparison.OrdinalIgnoreCase);
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
            if (ch is '\n' or '\t' || char.IsWhiteSpace(ch))
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

    private static List<string> SplitIntoUnits(string text)
    {
        var strong = StrongSentenceSplit.Split(text)
            .Select(s => s.Trim().Trim(',', ';', ':'))
            .Where(s => s.Length > 0)
            .ToList();

        var units = new List<string>();
        foreach (var part in strong)
        {
            if (part.Length <= 160 && CountWords(part) <= 28)
            {
                units.Add(part);
                continue;
            }

            // Long STT blob: break into clauses, then word-window chunks if needed.
            var clauses = SoftClauseSplit.Split(part)
                .Select(s => s.Trim().Trim(',', ';', ':'))
                .Where(s => s.Length > 12)
                .ToList();

            if (clauses.Count <= 1)
            {
                units.AddRange(ChunkByWords(part, maxWords: 18));
            }
            else
            {
                foreach (var clause in clauses)
                {
                    if (CountWords(clause) > 28)
                    {
                        units.AddRange(ChunkByWords(clause, maxWords: 18));
                    }
                    else
                    {
                        units.Add(clause);
                    }
                }
            }
        }

        // Merge tiny fragments into neighbors so scoring stays meaningful.
        return CompactTinyUnits(units);
    }

    private static List<string> CompactTinyUnits(List<string> units)
    {
        if (units.Count <= 1)
        {
            return units;
        }

        var compact = new List<string>();
        foreach (var unit in units)
        {
            if (compact.Count > 0 && unit.Length < 24)
            {
                compact[^1] = compact[^1] + " " + unit;
            }
            else
            {
                compact.Add(unit);
            }
        }

        return compact;
    }

    private static List<string> ChunkByWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= maxWords)
        {
            return new List<string> { text };
        }

        var chunks = new List<string>();
        for (var i = 0; i < words.Length; i += maxWords)
        {
            var take = Math.Min(maxWords, words.Length - i);
            chunks.Add(string.Join(' ', words.AsSpan(i, take).ToArray()));
        }

        return chunks;
    }

    private static int CountWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static double[] ScoreUnits(IReadOnlyList<string> units)
    {
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tokenized = new List<string[]>(units.Count);

        foreach (var unit in units)
        {
            var tokens = Tokenize(unit);
            tokenized.Add(tokens);
            foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                docFreq[token] = docFreq.GetValueOrDefault(token) + 1;
            }
        }

        var scores = new double[units.Count];
        for (var i = 0; i < units.Count; i++)
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

            var positionBoost = 1.0 + (0.1 * (1.0 - (double)i / Math.Max(1, units.Count - 1)));
            var len = units[i].Length;
            var lengthFactor = len < 28 ? 0.5 : len > 220 ? 0.7 : 1.0;
            scores[i] = (score / Math.Sqrt(tokens.Length)) * positionBoost * lengthFactor;
        }

        return scores;
    }

    private static string[] Tokenize(string sentence) =>
        WordSplit.Split(sentence)
            .Where(t => t.Length >= 3 && !Stopwords.Contains(t))
            .ToArray();

    private static string TrimBullet(string sentence)
    {
        var s = sentence.Trim().TrimEnd(',', ';', ':');
        if (s.Length > 160)
        {
            s = s[..157].TrimEnd() + "…";
        }

        return s;
    }
}
