namespace F1.Core;

public enum AgentProfile { Help, Session }
public enum OverlayState { Hidden, Ready, Thinking, Answering, WaitingForInput, Error }
public sealed record ForegroundContext(string Application, string Title);
public sealed record AgentEvent(string Kind, string? Text = null, Interaction? Interaction = null);
public sealed record Interaction(string Id, string Method, string Title, string[] Options, string? Value = null, string? Message = null, int? TimeoutMilliseconds = null);
public sealed record Capability(string Name, string Description, string Source, bool Enabled);

public interface IAgentBackend : IAsyncDisposable
{
    event Action<AgentEvent>? EventReceived;
    Task StartAsync(AgentProfile profile, CancellationToken cancellationToken);
    Task ResetAsync(CancellationToken cancellationToken);
    Task SubmitAsync(string text, ForegroundContext? context, CancellationToken cancellationToken);
    Task CancelAsync(CancellationToken cancellationToken);
    Task RespondAsync(string id, string? value, bool? confirmed, bool cancelled);
    Task<IReadOnlyList<Capability>> InspectAsync(CancellationToken cancellationToken);
}

public sealed class Settings
{
    public string Hotkey { get; set; } = "F1";
    public bool CaptureForeground { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string PiEntryPath { get; set; } = "";
    public string NodePath { get; set; } = "node.exe";
    public string AgentDirectory { get; set; } = "";
    public string ModelCommand { get; set; } = "";
    public string[] Extensions { get; set; } = [];
    public string[] Skills { get; set; } = [];
    public string[] PromptTemplates { get; set; } = [];
    public string[] AllowedTools { get; set; } = ["f1_weather", "f1_reference"];
    public string[] AllowedCommands { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxToolCalls { get; set; } = 6;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string LocationLabel { get; set; } = "";
    public string WeatherEndpoint { get; set; } = "https://api.open-meteo.com/v1/forecast";
    public string WeatherCredential { get; set; } = "";

    public void Validate()
    {
        if (TimeoutSeconds is < 5 or > 300 || MaxToolCalls is < 1 or > 30)
            throw new ArgumentException("Use a timeout of 5–300 seconds and a tool limit of 1–30.");
        if (Latitude.HasValue != Longitude.HasValue || Latitude is < -90 or > 90 || Longitude is < -180 or > 180
            || Latitude is double lat && !double.IsFinite(lat) || Longitude is double lon && !double.IsFinite(lon))
            throw new ArgumentException("Supply both latitude and longitude, within their valid ranges.");
        if (!Uri.TryCreate(WeatherEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != "https" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Weather endpoint must be HTTPS without credentials, query, or fragment.");
        if (WeatherCredential.Length > 0 && !WeatherCredential.StartsWith("F1/", StringComparison.Ordinal))
            throw new ArgumentException("Credential reference must start with F1/.");
        foreach (var path in Extensions.Concat(Skills).Concat(PromptTemplates))
            if (!Path.IsPathFullyQualified(path) || (!File.Exists(path) && !Directory.Exists(path)))
                throw new ArgumentException("Selected resources must be existing absolute local paths.");
        if (AllowedTools.Any(x => CommandPolicy.ForbiddenTools.Contains(x)))
            throw new ArgumentException("Help cannot enable built-in shell or filesystem tools. Use Session instead.");
        if (AllowedCommands.Any(x => !CommandPolicy.ValidCommandName(x)))
            throw new ArgumentException("Enter command names without / or arguments; f1-* commands are reserved.");
        if (ModelCommand.Length > 0 && (!ModelCommand.StartsWith('/') || ModelCommand.Contains('\n') || ModelCommand.Contains('\r')
            || !CommandPolicy.ValidCommandName(ModelCommand.Split(' ', 2)[0][1..])))
            throw new ArgumentException("Model selection must be one slash command, for example /switch fast.");
    }
}

public static class CommandPolicy
{
    public static readonly HashSet<string> ForbiddenTools = new(StringComparer.Ordinal)
        { "bash", "powershell", "read", "write", "edit", "grep", "find", "ls" };
    public static bool ValidCommandName(string name) => name.Length > 0 && !name.StartsWith("f1-", StringComparison.OrdinalIgnoreCase)
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');
    public static bool Allowed(string input, Settings settings, AgentProfile profile)
    {
        var text = input.TrimStart();
        if (!text.StartsWith('/')) return true;
        var command = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0][1..];
        if (!ValidCommandName(command)) return false;
        return profile == AgentProfile.Session || settings.AllowedCommands.Contains(command, StringComparer.Ordinal);
    }
}
