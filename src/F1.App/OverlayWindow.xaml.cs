using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using F1.Core;
using F1.Infrastructure;

namespace F1.App;

public partial class OverlayWindow : Window
{
    private readonly Settings settings;
    private readonly IAgentBackend backend;
    private readonly Diagnostics diagnostics;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? request;
    private Task active = Task.CompletedTask;
    private readonly OverlaySession session = new();
    private ForegroundContext? context;
    private IntPtr previousWindow;
    private long requestId;
    private bool permanentClose, changingProfile, ready;
    private int conversationCharacters;
    private InteractionWindow? interactionWindow;
    public OverlayWindow(Settings settings, string directory, IAgentBackend? injectedBackend = null)
    {
        this.settings = settings;
        diagnostics = new(directory);
        backend = injectedBackend ?? new PiBackend(settings, directory, Path.Combine(AppContext.BaseDirectory, "pi-package", "index.ts"));
        InitializeComponent();
        backend.EventReceived += OnAgentEvent;
    }
    public async Task WarmAsync()
    {
        await lifecycle.WaitAsync();
        try { await backend.StartAsync(AgentProfile.Help, CancellationToken.None); diagnostics.Record(DiagnosticKind.Started); }
        catch { Status.Text = "Pi is not ready. Open tray Settings to configure it."; }
        finally { lifecycle.Release(); }
    }
    public async void Invoke()
    {
        if (IsVisible) { WindowsIntegration.Focus(this); Question.Focus(); return; }
        var stopwatch = Stopwatch.StartNew();
        var captured = WindowsIntegration.Capture(); previousWindow = captured.Handle;
        context = settings.CaptureForeground ? captured.Context : null;
        ContextLabel.Text = context is null ? "Foreground context off" : $"Context: {context.Application} — {context.Title}";
        session.Show(); Answer.Clear(); Question.Clear(); ready = false; conversationCharacters = 0;
        changingProfile = true; Profile.SelectedIndex = 0; changingProfile = false;
        WindowsIntegration.Position(this, previousWindow);
        AnswerScroll.MaxHeight = Math.Max(80, MaxHeight - 280);
        Show(); WindowsIntegration.Focus(this); Question.Focus();
        diagnostics.Record(DiagnosticKind.HotkeyVisible, stopwatch.ElapsedMilliseconds);
        await lifecycle.WaitAsync();
        try
        {
            if (!IsVisible) return;
            Status.Text = "Preparing Help…";
            await backend.StartAsync(AgentProfile.Help, CancellationToken.None);
            await backend.ResetAsync(CancellationToken.None);
            ready = IsVisible; if (ready) Status.Text = "Ready";
        }
        catch { if (IsVisible) Status.Text = "Pi could not start. Check tray Settings and the supported Pi version."; }
        finally { lifecycle.Release(); }
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; _ = EndAsync(); }
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { Question.Clear(); Question.Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && Question.IsKeyboardFocusWithin)
        { e.Handled = true; if (ready) active = SubmitAsync(); }
    }
    private async Task SubmitAsync()
    {
        var text = Question.Text.Trim(); if (text.Length == 0 || !ready) return;
        if (conversationCharacters + text.Length > 64_000) { Status.Text = "This conversation reached its size limit. Close and reopen F1 for a fresh session."; return; }
        conversationCharacters += text.Length;
        ready = false; Profile.IsEnabled = false;
        request?.Dispose(); request = new();
        requestId = session.Begin(); Answer.Clear(); Status.Text = "Thinking…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await backend.SubmitAsync(text, context, request.Token);
            if (IsVisible && !request.IsCancellationRequested) { Question.Clear(); Status.Text = "Ready"; }
            diagnostics.Record(DiagnosticKind.RequestCompleted, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { diagnostics.Record(DiagnosticKind.Cancelled); }
        catch (Exception ex)
        {
            if (IsVisible && !request.IsCancellationRequested)
            {
                session.Apply(requestId, new("error"));
                Status.Text = ex is TimeoutException or ArgumentException or InvalidOperationException ? ex.Message : "Pi disconnected. Try again or check Settings.";
            }
            diagnostics.Record(DiagnosticKind.RequestFailed, stopwatch.ElapsedMilliseconds);
        }
        finally { ready = IsVisible; Profile.IsEnabled = true; }
    }
    private void OnAgentEvent(AgentEvent value)
    {
        var generation = requestId;
        Dispatcher.BeginInvoke(async () =>
        {
            if (!IsVisible || generation != requestId)
            {
                if (value.Interaction is { } hidden) { try { await backend.RespondAsync(hidden.Id, null, null, true); } catch { } }
                return;
            }
            if (value.Kind == "interaction" && value.Interaction is { } interaction)
            {
                var dialog = new InteractionWindow(interaction) { Owner = this };
                interactionWindow = dialog;
                dialog.ShowDialog();
                try { await backend.RespondAsync(interaction.Id, dialog.Value, dialog.Confirmed, dialog.Cancelled); }
                catch { Status.Text = "Pi no longer needs this response."; }
                interactionWindow = null; return;
            }
            if (!session.Apply(generation, value)) return;
            if (value.Kind == "text") { conversationCharacters += value.Text?.Length ?? 0; Answer.Text = session.Answer; Status.Text = "Answering…"; AnswerScroll.ScrollToEnd(); }
            if (value.Kind == "status") Status.Text = value.Text;
            if (value.Kind == "model") Status.Text = "Model: " + value.Text;
        });
    }
    public async Task EndAsync()
    {
        ready = false; session.Hide(); requestId++;
        interactionWindow?.Close(); Hide(); context = null; Question.Clear(); Answer.Clear();
        request?.Cancel(); WindowsIntegration.RestoreFocus(previousWindow);
        await lifecycle.WaitAsync();
        try { await active; await backend.CancelAsync(CancellationToken.None); }
        catch { diagnostics.Record(DiagnosticKind.ProcessFailed); }
        finally { lifecycle.Release(); }
    }
    private async void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (changingProfile || backend is null || !IsVisible || !ready) return;
        ready = false; Profile.IsEnabled = false;
        await lifecycle.WaitAsync();
        try
        {
            session.Hide(); session.Show(); Answer.Clear(); conversationCharacters = 0;
            var profile = Profile.SelectedIndex == 0 ? AgentProfile.Help : AgentProfile.Session;
            Status.Text = "Switching profile…";
            await backend.StartAsync(profile, CancellationToken.None); await backend.ResetAsync(CancellationToken.None);
            Status.Text = profile == AgentProfile.Help ? "Ready" : "Session: normal Pi tools have your user-account access.";
            ready = IsVisible;
        }
        catch { Status.Text = "Profile could not start. Check Settings."; }
        finally { Profile.IsEnabled = true; lifecycle.Release(); }
    }
    private void CloseClicked(object sender, RoutedEventArgs e) => _ = EndAsync();
    private void OnClosing(object? sender, CancelEventArgs e) { if (!permanentClose) { e.Cancel = true; _ = EndAsync(); } }
    public void ClosePermanently() { permanentClose = true; Close(); }
    public async Task StopAsync() { await EndAsync(); await backend.DisposeAsync(); }
}
