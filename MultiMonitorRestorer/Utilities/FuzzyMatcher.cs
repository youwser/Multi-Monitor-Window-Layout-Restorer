using MultiMonitorRestorer.Core;

namespace MultiMonitorRestorer.Utilities;

/// <summary>
/// Lightweight fuzzy string matcher used to identify windows whose titles
/// change between sessions (e.g. browser tab names, VS Code project names).
/// </summary>
internal static class FuzzyMatcher
{
    /// <summary>
    /// Minimum similarity ratio [0.0 – 1.0] required to consider two titles
    /// the same window. Tune this if you see too many false positives/negatives.
    /// </summary>
    public const double DefaultThreshold = 0.60;

    /// <summary>
    /// Scores similarity between a stored window title and a currently-observed
    /// title for windows with the same process name.
    /// </summary>
    /// <returns>A value in [0.0, 1.0], where 1.0 means identical.</returns>
    public static double Score(string stored, string current)
    {
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(current))
            return 0.0;

        // Fast path: exact match
        if (string.Equals(stored, current, StringComparison.OrdinalIgnoreCase))
            return 1.0;

        var a = stored.ToLowerInvariant();
        var b = current.ToLowerInvariant();

        // Score 1: Longest Common Subsequence ratio
        double lcsRatio = (2.0 * LcsLength(a, b)) / (a.Length + b.Length);

        // Score 2: Dice coefficient on trigrams (handles reordered words)
        double diceScore = DiceCoefficient(a, b, 3);

        // Score 3: Prefix bonus — many apps just append " - AppName" to a doc title
        double prefixBonus = 0.0;
        int prefixLen = CommonPrefixLength(a, b);
        if (prefixLen > 5)
            prefixBonus = (double)prefixLen / Math.Max(a.Length, b.Length) * 0.2;

        return Math.Min(1.0, lcsRatio * 0.5 + diceScore * 0.3 + prefixBonus);
    }

    /// <summary>
    /// Picks the best-matching HWND for a given snapshot from a dictionary of
    /// candidate windows keyed by (processName, title).
    /// </summary>
    /// <param name="snapshot">The stored window to find a live match for.</param>
    /// <param name="candidates">
    ///   All currently live windows sharing the same process name.
    ///   Key = window title; Value = HWND.
    /// </param>
    /// <param name="threshold">Minimum score to accept a match.</param>
    /// <returns>The best-matching HWND, or <see cref="IntPtr.Zero"/> if none qualifies.</returns>
    public static IntPtr FindBestMatch(
        WindowSnapshot snapshot,
        Dictionary<string, IntPtr> candidates,
        double threshold = DefaultThreshold)
    {
        IntPtr bestHandle = IntPtr.Zero;
        double bestScore  = threshold - double.Epsilon; // must beat threshold

        foreach (var (title, hwnd) in candidates)
        {
            double s = Score(snapshot.WindowTitle, title);
            if (s > bestScore)
            {
                bestScore  = s;
                bestHandle = hwnd;
            }
        }

        return bestHandle;
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private static int LcsLength(string a, string b)
    {
        // Space-optimised DP: O(min(m,n)) space
        if (a.Length < b.Length) (a, b) = (b, a); // ensure a is longer
        int n = b.Length;
        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                curr[j] = a[i - 1] == b[j - 1]
                    ? prev[j - 1] + 1
                    : Math.Max(curr[j - 1], prev[j]);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[n];
    }

    private static double DiceCoefficient(string a, string b, int n)
    {
        var setA = GetNgrams(a, n);
        var setB = GetNgrams(b, n);
        if (setA.Count == 0 || setB.Count == 0) return 0.0;

        int intersection = 0;
        foreach (var gram in setA)
            if (setB.Contains(gram)) intersection++;

        return (2.0 * intersection) / (setA.Count + setB.Count);
    }

    private static HashSet<string> GetNgrams(string s, int n)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i <= s.Length - n; i++)
            result.Add(s.Substring(i, n));
        return result;
    }

    private static int CommonPrefixLength(string a, string b)
    {
        int limit = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }
}
