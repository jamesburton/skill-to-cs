using System.Text.Json;

namespace SkillToCs.Engine;

public sealed record ValidationResult(bool IsValid, List<string> Errors, List<string> Warnings);

public sealed class RuleParams
{
    private readonly Dictionary<string, object?> _values;
    private readonly RuleSchema _schema;

    public RuleParams(Dictionary<string, object?> values, RuleSchema schema)
    {
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        _schema = schema;
    }

    public T? Get<T>(string name)
    {
        if (_values.TryGetValue(name, out var raw) && raw is not null)
            return ConvertValue<T>(raw);

        var def = _schema.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (def?.DefaultValue is not null)
            return ConvertValue<T>(def.DefaultValue);

        return default;
    }

    public bool TryGet<T>(string name, out T? value)
    {
        if (_values.TryGetValue(name, out var raw) && raw is not null)
        {
            value = ConvertValue<T>(raw);
            return true;
        }

        value = default;
        return false;
    }

    public bool Has(string name) =>
        _values.ContainsKey(name) && _values[name] is not null;

    public static RuleParams Parse(string json, RuleSchema schema)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? [];

        // JsonSerializer deserializes unknown values as JsonElement; unwrap them.
        var unwrapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, val) in dict)
            unwrapped[key] = UnwrapJsonElement(val);

        return new RuleParams(unwrapped, schema);
    }

    public static RuleParams ParseCliArgs(string[] args, RuleSchema schema)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--"))
                continue;

            var name = arg[2..];
            var paramDef = schema.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (paramDef is not null && paramDef.Type is ParamType.BoolType)
            {
                // Bool flags: --flag means true, --flag true/false consumes next arg
                if (i + 1 < args.Length && bool.TryParse(args[i + 1], out var boolVal))
                {
                    dict[name] = boolVal;
                    i++;
                }
                else
                {
                    dict[name] = true;
                }
            }
            else if (i + 1 < args.Length)
            {
                var value = args[++i];
                if (paramDef?.Type is ParamType.IntType && int.TryParse(value, out var intVal))
                    dict[name] = intVal;
                else
                    dict[name] = value;
            }
        }

        return new RuleParams(dict, schema);
    }

    public ValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check required fields
        foreach (var param in _schema.Parameters.Where(p => p.Required))
        {
            if (!Has(param.Name))
                errors.Add($"Required parameter '{param.Name}' is missing.");
        }

        // Check type compatibility
        foreach (var (name, value) in _values)
        {
            if (value is null) continue;

            var paramDef = _schema.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (paramDef is null)
            {
                warnings.Add($"Unknown parameter '{name}'.");
                continue;
            }

            if (!IsTypeCompatible(value, paramDef.Type))
                errors.Add($"Parameter '{name}' has incompatible type. Expected {paramDef.Type.GetType().Name}.");
        }

        // Evaluate block rules
        foreach (var block in _schema.Blocks)
        {
            if (BlockEvaluator.Evaluate(block.Condition, this))
            {
                var message = block.Message;
                if (block.Severity == BlockSeverity.Error)
                    errors.Add(message);
                else
                    warnings.Add(message);
            }
        }

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    private static bool IsTypeCompatible(object value, ParamType type) => type switch
    {
        ParamType.StringType => value is string or JsonElement { ValueKind: JsonValueKind.String },
        ParamType.IntType => value is int or long or JsonElement { ValueKind: JsonValueKind.Number },
        ParamType.BoolType => value is bool or JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False },
        ParamType.EnumType e => value is string s && e.Values.Contains(s, StringComparer.OrdinalIgnoreCase),
        ParamType.ArrayType => value is IEnumerable<object?> or JsonElement { ValueKind: JsonValueKind.Array },
        ParamType.ObjectType => value is IDictionary<string, object?> or JsonElement { ValueKind: JsonValueKind.Object },
        _ => true
    };

    private static T? ConvertValue<T>(object value)
    {
        if (value is T typed)
            return typed;

        if (value is JsonElement je)
            return JsonSerializer.Deserialize<T>(je.GetRawText());

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private static object? UnwrapJsonElement(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt32(out var i) => i,
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDouble(),
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        _ => value
    };
}
