using System.Collections.Concurrent;

namespace SkillToCs.Engine;

public sealed class RuleRegistry
{
    private readonly ConcurrentDictionary<string, IRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IRule rule) =>
        _rules[rule.Name] = rule;

    public IReadOnlyList<IRule> GetAll() =>
        _rules.Values.ToList();

    public IRule? GetByName(string name) =>
        _rules.GetValueOrDefault(name);

    public IReadOnlyList<IRule> GetByCategory(string category) =>
        _rules.Values
            .Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public IReadOnlyList<IRule> GetApplicable(ProjectContext ctx) =>
        _rules.Values
            .Where(r => r.AppliesTo(ctx))
            .ToList();
}
