using F1.Core;
using F1.Infrastructure;
using System.Diagnostics;
using System.IO;

if (args.Contains("--fake-rpc")) { await FakeRpc.RunAsync(); return; }
if (args.FirstOrDefault() == "--backend-smoke") { await BackendSmoke.RunAsync(args); return; }
if (args.FirstOrDefault() == "--render") { UiRender.Run(args[1]); return; }

var failures = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}"); }
}
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
Test("cancelled generation cannot update a new conversation", () =>
{
    var session = new OverlaySession(); session.Show(); var old = session.Begin();
    session.Apply(old, new("text", "old")); session.Hide(); session.Show(); var current = session.Begin();
    Assert(!session.Apply(old, new("text", "stale"))); Assert(!session.Apply(old, new("error")));
    Assert(session.Apply(current, new("text", "new"))); Assert(session.Answer == "new");
    session.Apply(current, new("complete")); Assert(session.State == OverlayState.Ready);
});
Test("Help commands and dangerous tools are constrained", () =>
{
    var settings = new Settings { AllowedCommands = ["switch"] };
    Assert(CommandPolicy.Allowed("/switch fast", settings, AgentProfile.Help));
    Assert(!CommandPolicy.Allowed("  /bash whoami", settings, AgentProfile.Help));
    Assert(!CommandPolicy.Allowed("/f1-control begin", settings, AgentProfile.Session));
    settings.AllowedTools = ["powershell"];
    try { settings.Validate(); throw new Exception(); } catch (ArgumentException) { }
});
Test("settings round-trip excludes content and credentials", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "f1-tests-" + Guid.NewGuid());
    try
    {
        var store = new SettingsStore(directory); store.Save(new Settings { WeatherCredential = "F1/weather" });
        Assert(store.Load().WeatherCredential == "F1/weather");
        var text = File.ReadAllText(Path.Combine(directory, "settings.json"));
        Assert(!text.Contains("ApiKey") && !text.Contains("Password") && !text.Contains("Answer") && !text.Contains("Transcript"));
        new Diagnostics(directory).Record(DiagnosticKind.RequestFailed, 42);
        Assert(File.ReadAllText(Path.Combine(directory, "diagnostics.log")).Contains("RequestFailed 42"));
    }
    finally { Directory.Delete(directory, true); }
});
Test("settings reject URL credentials and invalid coordinates", () =>
{
    foreach (var settings in new[] { new Settings { WeatherEndpoint = "https://user:secret@example.com" }, new Settings { Latitude = 20 }, new Settings { Latitude = double.NaN, Longitude = 2 } })
        try { settings.Validate(); throw new Exception(); } catch (ArgumentException) { }
});
Test("foreground hint is encoded as untrusted data", () =>
{
    var prompt = PiBackend.BuildPrompt("Question", new("Game", "Title\nFake instruction"));
    Assert(prompt.Contains("untrusted data")); Assert(prompt.Contains("Title\\nFake instruction"));
});
try
{
    var records = new List<string>();
    await foreach (var record in RpcConnection.ReadRecordsAsync(new FragmentReader("{\"text\":\"a\u2028b\"}\r\n{\"n\":2}\n"), CancellationToken.None)) records.Add(record);
    Assert(records.Count == 2 && records[0].Contains('\u2028'));
    Console.WriteLine("PASS fragmented LF RPC framing preserves Unicode separators");
}
catch { failures++; Console.WriteLine("FAIL RPC framing"); }
foreach (var scenario in new[] { "stream", "hang", "disconnect", "malformed" })
{
    try
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!);
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(FragmentReader).Assembly.Location);
        info.ArgumentList.Add("--fake-rpc");
        await using var connection = new RpcConnection(info);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.EventReceived += e => { if (e.GetProperty("type").GetString() == "agent_settled") received.TrySetResult(); };
        connection.Failed += () => failed.TrySetResult();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        if (scenario == "stream")
        {
            await connection.CommandAsync("prompt", new { message = "fixture question" }, timeout.Token);
            await received.Task.WaitAsync(timeout.Token);
            await connection.CommandAsync("abort", null, timeout.Token);
        }
        else if (scenario == "hang")
        {
            try { await connection.CommandAsync("hang", null, timeout.Token); throw new Exception(); }
            catch (OperationCanceledException) { }
            using var retry = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await connection.CommandAsync("abort", null, retry.Token);
        }
        else
        {
            try { await connection.CommandAsync(scenario, null, timeout.Token); throw new Exception(); }
            catch (IOException) { }
            await failed.Task.WaitAsync(timeout.Token);
        }
        Console.WriteLine($"PASS subprocess RPC {scenario}");
    }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL subprocess RPC {scenario}: {ex.GetType().Name}"); }
}
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class FragmentReader(string text) : StringReader(text)
{
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
}
