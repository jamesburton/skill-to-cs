using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkillToCs.Engine;

public sealed class SkillConfig
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Version { get; set; } = "1.0.0";
    public ConfigSettings Settings { get; set; } = new();
    public Dictionary<string, RuleConfig> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Disabled { get; set; } = [];

    /// <summary>
    /// Load config with cascading: built-in defaults &lt; user config &lt; project config.
    /// </summary>
    public static SkillConfig Load(string projectPath)
    {
        // Start with built-in defaults
        var config = new SkillConfig();

        // Layer user config
        var userConfig = LoadUser();
        DeepMerge(config, userConfig);

        // Layer project config
        var projectConfigPath = Path.Combine(projectPath, ".skill-to-cs", "config.json");
        if (File.Exists(projectConfigPath))
        {
            var projectJson = File.ReadAllText(projectConfigPath);
            var projectConfig = JsonSerializer.Deserialize<SkillConfig>(projectJson, s_jsonOptions);
            if (projectConfig is not null)
            {
                DeepMerge(config, projectConfig, applyRuleClear: true);
            }
        }

        return config;
    }

    /// <summary>
    /// Load user-level config from ~/.skill-to-cs/config.json.
    /// </summary>
    public static SkillConfig LoadUser()
    {
        var userConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".skill-to-cs",
            "config.json");

        if (!File.Exists(userConfigPath))
            return new SkillConfig();

        var json = File.ReadAllText(userConfigPath);
        return JsonSerializer.Deserialize<SkillConfig>(json, s_jsonOptions) ?? new SkillConfig();
    }

    /// <summary>
    /// Write config to the specified path as indented camelCase JSON.
    /// </summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, s_jsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Deep-merge <paramref name="overlay"/> onto <paramref name="target"/>.
    /// When <paramref name="applyRuleClear"/> is true and a rule has Clear=true,
    /// that rule's config replaces inherited values entirely.
    /// </summary>
    private static void DeepMerge(SkillConfig target, SkillConfig overlay, bool applyRuleClear = false)
    {
        if (!string.IsNullOrEmpty(overlay.Version))
            target.Version = overlay.Version;

        // Merge settings
        MergeSettings(target.Settings, overlay.Settings);

        // Merge disabled list (union)
        foreach (var d in overlay.Disabled)
        {
            if (!target.Disabled.Contains(d, StringComparer.OrdinalIgnoreCase))
                target.Disabled.Add(d);
        }

        // Merge rules
        foreach (var (ruleName, overlayRule) in overlay.Rules)
        {
            if (applyRuleClear && overlayRule.Clear)
            {
                // Clear means: ignore inherited, use only overlay values
                target.Rules[ruleName] = new RuleConfig
                {
                    Defaults = new Dictionary<string, object?>(overlayRule.Defaults),
                    Heuristics = overlayRule.Heuristics,
                    Clear = false // Don't persist the clear flag after merging
                };
            }
            else if (target.Rules.TryGetValue(ruleName, out var existingRule))
            {
                // Merge defaults
                foreach (var (key, value) in overlayRule.Defaults)
                    existingRule.Defaults[key] = value;

                // Merge heuristics
                if (overlayRule.Heuristics is not null)
                {
                    existingRule.Heuristics ??= new HeuristicOverrides();
                    if (overlayRule.Heuristics.ActThreshold.HasValue)
                        existingRule.Heuristics.ActThreshold = overlayRule.Heuristics.ActThreshold;
                    if (overlayRule.Heuristics.SuggestThreshold.HasValue)
                        existingRule.Heuristics.SuggestThreshold = overlayRule.Heuristics.SuggestThreshold;
                }
            }
            else
            {
                target.Rules[ruleName] = new RuleConfig
                {
                    Defaults = new Dictionary<string, object?>(overlayRule.Defaults),
                    Heuristics = overlayRule.Heuristics,
                    Clear = false
                };
            }
        }
    }

    private static void MergeSettings(ConfigSettings target, ConfigSettings overlay)
    {
        // Only override if different from built-in defaults (we treat 0 as "not set" for threshold)
        if (overlay.CoverageThreshold != 80 || target.CoverageThreshold == 80)
            target.CoverageThreshold = overlay.CoverageThreshold;

        target.TreatWarningsAsErrors = overlay.TreatWarningsAsErrors;

        if (overlay.ExcludePaths.Count > 0)
            target.ExcludePaths = new List<string>(overlay.ExcludePaths);
    }
}

public sealed class ConfigSettings
{
    public int CoverageThreshold { get; set; } = 80;
    public bool TreatWarningsAsErrors { get; set; } = false;
    public List<string> ExcludePaths { get; set; } = ["**/Generated/**", "**/Migrations/**"];
}

public sealed class RuleConfig
{
    public Dictionary<string, object?> Defaults { get; set; } = [];
    public HeuristicOverrides? Heuristics { get; set; }
    public bool Clear { get; set; } = false;
}

public sealed class HeuristicOverrides
{
    public double? ActThreshold { get; set; }
    public double? SuggestThreshold { get; set; }
}
