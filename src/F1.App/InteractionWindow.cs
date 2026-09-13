using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using F1.Core;

namespace F1.App;

internal sealed class InteractionWindow : Window
{
    public string? Value { get; private set; }
    public bool? Confirmed { get; private set; }
    public bool Cancelled { get; private set; } = true;
    public InteractionWindow(Interaction request)
    {
        Title = "Pi needs input"; Width = 480; SizeToContent = SizeToContent.Height; MaxHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = request.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (request.Message is not null) panel.Children.Add(new TextBlock { Text = request.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var input = new TextBox { Text = request.Value ?? "", AcceptsReturn = request.Method == "editor", MaxHeight = 250, TextWrapping = TextWrapping.Wrap };
        var options = new ComboBox { ItemsSource = request.Options, SelectedIndex = request.Options.Length > 0 ? 0 : -1 };
        if (request.Method == "select") panel.Children.Add(options);
        if (request.Method is "input" or "editor") panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; cancel.Click += (_, _) => Close();
        var accept = new Button { Content = request.Method == "confirm" ? "Allow" : "OK", IsDefault = request.Method != "editor" };
        accept.Click += (_, _) => { Value = request.Method == "select" ? options.SelectedItem as string : input.Text; Confirmed = request.Method == "confirm" ? true : null; Cancelled = false; Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(accept); panel.Children.Add(buttons);
        if (request.TimeoutMilliseconds is > 0)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(request.TimeoutMilliseconds.Value) };
            timer.Tick += (_, _) => Close(); Closed += (_, _) => timer.Stop(); Loaded += (_, _) => timer.Start();
        }
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); if (Owner is OverlayWindow overlay) _ = overlay.EndAsync(); } };
    }
}
