using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkillToCs.Output;

public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, Options));
    }

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}
