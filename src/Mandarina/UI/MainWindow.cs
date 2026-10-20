using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Mandarina.Actions;
using Mandarina.Interop;

namespace Mandarina.UI;

/// <summary>
/// The no-tricks way in: drop files (or click to pick them) and press a big button.
/// Closing it only hides it; Mandarina keeps running in the tray.
/// </summary>
internal sealed class MainWindow : Window
{
    private static readonly Brush Page = Frozen(Color.FromRgb(250, 247, 243));
    private static readonly Brush Card = Brushes.White;
    private static readonly Brush Ink = Frozen(Color.FromRgb(34, 31, 28));
    private static readonly Brush Muted = Frozen(Color.FromRgb(112, 104, 96));
    private static readonly Brush Line = Frozen(Color.FromRgb(226, 218, 208));
    private static readonly Brush Orange = Frozen(Color.FromRgb(234, 112, 22));
    private static readonly Brush OrangeSoft = Frozen(Color.FromRgb(255, 240, 225));
    private static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");

    private readonly Func<FileAction, IReadOnlyList<string>, Task<IReadOnlyList<string>>> _run;
    private readonly Border _dropZone;
    private readonly Rectangle _dropOutline;
    private readonly TextBlock _dropTitle;
    private readonly TextBlock _dropHint;
    private readonly StackPanel _actions = new();
    private readonly Border _status;
    private readonly TextBlock _statusText = new() { FontFamily = Ui, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Width = 90, Height = 4, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _openFolder;
    private IReadOnlyList<string> _files = [];
    private string? _lastOutput;

    public MainWindow(Func<FileAction, IReadOnlyList<string>, Task<IReadOnlyList<string>>> run)
    {
        _run = run;
        Title = "Mandarina";
        Width = 620;
        Height = 680;
        MinWidth = 440;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Page;
        FontFamily = Ui;
        Foreground = Ink;
        AllowDrop = true;
        Icon = AppIcon();

        var root = new Grid { Margin = new Thickness(24, 20, 24, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new Image { Source = Icon, Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Mandarina", FontSize = 24, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Convertí y editá archivos en un clic", FontSize = 13.5, Foreground = Muted });
        header.Children.Add(titles);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Drop zone
        _dropOutline = new Rectangle { Stroke = Line, StrokeThickness = 2, StrokeDashArray = [5, 4], RadiusX = 14, RadiusY = 14 };
        _dropTitle = new TextBlock { Text = "Soltá tus archivos acá", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _dropHint = new TextBlock { Text = "o hacé clic para elegirlos", FontSize = 13.5, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center };
        var dropContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
        dropContent.Children.Add(new TextBlock { Text = "", FontFamily = ActionStyle.IconFont, FontSize = 30, Foreground = Orange, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        dropContent.Children.Add(_dropTitle);
        dropContent.Children.Add(_dropHint);
        var dropGrid = new Grid();
        dropGrid.Children.Add(_dropOutline);
        dropGrid.Children.Add(dropContent);
        _dropZone = new Border
        {
            Background = Card,
            CornerRadius = new CornerRadius(14),
            Height = 150,
            Cursor = Cursors.Hand,
            Child = dropGrid,
            Margin = new Thickness(0, 0, 0, 16),
            Focusable = true,
        };
        _dropZone.MouseLeftButtonUp += (_, _) => PickFiles();
        _dropZone.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) PickFiles(); };
        AutomationProperties("Elegir archivos", _dropZone);
        Grid.SetRow(_dropZone, 1);
        root.Children.Add(_dropZone);

        // Actions
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _actions };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        ShowEmptyHint();

        // Status bar
        _openFolder = new Button
        {
            Content = "Abrir carpeta",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(12, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
        };
        _openFolder.Click += (_, _) => Reveal(_lastOutput);
        var statusRow = new DockPanel();
        DockPanel.SetDock(_openFolder, Dock.Right);
        DockPanel.SetDock(_progress, Dock.Right);
        statusRow.Children.Add(_openFolder);
        statusRow.Children.Add(_progress);
        statusRow.Children.Add(_statusText);
        _status = new Border
        {
            Background = Card,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 12, 0, 0),
            Child = statusRow,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);

        // Footer with the shortcuts
        var footer = new TextBlock
        {
            Text = "Más rápido: arrastrá un archivo y tocá Mayús  ·  Ctrl+Mayús+M  ·  clic derecho → Convertir con Mandarina",
            FontSize = 12,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        Content = root;

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => Highlight(false);
        Drop += (_, e) =>
        {
            Highlight(false);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) SetFiles(files);
        };
        Closing += (_, e) =>
        {
            // Keep running in the tray.
            e.Cancel = true;
            Hide();
        };
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // bring to front even when another app had focus
        Topmost = false;
    }

    public void SetFiles(IReadOnlyList<string> files)
    {
        _files = files.Where(File.Exists).ToList();
        _status.Visibility = Visibility.Collapsed;
        if (_files.Count == 0) { ShowEmptyHint(); return; }

        _dropTitle.Text = _files.Count == 1 ? System.IO.Path.GetFileName(_files[0]) : $"{_files.Count} archivos";
        _dropHint.Text = "Soltá otros o hacé clic para cambiarlos";
        _actions.Children.Clear();

        var all = Catalog.For(_files);
        if (all.Count == 0)
        {
            _actions.Children.Add(Note("No hay opciones para este tipo de archivo" +
                (_files.Select(FileKinds.Ext).Distinct().Count() > 1 ? " (probá con archivos del mismo tipo)." : ".")));
            return;
        }
        AddSection("Convertir a", all.Where(a => a.Mode == WheelMode.Convert));
        AddSection("Editar", all.Where(a => a.Mode == WheelMode.Edit));
    }

    private void ShowEmptyHint()
    {
        _dropTitle.Text = "Soltá tus archivos acá";
        _dropHint.Text = "o hacé clic para elegirlos";
        _actions.Children.Clear();
        _actions.Children.Add(Note("Imágenes, videos, audio, PDF, Word, Excel, PowerPoint y texto (.txt)."));
    }

    private void AddSection(string title, IEnumerable<FileAction> actions)
    {
        var list = actions.ToList();
        if (list.Count == 0) return;
        _actions.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 4, 0, 8) });
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var a in list) wrap.Children.Add(ActionButton(a));
        _actions.Children.Add(wrap);
    }

    private Button ActionButton(FileAction action)
    {
        var (glyph, color) = ActionStyle.For(action);
        var brush = Frozen(color);
        var tint = Frozen(Color.FromArgb(28, color.R, color.G, color.B));

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = ActionStyle.IconFont, FontSize = 24, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        content.Children.Add(new TextBlock { Text = action.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });

        var face = new Border
        {
            Background = Card,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Child = content,
        };
        var button = new Button
        {
            Width = 104,
            Height = 86,
            Margin = new Thickness(0, 0, 10, 10),
            Cursor = Cursors.Hand,
            ToolTip = action.Mode == WheelMode.Convert ? $"Convertir a {action.Label}" : action.Label,
            Template = FaceTemplate(),
            Content = face,
        };
        button.MouseEnter += (_, _) => { face.Background = tint; face.BorderBrush = brush; };
        button.MouseLeave += (_, _) => { face.Background = Card; face.BorderBrush = Line; };
        button.GotKeyboardFocus += (_, _) => face.BorderBrush = brush;
        button.LostKeyboardFocus += (_, _) => face.BorderBrush = Line;
        button.Click += async (_, _) => await Run(action);
        return button;
    }

    private async Task Run(FileAction action)
    {
        var files = _files;
        _status.Visibility = Visibility.Visible;
        _progress.Visibility = Visibility.Visible;
        _openFolder.Visibility = Visibility.Collapsed;
        _statusText.Foreground = Ink;
        _statusText.Text = $"{action.Label}: trabajando…";
        try
        {
            var outputs = await _run(action, files);
            _lastOutput = outputs.FirstOrDefault();
            _statusText.Text = "✓ Listo: " + string.Join(", ", outputs.Select(System.IO.Path.GetFileName).Take(3)) + (outputs.Count > 3 ? "…" : "");
            _openFolder.Visibility = _lastOutput is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            _statusText.Foreground = Frozen(ActionStyle.Document);
            _statusText.Text = "No se pudo: " + ex.Message;
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Elegí los archivos" };
        if (dialog.ShowDialog(this) == true) SetFiles(dialog.FileNames);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        Highlight(files);
        e.Handled = true;
    }

    private void Highlight(bool on)
    {
        _dropOutline.Stroke = on ? Orange : Line;
        _dropZone.Background = on ? OrangeSoft : Card;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 13.5,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 12, 0, 0),
    };

    /// <summary>A button template that just shows its content (the card), without the default chrome.</summary>
    private static ControlTemplate FaceTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }

    private static void AutomationProperties(string name, UIElement element) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    private static void Reveal(string? path)
    {
        if (path is null) return;
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }

    /// <summary>The largest frame of the embedded mandarina.ico, so it stays sharp when scaled.</summary>
    public static BitmapSource? AppIcon()
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/mandarina.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
