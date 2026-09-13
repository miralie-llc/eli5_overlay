using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using F1.App;
using F1.Core;

static class UiRender
{
    public static void Run(string destination)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App(); app.InitializeComponent();
                var window = new OverlayWindow(new Settings(), Path.GetTempPath(), new IdleBackend());
                ((TextBox)window.FindName("Question")).Text = "What is adamantium?";
                ((TextBlock)window.FindName("ContextLabel")).Text = "Context: example-game — Example game window";
                ((TextBlock)window.FindName("Status")).Text = "Ready";
                ((TextBox)window.FindName("Answer")).Text = "Adamantium is a fictional, nearly indestructible metal in Marvel stories. Wolverine’s claws and skeleton are coated with it. Think of it as the story’s version of a metal that almost nothing can break.";
                var content = (Border)window.Content;
                content.Background = window.Background;
                content.Measure(new Size(720, 560)); content.Arrange(new Rect(0, 0, 720, content.DesiredSize.Height)); content.UpdateLayout();
                if (content.ActualHeight < 200 || content.ActualHeight > 560) throw new Exception("Unexpected overlay layout size");
                var bitmap = new RenderTargetBitmap(720, (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
                using var output = File.Create(destination); encoder.Save(output);
                window.ClosePermanently(); app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("WPF render failed", failure);
        Console.WriteLine("PASS WPF overlay layout render");
    }
    private sealed class IdleBackend : IAgentBackend
    {
        public event Action<AgentEvent>? EventReceived { add { } remove { } }
        public Task StartAsync(AgentProfile profile, CancellationToken token) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken token) => Task.CompletedTask;
        public Task SubmitAsync(string text, ForegroundContext? context, CancellationToken token) => Task.CompletedTask;
        public Task CancelAsync(CancellationToken token) => Task.CompletedTask;
        public Task RespondAsync(string id, string? value, bool? confirmed, bool cancelled) => Task.CompletedTask;
        public Task<IReadOnlyList<Capability>> InspectAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<Capability>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
