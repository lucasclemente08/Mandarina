using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Mandarina.UI;

/// <summary>Notification-area icon: menu, progress tooltip and completion balloons.</summary>
internal sealed class Tray : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _enabledItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private string? _revealOnClick;
    private int _busy;

    public event Action<bool>? EnabledChanged;
    public event Action? ExitRequested;
    public event Action? UninstallRequested;
    public event Action? ShowRequested;
    public event Action<bool>? ClassicMenuRequested;

    public Tray()
    {
        _enabledItem = new Forms.ToolStripMenuItem("Activo") { Checked = true, CheckOnClick = true };
        _enabledItem.CheckedChanged += (_, _) => { EnabledChanged?.Invoke(_enabledItem.Checked); UpdateText(); };

        _startupItem = new Forms.ToolStripMenuItem("Iniciar con Windows") { Checked = StartsWithWindows, CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => StartsWithWindows = _startupItem.Checked;

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Abrir Mandarina", null, (_, _) => ShowRequested?.Invoke());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_startupItem);
        if (Mandarina.Interop.ClassicMenu.IsWindows11)
        {
            var classic = new Forms.ToolStripMenuItem("Mandarina directo en el clic derecho") { Checked = Mandarina.Interop.ClassicMenu.IsEnabled };
            classic.Click += (_, _) => ClassicMenuRequested?.Invoke(!classic.Checked);
            menu.Opening += (_, _) => classic.Checked = Mandarina.Interop.ClassicMenu.IsEnabled;
            menu.Items.Add(classic);
        }
        menu.Items.Add("Cómo se usa", null, (_, _) => ShowHelp());
        menu.Items.Add("Desinstalar…", null, (_, _) => UninstallRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = DrawIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.BalloonTipClicked += (_, _) => Reveal(_revealOnClick);
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke(); };
        UpdateText();
    }

    public void ShowHelp() => Balloon("Cómo se usa",
        "• Arrastrá un archivo y tocá Mayús (convertir) o Alt (editar); soltalo sobre una opción.\n" +
        "• O seleccioná archivos y apretá Ctrl+Mayús+M, o clic derecho → Convertir con Mandarina; elegí con un clic.\n" +
        "Esc cancela.", Forms.ToolTipIcon.Info);

    public void Started(string what)
    {
        _busy++;
        UpdateText(what);
    }

    public void Finished(IReadOnlyList<string> outputs, bool balloon = true)
    {
        _busy = Math.Max(0, _busy - 1);
        UpdateText();
        if (!balloon) return;
        var names = string.Join(", ", outputs.Select(Path.GetFileName).Take(3)) + (outputs.Count > 3 ? "…" : "");
        Balloon("Listo", names, Forms.ToolTipIcon.Info, outputs.FirstOrDefault());
    }

    public void Failed(string error, bool balloon = true)
    {
        _busy = Math.Max(0, _busy - 1);
        UpdateText();
        if (!balloon) return;
        Balloon("No se pudo completar", error, Forms.ToolTipIcon.Error);
    }

    public void Notify(string title, string text) => Balloon(title, text, Forms.ToolTipIcon.Info);

    private void Balloon(string title, string text, Forms.ToolTipIcon kind, string? reveal = null)
    {
        _revealOnClick = reveal;
        _icon.ShowBalloonTip(4000, title, text.Length > 250 ? text[..250] + "…" : text, kind);
    }

    private void UpdateText(string? what = null)
    {
        var text = !_enabledItem.Checked ? "Mandarina (pausado)"
            : _busy > 0 ? $"Mandarina: {what ?? "trabajando…"}"
            : "Mandarina: arrastrá + Mayús, o Ctrl+Mayús+M";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static void Reveal(string? path)
    {
        if (path is null) return;
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
    }

    private static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Mandarina") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Mandarina", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("Mandarina", throwOnMissingValue: false);
        }
    }

    /// <summary>The exe's own icon (a tangerine), falling back to a drawn one.</summary>
    private static Icon DrawIcon()
    {
        try
        {
            var own = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            if (own is not null) return new Icon(own, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception) { }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fruit = new SolidBrush(Color.FromArgb(240, 130, 30));
            g.FillEllipse(fruit, 3, 7, 26, 24);
            using var leaf = new SolidBrush(Color.FromArgb(76, 160, 70));
            g.FillEllipse(leaf, 15, 1, 11, 7);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
