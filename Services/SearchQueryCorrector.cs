using System.Text.RegularExpressions;

namespace GitHubTopicReports.Services;

public sealed record SearchQueryCorrection(
    string OriginalQuery,
    string CorrectedQuery);

public static class SearchQueryCorrector
{
    private static readonly Regex TermRegex = new(
        @"[A-Za-z][A-Za-z0-9+#.]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CjkRegex = new(
        @"[\u3400-\u9fff]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SearchQueryCorrection? Correct(
        string query,
        IEnumerable<string> vocabulary)
    {
        var original = query.Trim();
        if (original.Length == 0 ||
            CjkRegex.IsMatch(original) ||
            !TermRegex.IsMatch(original))
        {
            return null;
        }

        var terms = vocabulary
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var changed = false;
        var corrected = TermRegex.Replace(original, match =>
        {
            var token = match.Value;
            if (token.Length < 4 ||
                token.Any(character =>
                    char.IsDigit(character) ||
                    character is '+' or '#' or '.') ||
                terms.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                return token;
            }

            var best = FindClosest(token, terms);
            if (best is null)
            {
                return token;
            }

            changed = true;
            return best;
        });

        return changed &&
               !string.Equals(original, corrected, StringComparison.OrdinalIgnoreCase)
            ? new SearchQueryCorrection(original, corrected)
            : null;
    }

    private static string? FindClosest(string token, IReadOnlyList<string> terms)
    {
        var maximumDistance = token.Length >= 8 ? 2 : 1;
        string? best = null;
        var bestDistance = int.MaxValue;
        var bestPrefix = -1;
        var bestLengthDifference = int.MaxValue;

        foreach (var term in terms)
        {
            if (term.Length < 4 ||
                Math.Abs(term.Length - token.Length) > maximumDistance ||
                char.ToLowerInvariant(term[0]) != char.ToLowerInvariant(token[0]))
            {
                continue;
            }

            var distance = DamerauLevenshtein(token, term, maximumDistance);
            if (distance < 1 || distance > maximumDistance)
            {
                continue;
            }

            var commonPrefix = CommonPrefixLength(token, term);
            var lengthDifference = Math.Abs(term.Length - token.Length);
            if (distance < bestDistance ||
                distance == bestDistance && commonPrefix > bestPrefix ||
                distance == bestDistance &&
                commonPrefix == bestPrefix &&
                lengthDifference < bestLengthDifference)
            {
                best = term;
                bestDistance = distance;
                bestPrefix = commonPrefix;
                bestLengthDifference = lengthDifference;
            }
        }

        return best;
    }

    private static int DamerauLevenshtein(
        string source,
        string target,
        int maximumDistance)
    {
        if (Math.Abs(source.Length - target.Length) > maximumDistance)
        {
            return maximumDistance + 1;
        }

        var previousPrevious = new int[target.Length + 1];
        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];
        for (var column = 0; column <= target.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= source.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= target.Length; column++)
            {
                var cost = char.ToLowerInvariant(source[row - 1]) ==
                           char.ToLowerInvariant(target[column - 1])
                    ? 0
                    : 1;
                current[column] = Math.Min(
                    Math.Min(
                        current[column - 1] + 1,
                        previous[column] + 1),
                    previous[column - 1] + cost);

                if (row > 1 &&
                    column > 1 &&
                    char.ToLowerInvariant(source[row - 1]) ==
                    char.ToLowerInvariant(target[column - 2]) &&
                    char.ToLowerInvariant(source[row - 2]) ==
                    char.ToLowerInvariant(target[column - 1]))
                {
                    current[column] = Math.Min(
                        current[column],
                        previousPrevious[column - 2] + 1);
                }

                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximumDistance)
            {
                return maximumDistance + 1;
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[target.Length];
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = 0;
        while (length < left.Length &&
               length < right.Length &&
               char.ToLowerInvariant(left[length]) == char.ToLowerInvariant(right[length]))
        {
            length++;
        }

        return length;
    }
}
