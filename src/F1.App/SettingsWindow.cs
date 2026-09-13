using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using F1.Core;
using F1.Infrastructure;

namespace F1.App;

internal sealed class SettingsWindow : Window
{
    public Settings Result { get; private set; }
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly CheckBox capture = new() { Content = "Include foreground app and window title" };
    private readonly CheckBox startup = new() { Content = "Start with Windows" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly PasswordBox weatherKey = new();
    private readonly string directory;
    public SettingsWindow(Settings current, string directory)
    {
        this.directory = directory;
        Result = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(current))!;
        Title = "F1 Settings"; Width = 700; Height = 760; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Help uses only selected local Pi resources. Loading an extension runs trusted code with your Windows account permissions.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        Add(panel, "Hotkey", current.Hotkey);
        capture.IsChecked = current.CaptureForeground; startup.IsChecked = current.StartWithWindows;
        panel.Children.Add(capture); panel.Children.Add(startup);
        Add(panel, "Pi cli.js path (blank: standard npm installation)", current.PiEntryPath);
        Add(panel, "Node executable", current.NodePath);
        Add(panel, "Pi agent directory (blank: Pi default)", current.AgentDirectory);
        Add(panel, "Model command (optional, e.g. /switch fast)", current.ModelCommand);
        Add(panel, "Extension paths (one installed local path per line)", string.Join('\n', current.Extensions), true);
        Add(panel, "Skill paths (one per line)", string.Join('\n', current.Skills), true);
        Add(panel, "Prompt template paths (one per line)", string.Join('\n', current.PromptTemplates), true);
        Add(panel, "Help tool names (one per line)", string.Join('\n', current.AllowedTools), true);
        Add(panel, "Help command names (without /, one per line)", string.Join('\n', current.AllowedCommands), true);
        var inspect = new Button { Content = "Inspect selected capabilities / test Pi" };
        inspect.Click += async (_, _) =>
        {
            inspect.IsEnabled = false;
            try
            {
                Read();
                await using var backend = new PiBackend(Result, directory, Path.Combine(AppContext.BaseDirectory, "pi-package", "index.ts"));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await backend.StartAsync(AgentProfile.Help, timeout.Token);
                var capabilities = await backend.InspectAsync(timeout.Token);
                status.Text = "Pi connected. This tests RPC, not a paid model request.\n" + string.Join('\n', capabilities.Select(c => $"{(c.Enabled ? "Enabled" : "Disabled")}: {c.Name} — {c.Description}\nSource: {c.Source}"));
            }
            catch (Exception ex) { status.Text = ex is ArgumentException or InvalidOperationException ? ex.Message : "Pi inspection failed. Check version 0.85.1, paths, and extension compatibility."; }
            finally { inspect.IsEnabled = true; }
        };
        panel.Children.Add(inspect);
        Add(panel, "Timeout seconds", current.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        Add(panel, "Maximum tool calls", current.MaxToolCalls.ToString(CultureInfo.InvariantCulture));
        Add(panel, "Location label", current.LocationLabel);
        Add(panel, "Latitude (optional)", current.Latitude?.ToString(CultureInfo.InvariantCulture) ?? "");
        Add(panel, "Longitude (optional)", current.Longitude?.ToString(CultureInfo.InvariantCulture) ?? "");
        Add(panel, "Weather HTTPS endpoint", current.WeatherEndpoint);
        Add(panel, "Weather credential reference (optional, F1/name)", current.WeatherCredential);
        panel.Children.Add(new TextBlock { Text = "New weather API key (optional; saved only to Windows Credential Manager)" });
        panel.Children.Add(weatherKey);
        panel.Children.Add(new TextBlock { Text = "Open-Meteo's free service is for noncommercial use. Use a licensed commercial endpoint or self-host for other uses. Questions, foreground hints, and tool results may be sent to your configured model provider.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var save = new Button { Content = "Save" };
        save.Click += (_, _) =>
        {
            try
            {
                Read(); WindowsIntegration.ParseHotkey(Result.Hotkey);
                if (weatherKey.Password.Length > 0)
                {
                    if (Result.WeatherCredential.Length == 0) Result.WeatherCredential = "F1/weather";
                    CredentialStore.Write(Result.WeatherCredential, weatherKey.Password); weatherKey.Clear();
                }
                DialogResult = true;
            }
            catch (Exception ex) { status.Text = ex is ArgumentException or FormatException or OverflowException ? ex.Message : "Could not save the credential. Check Windows Credential Manager."; }
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
    }
    private void Add(Panel panel, string label, string value, bool multiline = false)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 0) });
        var input = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 65 : 32, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        fields[label] = input; panel.Children.Add(input);
    }
    private void Read()
    {
        string Get(string label) => fields[label].Text.Trim();
        string[] Lines(string label) => Get(label).Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        double? Number(string label) => Get(label).Length == 0 ? null : double.Parse(Get(label), CultureInfo.InvariantCulture);
        Result.Hotkey = Get("Hotkey"); Result.CaptureForeground = capture.IsChecked == true; Result.StartWithWindows = startup.IsChecked == true;
        Result.PiEntryPath = Get("Pi cli.js path (blank: standard npm installation)"); Result.NodePath = Get("Node executable");
        Result.AgentDirectory = Get("Pi agent directory (blank: Pi default)"); Result.ModelCommand = Get("Model command (optional, e.g. /switch fast)");
        Result.Extensions = Lines("Extension paths (one installed local path per line)"); Result.Skills = Lines("Skill paths (one per line)");
        Result.PromptTemplates = Lines("Prompt template paths (one per line)"); Result.AllowedTools = Lines("Help tool names (one per line)");
        Result.AllowedCommands = Lines("Help command names (without /, one per line)");
        Result.TimeoutSeconds = int.Parse(Get("Timeout seconds"), CultureInfo.InvariantCulture); Result.MaxToolCalls = int.Parse(Get("Maximum tool calls"), CultureInfo.InvariantCulture);
        Result.LocationLabel = Get("Location label"); Result.Latitude = Number("Latitude (optional)"); Result.Longitude = Number("Longitude (optional)");
        Result.WeatherEndpoint = Get("Weather HTTPS endpoint"); Result.WeatherCredential = Get("Weather credential reference (optional, F1/name)");
        Result.Validate();
    }
}
