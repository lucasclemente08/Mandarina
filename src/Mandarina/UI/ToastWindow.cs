using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Mandarina.Interop;

namespace Mandarina.UI;

/// <summary>
/// Mandarina's own small notice in the bottom-right corner ("Convirtiendo…", "✓ Listo · Abrir carpeta").
/// Unlike Windows notifications it still shows with Focus/Do-not-disturb on.
/// </summary>
internal sealed class ToastWindow : Window
{
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _detail = new() { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(214, 208, 200)), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _open;
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(7) };
    private string? _reveal;

    public ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        _open = new Button { Content = "Abrir carpeta", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        _open.Click += (_, _) => { Reveal(_reveal); Hide(); };

        var text = new StackPanel { Width = 260, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_detail);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image { Source = MainWindow.AppIcon(), Width = 28, Height = 28, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(text);
        row.Children.Add(_open);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 34, 31, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(242, 133, 34)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(8),
            Child = row,
        };
        MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button) Hide(); };
        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
    }

    public void Working(string what) => Present("Mandarina", what + "…", reveal: null, autoHide: false);

    public void Done(IReadOnlyList<string> outputs) =>
        Present("✓ Listo", string.Join(", ", outputs.Select(System.IO.Path.GetFileName).Take(2)) + (outputs.Count > 2 ? "…" : ""), outputs.FirstOrDefault(), autoHide: true);

    public void Failed(string error) => Present("No se pudo", error, reveal: null, autoHide: true);

    private void Present(string title, string detail, string? reveal, bool autoHide)
    {
        _title.Text = title;
        _detail.Text = detail;
        _detail.ToolTip = detail;
        _reveal = reveal;
        _open.Visibility = reveal is null ? Visibility.Collapsed : Visibility.Visible;
        Show();
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 8;
        Top = work.Bottom - ActualHeight - 8;
        _hide.Stop();
        if (autoHide) _hide.Start();
    }

    private static void Reveal(string? path)
    {
        if (path is null) return;
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }
}
