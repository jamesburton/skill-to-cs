namespace SkillToCs.Engine;

public static class BlockEvaluator
{
    public static bool Evaluate(string condition, RuleParams parameters)
    {
        // Split on || first (lower precedence), then && (higher precedence)
        var orGroups = condition.Split("||", StringSplitOptions.TrimEntries);

        foreach (var orGroup in orGroups)
        {
            var andClauses = orGroup.Split("&&", StringSplitOptions.TrimEntries);
            var allTrue = true;

            foreach (var clause in andClauses)
            {
                if (!EvaluateComparison(clause.Trim(), parameters))
                {
                    allTrue = false;
                    break;
                }
            }

            if (allTrue)
                return true;
        }

        return false;
    }

    private static bool EvaluateComparison(string comparison, RuleParams parameters)
    {
        // Try != first (so we don't match the = in != as ==)
        if (TrySplitOperator(comparison, "!=", out var leftNe, out var rightNe))
            return !AreEqual(leftNe, rightNe, parameters);

        if (TrySplitOperator(comparison, "==", out var leftEq, out var rightEq))
            return AreEqual(leftEq, rightEq, parameters);

        // Bare parameter name: truthy check
        var trimmed = comparison.Trim();
        return parameters.Has(trimmed);
    }

    private static bool TrySplitOperator(string expression, string op, out string left, out string right)
    {
        var idx = expression.IndexOf(op, StringComparison.Ordinal);
        if (idx >= 0)
        {
            left = expression[..idx].Trim();
            right = expression[(idx + op.Length)..].Trim();
            return true;
        }

        left = right = string.Empty;
        return false;
    }

    private static bool AreEqual(string left, string right, RuleParams parameters)
    {
        var leftVal = ResolveValue(left, parameters);
        var rightVal = ResolveValue(right, parameters);

        if (leftVal is null && rightVal is null) return true;
        if (leftVal is null || rightVal is null) return false;

        return string.Equals(
            leftVal.ToString(),
            rightVal.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static object? ResolveValue(string token, RuleParams parameters)
    {
        if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
            return null;

        // Quoted string literal
        if ((token.StartsWith('\'') && token.EndsWith('\'')) ||
            (token.StartsWith('"') && token.EndsWith('"')))
            return token[1..^1];

        // Otherwise treat as parameter name
        parameters.TryGet<object>(token, out var value);
        return value;
    }
}
