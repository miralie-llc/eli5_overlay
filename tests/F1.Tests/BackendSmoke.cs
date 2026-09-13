using F1.Core;
using F1.Infrastructure;

static class BackendSmoke
{
    public static async Task RunAsync(string[] args)
    {
        var settings = new Settings { PiEntryPath = args[1], AgentDirectory = args[2], Extensions = [args[4]],
            ModelCommand = "/fixture-switch fast", AllowedCommands = ["fixture-confirm"], CaptureForeground = false, MaxToolCalls = 2 };
        await using var backend = new PiBackend(settings, args[3], args[5]);
        var text = "";
        var firstChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogSeen = false;
        backend.EventReceived += value =>
        {
            if (value.Kind == "text") { text += value.Text; firstChunk.TrySetResult(); }
            if (value.Interaction is { } interaction)
            {
                if (interaction.Title != "Fixture confirmation" || interaction.Message != "Synthetic action only.") throw new Exception("Dialog details were lost.");
                dialogSeen = true;
                _ = backend.RespondAsync(interaction.Id, null, true, false);
            }
        };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await backend.StartAsync(AgentProfile.Help, limit.Token);
        await backend.ResetAsync(limit.Token);
        var capabilities = await backend.InspectAsync(limit.Token);
        if (!capabilities.Any(c => c.Name == "f1_weather" && c.Enabled)) throw new Exception("Starter missing");
        await backend.SubmitAsync("fixture hello", new("Fixture", "Fixture title"), limit.Token);
        if (text != "Fixture answer.") throw new Exception("Streaming failed");
        await backend.SubmitAsync("fixture followup", null, limit.Token);
        await backend.SubmitAsync("/fixture-confirm", null, limit.Token);
        if (!dialogSeen) throw new Exception("Dialog missing");
        text = "";
        await backend.SubmitAsync("fixture budget", null, limit.Token);
        if (!text.Contains("tool-call limit", StringComparison.Ordinal)) throw new Exception("Budget stop was not visible");
        firstChunk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var slow = backend.SubmitAsync("fixture slow", null, cancellation.Token);
        await firstChunk.Task.WaitAsync(limit.Token); cancellation.Cancel();
        try { await slow; throw new Exception("Cancellation did not propagate"); } catch (OperationCanceledException) { }
        await backend.ResetAsync(limit.Token);
        text = "";
        await backend.SubmitAsync("fixture fresh", null, limit.Token);
        if (text != "Fixture answer.") throw new Exception("Stale output after reset");
        await backend.StartAsync(AgentProfile.Session, limit.Token);
        settings.ModelCommand = "";
        await backend.SubmitAsync("fixture session", null, limit.Token);
        await backend.StartAsync(AgentProfile.Help, limit.Token);
        settings.ModelCommand = "/missing-command fast";
        try { await backend.SubmitAsync("must never reach model", null, limit.Token); throw new Exception("Missing model command accepted"); }
        catch (InvalidOperationException) { }
        Console.WriteLine("PASS C# backend with actual Pi and local model fixture: streaming, follow-ups, model command, dialog, tool budget, cancellation, reset, profile separation");
    }
}
