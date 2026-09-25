using System.Text.Json;

namespace Gplus.State;

internal static class JsonFile
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    public static T LoadOr<T>(string path, Func<T> fallback)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opts) ?? fallback();
        }
        catch (JsonException) { /* corrupt state is rebuilt, never fatal */ }
        return fallback();
    }

    /// <summary>Write-to-temp then rename, so a crash mid-write can't leave a truncated file.</summary>
    public static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Opts));
        File.Move(tmp, path, overwrite: true);
    }
}
