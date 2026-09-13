using System.Text.Json;
using F1.Core;

namespace F1.Infrastructure;

public sealed class SettingsStore(string? directory = null)
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Miralie", "F1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public Settings Load()
    {
        var path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Settings are invalid.");
        settings.Validate();
        return settings;
    }
    public void Save(Settings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "settings.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, path, true);
    }
}

/// <summary>Only fixed categories and numeric measurements may enter diagnostics.</summary>
public sealed class Diagnostics(string directory)
{
    public void Record(DiagnosticKind kind, long milliseconds = 0)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {kind} {milliseconds}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
public enum DiagnosticKind { Started, RequestCompleted, RequestFailed, ProcessFailed, Cancelled, HotkeyVisible, UnexpectedError }
