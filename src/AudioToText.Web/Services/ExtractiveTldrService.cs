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
/// Offline extractive TLDR tuned for spoken / WhatsApp Italian STT (run-on, weak punctuation).
/// Builds thought-spans instead of mid-clause slices; never echoes the full source.
/// </summary>
public sealed class ExtractiveTldrService : ITldrService
{
    public const string TooShortMessage = "Testo troppo corto per un riassunto";

    private const int MinCharsForSummary = 120;
    private const int MinThoughtsForSummary = 2;
    private const int MaxBullets = 4;
    private const double MaxSummaryRatio = 0.40;
    private const int MinWordsPerThought = 8;
    private const int TargetWordsPerThought = 16;
    private const int MaxWordsPerThought = 24;
    private const int MaxBulletChars = 92;

    // Strong sentence boundaries (STT often lacks capitals after these).
    private static readonly Regex StrongSentenceSplit = new(
        @"(?<=[\.!\?…])\s+",
        RegexOptions.Compiled);

    // Spoken Italian discourse turns — prefer these over chopping on bare "e"/"ma".
    private static readonly Regex DiscourseSplit = new(
        @"\s+(?=(?:" +
        @"allora|comunque|insomma|quindi|però|pero|invece|cioè|cioe|" +
        @"perché|perche|poi|dopo|prima|alla\s+fine|tipo|" +
        @"e\s+poi|ma\s+poi|fammi\s+sapere|guarda|senti|ascolta|" +
        @"vabbè|vabbe|ok\s+quindi|quindi\s+ecco" +
        @")\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SoftClauseSplit = new(
        @"\s*[,;:]\s+|\s+[–—]\s+",
        RegexOptions.Compiled);

    private static readonly Regex WordSplit = new(@"[^\p{L}\p{N}']+", RegexOptions.Compiled);

    private static readonly HashSet<string> WeakStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "che", "e", "ed", "ma", "o", "od", "di", "da", "a", "ad", "in", "su", "per",
        "con", "tra", "fra", "se", "no", "si", "sì", "la", "il", "lo", "le", "gli",
        "un", "una", "uno", "dei", "delle", "degli", "del", "della", "nel", "nella",
        "al", "alla", "ai", "alle", "the", "and", "but", "or", "of", "to", "for"
    };

    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "allora", "comunque", "insomma", "tipo", "cioè", "cioe", "vabbè", "vabbe",
        "guarda", "senti", "ascolta", "ecco", "boh", "mah", "niente", "okay", "ok",
        "diciamo", "praticamente", "letteralmente", "veramente", "proprio"
    };

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
        "quindi", "invece", "però", "pero", "cioè", "cioe", "mi", "è", "sembrato", "sembra",
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

        var thoughts = BuildThoughtSpans(cleaned);

        if (cleaned.Length < MinCharsForSummary || thoughts.Count < MinThoughtsForSummary)
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

        var scores = ScoreThoughts(thoughts);
        var targetCount = Math.Clamp((int)Math.Ceiling(thoughts.Count / 2.2), 2, MaxBullets);
        var maxChars = Math.Max(120, (int)(cleaned.Length * MaxSummaryRatio));

        var ranked = scores
            .Select((score, index) => (score, index))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .ToList();

        var picked = new List<int>();
        var usedChars = 0;
        foreach (var (score, index) in ranked)
        {
            if (score <= 0 || picked.Count >= targetCount)
            {
                break;
            }

            var unit = thoughts[index];
            // Always allow the first two picks; then enforce char budget.
            if (picked.Count >= 2 && usedChars + unit.Length > maxChars)
            {
                continue;
            }

            // Near-duplicate only (same idea restated), not same-topic contrast.
            if (picked.Any(p => OverlapRatio(thoughts[p], unit) > 0.82))
            {
                continue;
            }

            picked.Add(index);
            usedChars += Math.Min(unit.Length, 140) + 1;
        }

        // Ensure at least two chronological thoughts when available.
        if (picked.Count < 2 && thoughts.Count >= 2)
        {
            foreach (var (score, index) in ranked)
            {
                if (picked.Contains(index) || score <= 0)
                {
                    continue;
                }

                picked.Add(index);
                if (picked.Count >= 2)
                {
                    break;
                }
            }
        }

        if (picked.Count == 0)
        {
            picked.Add(0);
            if (thoughts.Count > 1)
            {
                picked.Add(thoughts.Count > 2 ? 1 : thoughts.Count - 1);
            }
        }

        // Keep chronological order for readability.
        picked.Sort();
        var selected = picked
            .Select(i => FormatBullet(thoughts[i]))
            .Where(s => s.Length > 0 && !IsWeakFragment(s))
            .Take(MaxBullets)
            .ToList();

        if (selected.Count < 2 && thoughts.Count >= 2)
        {
            foreach (var thought in thoughts)
            {
                var bullet = FormatBullet(thought);
                if (bullet.Length == 0 || IsWeakFragment(bullet))
                {
                    continue;
                }

                if (selected.Any(s => OverlapRatio(s, bullet) > 0.82))
                {
                    continue;
                }

                selected.Add(bullet);
                if (selected.Count >= Math.Min(2, targetCount))
                {
                    break;
                }
            }
        }

        // Soft trim to budget while keeping ≥2 bullets when possible.
        while (selected.Count > 2 && selected.Sum(s => s.Length) > maxChars)
        {
            selected.RemoveAt(selected.Count - 1);
        }

        // If still over budget with exactly 2, compress the longer bullet.
        if (selected.Sum(s => s.Length) > maxChars && selected.Count >= 2)
        {
            selected = selected.Select(s => TruncateAtWord(s, Math.Min(MaxBulletChars, maxChars / selected.Count))).ToList();
        }

        var summary = string.Join(" ", selected);

        if (string.IsNullOrWhiteSpace(summary) ||
            IsEssentiallySame(summary, cleaned) ||
            summary.Length >= cleaned.Length)
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
            Bullets = selected.Count >= 2 ? selected.ToArray() : Array.Empty<string>(),
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

        return na.Length >= (int)(nb.Length * 0.9) &&
               nb.StartsWith(na[..Math.Min(40, na.Length)], StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWeakFragment(string bullet)
    {
        var b = Normalize(bullet).TrimEnd('…', '.', '!', '?');
        if (b.Length < 18 || ContentTokenCount(b) < 3)
        {
            return true;
        }

        return WeakStarters.Contains(FirstWord(b));
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

    private static List<string> BuildThoughtSpans(string text)
    {
        var seeds = new List<string>();
        foreach (var sentence in StrongSentenceSplit.Split(text))
        {
            var trimmed = sentence.Trim().Trim(',', ';', ':');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var discourseParts = DiscourseSplit.Split(trimmed)
                .Select(s => s.Trim().Trim(',', ';', ':'))
                .Where(s => s.Length > 0);

            foreach (var part in discourseParts)
            {
                if (CountWords(part) <= MaxWordsPerThought && part.Length <= 180)
                {
                    seeds.Add(part);
                    continue;
                }

                // Soft commas only — never bare "e"/"ma" mid-thought chops.
                var clauses = SoftClauseSplit.Split(part)
                    .Select(s => s.Trim().Trim(',', ';', ':'))
                    .Where(s => s.Length > 0)
                    .ToList();

                if (clauses.Count <= 1)
                {
                    seeds.AddRange(ChunkByWords(part, MaxWordsPerThought));
                }
                else
                {
                    seeds.AddRange(clauses);
                }
            }
        }

        return PackThoughts(seeds);
    }

    private static List<string> PackThoughts(List<string> seeds)
    {
        if (seeds.Count == 0)
        {
            return seeds;
        }

        var packed = new List<string>();
        var buffer = new StringBuilder();
        var bufferWords = 0;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var thought = buffer.ToString().Trim();
            buffer.Clear();
            bufferWords = 0;
            if (thought.Length > 0)
            {
                packed.Add(thought);
            }
        }

        foreach (var seed in seeds)
        {
            var words = CountWords(seed);
            var starter = FirstWord(seed);
            var weak = WeakStarters.Contains(starter);
            var seedComplete = EndsWithSentencePunct(seed);

            // Never leave a weak-starting fragment alone — glue to previous thought.
            if (weak && buffer.Length > 0)
            {
                buffer.Append(' ').Append(seed);
                bufferWords += words;
                if (bufferWords >= TargetWordsPerThought || EndsWithSentencePunct(seed))
                {
                    Flush();
                }

                continue;
            }

            // Completed sentence in buffer → don't merge with the next independent thought.
            if (buffer.Length > 0 && EndsWithSentencePunct(buffer.ToString()) && !weak)
            {
                Flush();
            }

            if (buffer.Length == 0)
            {
                buffer.Append(seed);
                bufferWords = words;
                if (seedComplete || bufferWords >= TargetWordsPerThought)
                {
                    Flush();
                }

                continue;
            }

            if (bufferWords + words <= MaxWordsPerThought && !seedComplete)
            {
                buffer.Append(' ').Append(seed);
                bufferWords += words;
                if (bufferWords >= TargetWordsPerThought)
                {
                    Flush();
                }
            }
            else if (bufferWords + words <= MaxWordsPerThought && seedComplete && bufferWords < MinWordsPerThought)
            {
                buffer.Append(' ').Append(seed);
                Flush();
            }
            else
            {
                Flush();
                buffer.Append(seed);
                bufferWords = words;
                if (seedComplete || bufferWords >= TargetWordsPerThought)
                {
                    Flush();
                }
            }
        }

        Flush();

        // Merge leftover tiny tails into the previous thought.
        var compact = new List<string>();
        foreach (var thought in packed)
        {
            if (compact.Count > 0 && CountWords(thought) < MinWordsPerThought)
            {
                compact[^1] = compact[^1] + " " + thought;
            }
            else
            {
                compact.Add(thought);
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

        // Prefer breaks near conjunctions inside the window.
        var chunks = new List<string>();
        var start = 0;
        while (start < words.Length)
        {
            var remaining = words.Length - start;
            if (remaining <= maxWords)
            {
                chunks.Add(string.Join(' ', words.AsSpan(start, remaining).ToArray()));
                break;
            }

            var end = start + maxWords;
            // Walk back to a softer join word if possible.
            var breakAt = end;
            for (var i = end; i > start + MinWordsPerThought; i--)
            {
                var w = words[i - 1];
                if (w.Equals("e", StringComparison.OrdinalIgnoreCase) ||
                    w.Equals("ma", StringComparison.OrdinalIgnoreCase) ||
                    w.Equals("poi", StringComparison.OrdinalIgnoreCase) ||
                    w.Equals("però", StringComparison.OrdinalIgnoreCase) ||
                    w.Equals("pero", StringComparison.OrdinalIgnoreCase) ||
                    w.Equals("quindi", StringComparison.OrdinalIgnoreCase))
                {
                    breakAt = i;
                    break;
                }
            }

            chunks.Add(string.Join(' ', words.AsSpan(start, breakAt - start).ToArray()));
            start = breakAt;
        }

        return chunks;
    }

    private static int CountWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool EndsWithSentencePunct(string text)
    {
        var t = text.TrimEnd();
        return t.Length > 0 && t[^1] is '.' or '!' or '?' or '…';
    }

    private static string FirstWord(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[0];
    }

    private static double[] ScoreThoughts(IReadOnlyList<string> thoughts)
    {
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tokenized = new List<string[]>(thoughts.Count);

        foreach (var thought in thoughts)
        {
            var tokens = Tokenize(thought);
            tokenized.Add(tokens);
            foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                docFreq[token] = docFreq.GetValueOrDefault(token) + 1;
            }
        }

        var scores = new double[thoughts.Count];
        for (var i = 0; i < thoughts.Count; i++)
        {
            var tokens = tokenized[i];
            if (tokens.Length < 3)
            {
                scores[i] = 0;
                continue;
            }

            if (WeakStarters.Contains(FirstWord(thoughts[i])))
            {
                scores[i] = 0;
                continue;
            }

            double score = 0;
            var fillers = 0;
            foreach (var token in tokens)
            {
                if (FillerWords.Contains(token))
                {
                    fillers++;
                    continue;
                }

                // Rare content words (appear in few thoughts) get more weight.
                var df = docFreq.GetValueOrDefault(token);
                score += 1.0 + Math.Log(1 + (thoughts.Count / (double)Math.Max(1, df)));
                if (token.Any(char.IsDigit))
                {
                    score += 1.2;
                }
            }

            var positionBoost = 1.0 + (0.08 * (1.0 - (double)i / Math.Max(1, thoughts.Count - 1)));
            var fillerPenalty = 1.0 - Math.Min(0.45, fillers * 0.12);
            var len = thoughts[i].Length;
            var lengthFactor = len < 36 ? 0.55 : len > 240 ? 0.75 : 1.0;
            // Deprioritize closing CTAs / “fammi sapere…” closers.
            var ctaPenalty = Regex.IsMatch(thoughts[i], @"\bfammi\s+sapere\b", RegexOptions.IgnoreCase)
                ? 0.35
                : 1.0;
            scores[i] = (score / Math.Sqrt(Math.Max(1, tokens.Length))) * positionBoost * fillerPenalty * lengthFactor * ctaPenalty;
        }

        return scores;
    }

    private static int ContentTokenCount(string text) => Tokenize(text).Length;

    private static string[] Tokenize(string sentence) =>
        WordSplit.Split(sentence)
            .Where(t => t.Length >= 3 && !Stopwords.Contains(t) && !FillerWords.Contains(t))
            .ToArray();

    private static double OverlapRatio(string a, string b)
    {
        var ta = Tokenize(a).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tb = Tokenize(b).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        var inter = ta.Count(t => tb.Contains(t));
        return inter / (double)Math.Min(ta.Count, tb.Count);
    }

    private static string FormatBullet(string sentence)
    {
        var s = sentence.Trim().TrimEnd(',', ';', ':');
        // Prefer the informative clause after "cioè" / "tipo".
        var clarifying = Regex.Match(s, @"\b(?:cioè|cioe|tipo)\s+(.+)$", RegexOptions.IgnoreCase);
        if (clarifying.Success && clarifying.Groups[1].Value.Split(' ').Length >= MinWordsPerThought)
        {
            s = clarifying.Groups[1].Value.Trim();
        }

        // Strip leading turn-taking markers for cleaner bullets.
        s = Regex.Replace(s,
            @"^(allora|comunque|insomma|cioè|cioe|tipo|guarda|senti|ascolta|vabbè|vabbe|però|pero|quindi|poi|invece)\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        // Drop trailing spoken hesitation / self-correction tails (keep the lead clause).
        s = Regex.Replace(s,
            @"\s+\bno\b\s+la\s+prima\s+volta\b[\s\S]*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

        if (s.Length == 0)
        {
            return string.Empty;
        }

        // Capitalize first letter (STT often lowercase).
        if (char.IsLetter(s[0]) && char.IsLower(s[0]))
        {
            s = char.ToUpperInvariant(s[0]) + s[1..];
        }

        s = TruncateAtWord(s, MaxBulletChars);

        if (WeakStarters.Contains(FirstWord(s)) || ContentTokenCount(s) < 3)
        {
            return string.Empty;
        }

        return s;
    }

    private static string TruncateAtWord(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
        if (cut < Math.Max(40, maxChars / 2))
        {
            cut = Math.Min(maxChars, text.Length);
        }

        return text[..cut].TrimEnd(',', ';', ':', '.', '!', ' ') + "…";
    }
}
