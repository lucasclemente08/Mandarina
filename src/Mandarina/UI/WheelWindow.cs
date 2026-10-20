using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Mandarina.Actions;
using Mandarina.Interop;
using Path = System.Windows.Shapes.Path;

namespace Mandarina.UI;

/// <summary>
/// The radial menu that pops up under the cursor mid-drag. It is an OLE drop target:
/// the dragged files enter it, each slice is an action, and dropping on a slice runs it.
/// </summary>
internal sealed class WheelWindow : Window
{
    private const double Size = 460;
    private const double C = Size / 2;
    private const double CenterRadius = 62;
    private static readonly (double inner, double outer, int capacity)[] Rings = [(CenterRadius, 145, 8), (145, 226, 16)];

    private static readonly Brush SliceFill = Frozen(new SolidColorBrush(Color.FromArgb(235, 32, 30, 34)));
    private static readonly Brush SliceHover = Frozen(new SolidColorBrush(Color.FromRgb(242, 133, 34)));
    private static readonly Brush SliceStroke = Frozen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
    private static readonly Brush CenterFill = Frozen(new SolidColorBrush(Color.FromArgb(245, 20, 19, 22)));
    private static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(255, 166, 77)));

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly List<(Path shape, TextBlock label, double inner, double outer, double a0, double a1)> _slices = [];
    private readonly DispatcherTimer _enterTimeout;

    private WheelMode _mode;
    private IReadOnlyList<string> _files = [];
    private IReadOnlyList<FileAction> _actions = [];
    private int _hover = -1;
    private bool _gotDrag;

    /// <summary>Raised when files are dropped on an action slice.</summary>
    public event Action<FileAction, IReadOnlyList<string>>? ActionChosen;

    public WheelWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Width = Height = Size;
        Content = new Viewbox { Child = _canvas, Stretch = Stretch.Uniform };

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        Drop += OnDrop;
        DragLeave += (_, _) => SetHover(-1);
        MouseMove += (_, e) => { if (IsClickMode) SetHover(HitTest(e.GetPosition(_canvas))); };
        MouseLeave += (_, _) => { if (IsClickMode) SetHover(-1); };
        MouseLeftButtonUp += OnClick;

        _enterTimeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _enterTimeout.Tick += (_, _) =>
        {
            // Nothing OLE-draggable reached us (e.g. a text selection drag): go away quietly.
            _enterTimeout.Stop();
            if (!_gotDrag) HideWheel();
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST);
        };
    }

    public bool IsOpen => IsVisible;

    /// <summary>True when opened from the context menu or hotkey: pick with a click instead of a drop.</summary>
    public bool IsClickMode { get; private set; }

    /// <summary>Drag mode: opens empty under the cursor and waits for the dragged files to enter.</summary>
    public void ShowAt(Native.POINT pt, WheelMode mode)
    {
        if (IsVisible)
        {
            // Already open: Shift/Alt just flips between convert and edit.
            if (mode != _mode) { _mode = mode; Rebuild(); }
            return;
        }

        Log.Write("ShowAt");
        IsClickMode = false;
        _mode = mode;
        _files = [];
        _actions = [];
        _gotDrag = false;
        Rebuild();
        PlaceAt(pt);

        // Let the window paint, then nudge the cursor so the drag loop notices us.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Native.JiggleMouse);
        _enterTimeout.Start();
    }

    /// <summary>Click mode: opens with known files (context menu, hotkey); a click on a slice runs it.</summary>
    public void ShowFor(IReadOnlyList<string> files, Native.POINT pt, WheelMode mode)
    {
        _enterTimeout.Stop();
        IsClickMode = true;
        _mode = mode;
        _files = files;
        Rebuild();
        if (!IsVisible) PlaceAt(pt);
    }

    /// <summary>Whether a screen point (physical pixels) falls inside the wheel window.</summary>
    public bool Contains(Native.POINT pt)
    {
        if (!IsVisible) return false;
        Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    private void PlaceAt(Native.POINT pt)
    {
        double scale = Native.ScaleForPoint(pt);
        int side = (int)Math.Round(Size * scale);
        var work = Native.WorkAreaForPoint(pt);
        int x = Math.Clamp(pt.X - side / 2, work.Left, work.Right - side);
        int y = Math.Clamp(pt.Y - side / 2, work.Top, work.Bottom - side);

        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, side, side, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsClickMode) return;
        e.Handled = true;
        var p = e.GetPosition(_canvas);
        double dx = p.X - C, dy = p.Y - C;
        if (dx * dx + dy * dy < CenterRadius * CenterRadius)
        {
            // The center toggles between convert and edit.
            _mode = _mode == WheelMode.Convert ? WheelMode.Edit : WheelMode.Convert;
            Rebuild();
            return;
        }
        int hit = HitTest(p);
        if (hit < 0 || hit >= _actions.Count) return;
        var action = _actions[hit];
        var files = _files;
        HideWheel();
        ActionChosen?.Invoke(action, files);
    }

    public void HideWheel()
    {
        _enterTimeout.Stop();
        _hover = -1;
        Hide();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        if (!_gotDrag) Log.Write("drag entered");
        _gotDrag = true;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (!files.SequenceEqual(_files))
        {
            _files = files;
            Rebuild();
        }

        int hit = HitTest(e.GetPosition(_canvas));
        SetHover(hit);
        // Always "copy": the original stays put even though Shift normally means "move" in Explorer.
        e.Effects = hit >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        Log.Write("drop");
        e.Handled = true;
        int hit = HitTest(e.GetPosition(_canvas));
        var files = _files;
        var action = hit >= 0 && hit < _actions.Count ? _actions[hit] : null;
        // Report "copy" so the drag source never deletes the original file.
        e.Effects = action is null ? DragDropEffects.None : DragDropEffects.Copy;
        HideWheel();
        if (action is not null) ActionChosen?.Invoke(action, files);
    }

    private int HitTest(Point p)
    {
        double dx = p.X - C, dy = p.Y - C;
        double r = Math.Sqrt(dx * dx + dy * dy);
        double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360; // 0° = up, clockwise
        for (int i = 0; i < _slices.Count; i++)
        {
            var s = _slices[i];
            if (r >= s.inner && r < s.outer && angle >= s.a0 && angle < s.a1) return i;
        }
        return -1;
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        if (_hover >= 0 && _hover < _slices.Count) _slices[_hover].shape.Fill = SliceFill;
        _hover = index;
        if (_hover >= 0 && _hover < _slices.Count) _slices[_hover].shape.Fill = SliceHover;
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _slices.Clear();
        _hover = -1;
        _actions = _files.Count == 0 ? [] : Catalog.For(_files, _mode);

        // Up to 12 actions fit in one ring; beyond that, 8 inside and the rest in an outer ring.
        (double inner, double outer, int capacity)[] rings = _actions.Count <= 12 ? [(CenterRadius, 210, 12)] : Rings;
        int remaining = _actions.Count, index = 0;
        foreach (var (inner, outer, capacity) in rings)
        {
            if (remaining == 0) break;
            int count = Math.Min(remaining, capacity);
            double step = 360.0 / count;
            for (int i = 0; i < count; i++, index++)
            {
                double a0 = i * step, a1 = a0 + step;
                AddSlice(_actions[index].Label, inner, outer, a0, a1);
            }
            remaining -= count;
        }

        AddCenter();
    }

    private void AddSlice(string text, double inner, double outer, double a0, double a1)
    {
        var shape = new Path
        {
            Data = Sector(inner, outer, a0, a1),
            Fill = SliceFill,
            Stroke = SliceStroke,
            StrokeThickness = 1,
        };
        _canvas.Children.Add(shape);

        double mid = (a0 + a1) / 2 * Math.PI / 180, rm = (inner + outer) / 2;
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = text.Length > 9 ? 12 : 14,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = Math.Min(outer - inner + 10, 2 * Math.PI * rm * (a1 - a0) / 360 - 6),
            IsHitTestVisible = false,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, C + rm * Math.Sin(mid) - label.Width / 2);
        Canvas.SetTop(label, C - rm * Math.Cos(mid) - label.DesiredSize.Height / 2);
        _canvas.Children.Add(label);

        _slices.Add((shape, label, inner, outer, a0, a1));
    }

    private void AddCenter()
    {
        var circle = new Ellipse
        {
            Width = CenterRadius * 2,
            Height = CenterRadius * 2,
            Fill = CenterFill,
            Stroke = Accent,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(circle, C - CenterRadius);
        Canvas.SetTop(circle, C - CenterRadius);
        _canvas.Children.Add(circle);

        string title = _mode == WheelMode.Convert ? "Convertir" : "Editar";
        string sub = _files.Count switch
        {
            0 => "…",
            1 => System.IO.Path.GetFileName(_files[0]),
            _ => $"{_files.Count} archivos",
        };
        if (_files.Count > 0 && _actions.Count == 0) sub = "Sin acciones para este tipo";
        string hint = IsClickMode
            ? (_mode == WheelMode.Convert ? "Clic aquí: editar" : "Clic aquí: convertir")
            : (_mode == WheelMode.Convert ? "Alt: editar" : "Mayús: convertir");

        var stack = new StackPanel { Width = CenterRadius * 2 - 14, IsHitTestVisible = false };
        stack.Children.Add(new TextBlock { Text = title, Foreground = Accent, FontSize = 15, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
        stack.Children.Add(new TextBlock { Text = sub, Foreground = Brushes.White, FontSize = 10.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 30 });
        stack.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 9.5, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(stack, C - stack.Width / 2);
        Canvas.SetTop(stack, C - stack.DesiredSize.Height / 2);
        _canvas.Children.Add(stack);
    }

    /// <summary>Draws the wheel for the given files into a PNG (used by --preview).</summary>
    public void RenderPreview(IReadOnlyList<string> files, WheelMode mode, int hover, string output)
    {
        _files = files;
        _mode = mode;
        Rebuild();
        SetHover(hover);
        _canvas.Measure(new Size(Size, Size));
        _canvas.Arrange(new Rect(0, 0, Size, Size));
        _canvas.UpdateLayout();
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Size * 2, (int)Size * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(_canvas);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(output);
        enc.Save(fs);
    }

    private static Geometry Sector(double r0, double r1, double a0, double a1)
    {
        const double gap = 0.6; // degrees between slices
        a0 += gap / 2; a1 -= gap / 2;
        Point P(double r, double a) => new(C + r * Math.Sin(a * Math.PI / 180), C - r * Math.Cos(a * Math.PI / 180));
        bool large = a1 - a0 > 180;

        if (a1 - a0 >= 359)
        {
            // A single action: full ring.
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(C, C), r1, r1), new EllipseGeometry(new Point(C, C), r0, r0));
            ring.Freeze();
            return ring;
        }

        var fig = new PathFigure { StartPoint = P(r0, a0), IsClosed = true };
        fig.Segments.Add(new LineSegment(P(r1, a0), true));
        fig.Segments.Add(new ArcSegment(P(r1, a1), new Size(r1, r1), 0, large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(r0, a1), true));
        fig.Segments.Add(new ArcSegment(P(r0, a0), new Size(r0, r0), 0, large, SweepDirection.Counterclockwise, true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return geo;
    }

    private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }
}
