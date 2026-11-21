namespace NoticeForge.Domain;

/// <summary>
/// Checks whether an SPDX license expression is satisfied by the allow list.
/// OR needs any one allowed, AND needs all allowed. "X WITH exception" needs only X allowed
/// (an exception only adds permissions). Precedence: WITH &gt; AND &gt; OR.
/// </summary>
public static class LicenseExpression
{
    /// <exception cref="FormatException">The expression cannot be parsed.</exception>
    public static bool IsAllowed(string expression, IReadOnlyCollection<string> allowed)
    {
        var tokens = Tokenize(expression);
        int pos = 0;
        var set = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        var result = ParseOr(tokens, ref pos, set);
        if (pos != tokens.Count) throw new FormatException($"unexpected '{tokens[pos]}' in license expression: {expression}");
        return result;
    }

    static List<string> Tokenize(string s) =>
        s.Replace("(", " ( ").Replace(")", " ) ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    static bool ParseOr(List<string> t, ref int pos, HashSet<string> allowed)
    {
        var any = ParseAnd(t, ref pos, allowed);
        while (pos < t.Count && t[pos].Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            pos++;
            any |= ParseAnd(t, ref pos, allowed);
        }
        return any;
    }

    static bool ParseAnd(List<string> t, ref int pos, HashSet<string> allowed)
    {
        var all = ParseWith(t, ref pos, allowed);
        while (pos < t.Count && t[pos].Equals("AND", StringComparison.OrdinalIgnoreCase))
        {
            pos++;
            all &= ParseWith(t, ref pos, allowed);
        }
        return all;
    }

    static bool ParseWith(List<string> t, ref int pos, HashSet<string> allowed)
    {
        var start = pos;
        var ok = ParseAtom(t, ref pos, allowed);
        if (pos < t.Count && t[pos].Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            if (pos + 1 >= t.Count) throw new FormatException("WITH needs an exception id");
            // "X WITH Y" may also be written as a whole in the allow list
            ok |= allowed.Contains($"{t[start]} WITH {t[pos + 1]}");
            pos += 2;
        }
        return ok;
    }

    static bool ParseAtom(List<string> t, ref int pos, HashSet<string> allowed)
    {
        if (pos >= t.Count) throw new FormatException("license expression ended unexpectedly");
        if (t[pos] == "(")
        {
            pos++;
            var v = ParseOr(t, ref pos, allowed);
            if (pos >= t.Count || t[pos] != ")") throw new FormatException("missing ')' in license expression");
            pos++;
            return v;
        }
        var id = t[pos++];
        if (id is ")" || id.Equals("AND", StringComparison.OrdinalIgnoreCase) || id.Equals("OR", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"unexpected '{id}' in license expression");
        // "or later" ids like "Apache-2.0+" pass if the base id is allowed
        return allowed.Contains(id) || (id.EndsWith('+') && allowed.Contains(id[..^1]));
    }
}
