using System.IO;
using System.IO.Pipes;
using System.Windows;
using F1.Core;
using F1.Infrastructure;
using Forms = System.Windows.Forms;

namespace F1.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? startupItem;
    private WindowsIntegration? integration;
    private OverlayWindow? overlay;
    private readonly SettingsStore store = new();
    private Settings settings = new();
    private readonly CancellationTokenSource shutdown = new();
    private string PipeName => "Miralie.F1." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value.Replace('-', '_');
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, exception) =>
        {
            exception.Handled = true;
            new Diagnostics(store.DirectoryPath).Record(DiagnosticKind.UnexpectedError);
            MessageBox.Show("F1 encountered an unexpected error. Close the overlay and try again.", "F1");
        };
        instance = new Mutex(true, @"Local\" + PipeName, out ownsInstance);
        if (!ownsInstance)
        {
            try { using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous); await client.ConnectAsync(1500); await client.WriteAsync(new byte[] { 1 }); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { }
            Shutdown(); return;
        }
        string? startupError = null;
        try { settings = store.Load(); }
        catch { startupError = "Settings could not be loaded. Open Settings to correct them."; }
        integration = new();
        integration.Invoked += Open;
        try { integration.Register(settings.Hotkey); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { startupError = ex.Message; }
        overlay = new OverlayWindow(settings, store.DirectoryPath);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Open());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = settings.StartWithWindows, CheckOnClick = true };
        startupItem = startup;
        startup.Click += (_, _) =>
        {
            try { WindowsIntegration.SetStartup(startup.Checked); settings.StartWithWindows = startup.Checked; store.Save(settings); }
            catch { startup.Checked = !startup.Checked; MessageBox.Show("Could not change startup settings.", "F1"); }
        };
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, async (_, _) => { if (overlay is not null) await overlay.StopAsync(); Shutdown(); });
        tray = new Forms.NotifyIcon { Text = "F1 — Help", Icon = System.Drawing.SystemIcons.Question, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Open();
        _ = ListenAsync(shutdown.Token);
        if (startupError is not null) { tray.BalloonTipTitle = "F1 needs attention"; tray.BalloonTipText = startupError; tray.ShowBalloonTip(5000); }
        if (e.Args.Contains("--open")) Open();
        await overlay.WarmAsync();
    }
    private async Task ListenAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                var data = new byte[1];
                if (await server.ReadAsync(data, token) > 0) await Dispatcher.InvokeAsync(Open);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { new Diagnostics(store.DirectoryPath).Record(DiagnosticKind.UnexpectedError); }
    }
    private void Open() => overlay?.Invoke();
    private async void OpenSettings()
    {
        if (overlay is null || integration is null) return;
        await overlay.EndAsync();
        var window = new SettingsWindow(settings, store.DirectoryPath);
        if (window.ShowDialog() != true) return;
        try
        {
            integration.Register(window.Result.Hotkey);
            WindowsIntegration.SetStartup(window.Result.StartWithWindows);
            store.Save(window.Result);
            settings = window.Result;
            if (startupItem is not null) startupItem.Checked = settings.StartWithWindows;
            await overlay.StopAsync(); overlay.ClosePermanently();
            overlay = new OverlayWindow(settings, store.DirectoryPath);
            await overlay.WarmAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            try { integration.Register(settings.Hotkey); } catch { }
            MessageBox.Show("Settings could not be applied. Check the shortcut and file access.", "F1");
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        shutdown.Cancel(); tray?.Dispose(); integration?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose(); base.OnExit(e);
    }
}
