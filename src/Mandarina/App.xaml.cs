using System.Runtime.InteropServices;
using System.Windows;
using Mandarina.Actions;
using Mandarina.Interop;
using Mandarina.UI;

namespace Mandarina;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private DragWatcher? _watcher;
    private WheelWindow? _wheel;
    private Tray? _tray;
    private Hotkey? _hotkey;
    private MainWindow? _main;
    private readonly List<string> _pendingOpen = [];
    private System.Windows.Threading.DispatcherTimer? _openDebounce;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        switch (args.FirstOrDefault())
        {
            case "--install":
                Installer.Install();
                Installer.LaunchInstalled();
                Shutdown();
                return;
            case "--uninstall":
                Installer.Uninstall();
                MessageBox.Show("Mandarina se desinstaló.", "Mandarina", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            case "--open":
            case "--window":
            case "--background":
                break;
            case { } flag when flag.StartsWith("--"):
                Shutdown(await Cli.RunAsync(args));
                return;
        }

        var toOpen = args.FirstOrDefault() == "--open" ? args[1..] : [];
        var toWindow = args.FirstOrDefault() == "--window" ? args[1..] : [];

        _singleInstance = new Mutex(true, @"Local\Mandarina", out bool first);
        if (!first)
        {
            // Already running in the tray: hand over the files, or bring up the window.
            if (toOpen.Length > 0) InstancePipe.TrySend("open", toOpen);
            else if (args.Length == 0 || toWindow.Length > 0) InstancePipe.TrySend("show", toWindow);
            Shutdown();
            return;
        }

        if (args.Length == 0 && !Installer.IsRunningInstalled && OfferInstall())
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Installer.Install();
            Installer.LaunchInstalled();
            Shutdown();
            return;
        }

        _tray = new Tray();
        _tray.ExitRequested += () => Shutdown();
        _tray.ShowRequested += ShowMain;
        _tray.ClassicMenuRequested += SetClassicMenu;
        _tray.UninstallRequested += () =>
        {
            if (MessageBox.Show("¿Desinstalar Mandarina? Se quita del menú contextual, del inicio y del menú Inicio.",
                    "Mandarina", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _singleInstance?.Dispose();
            _singleInstance = null;
            Installer.Uninstall();
            Shutdown();
        };

        _wheel = new WheelWindow();
        _wheel.ActionChosen += (action, files) => _ = RunAction(action, files).ContinueWith(_ => { }, TaskScheduler.Default);

        _watcher = new DragWatcher(Dispatcher);
        _watcher.Triggered += (mode, pt) => { if (!_wheel.IsClickMode || !_wheel.IsOpen) _wheel.ShowAt(pt, mode); };
        _watcher.MouseReleased += () =>
        {
            // A drop on the wheel hides it itself; this covers releasing anywhere else.
            if (_wheel.IsClickMode) return;
            Dispatcher.BeginInvoke(() => { if (_wheel.IsOpen && !_wheel.IsClickMode) _wheel.HideWheel(); },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        _watcher.MousePressed += pt =>
        {
            // Click mode: clicking anywhere outside the wheel closes it.
            if (_wheel.IsOpen && _wheel.IsClickMode && !_wheel.Contains(pt)) _wheel.HideWheel();
        };
        _watcher.Escape += () => { if (_wheel.IsOpen) _wheel.HideWheel(); };
        _tray.EnabledChanged += on => _watcher.Enabled = on;

        try
        {
            _watcher.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Mandarina", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _hotkey = new Hotkey(Native.MOD_CONTROL | Native.MOD_SHIFT, 'M');
        _hotkey.Pressed += OpenExplorerSelection;

        // Explorer launches one process per selected file; collect them for a moment and open one wheel.
        _openDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _openDebounce.Tick += (_, _) =>
        {
            _openDebounce.Stop();
            var files = _pendingOpen.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _pendingOpen.Clear();
            OpenWheel(files);
        };
        InstancePipe.Serve((command, paths) => Dispatcher.BeginInvoke(() =>
        {
            switch (command)
            {
                case "open": QueueOpen(paths); break;
                case "help": _tray.ShowHelp(); break;
                case "show": ShowMain(paths); break;
                case "quit": Shutdown(); break;
            }
        }));

        if (toOpen.Length > 0) QueueOpen(toOpen);
        else if (args.Length == 0 || toWindow.Length > 0) ShowMain(toWindow);
        else _tray.Notify("Mandarina está activa",
            "Arrastrá un archivo y tocá Mayús. O seleccionalo y apretá Ctrl+Mayús+M, o clic derecho → Convertir con Mandarina." +
            (_hotkey.IsRegistered ? "" : " (Ctrl+Mayús+M ya lo usa otro programa.)"));
    }

    private static bool OfferInstall() =>
        MessageBox.Show(
            (Installer.IsInstalled ? "¿Actualizar la Mandarina instalada con esta versión?" : "¿Instalar Mandarina?") +
            "\n\n• Clic derecho en un archivo → \"Convertir con Mandarina\"\n• Atajo Ctrl+Mayús+M con archivos seleccionados\n" +
            "• Arranca sola con Windows y aparece en el menú Inicio\n\nNo necesita permisos de administrador. Con \"No\" se abre sin instalar.",
            "Mandarina", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void QueueOpen(IEnumerable<string> paths)
    {
        _pendingOpen.AddRange(paths);
        _openDebounce!.Stop();
        _openDebounce.Start();
    }

    private void OpenExplorerSelection()
    {
        IReadOnlyList<string> files;
        try { files = ExplorerSelection.Get(); }
        catch (Exception) { files = []; }
        if (files.Count == 0)
        {
            _tray!.Notify("Nada seleccionado", "Seleccioná uno o más archivos en el Explorador o el escritorio y apretá Ctrl+Mayús+M.");
            return;
        }
        OpenWheel(files);
    }

    private void OpenWheel(IReadOnlyList<string> files)
    {
        if (files.Count == 0 || !_watcher!.Enabled) return;
        Native.GetCursorPos(out var pt);
        _wheel!.ShowFor(files, pt, WheelMode.Convert);
    }

    private void ShowMain() => ShowMain([]);

    private void ShowMain(IReadOnlyList<string> files)
    {
        _main ??= new MainWindow(RunAction);
        if (files.Count > 0) _main.SetFiles(files);
        _main.ShowAndActivate();
    }

    private void SetClassicMenu(bool on)
    {
        var question = on
            ? "Esto vuelve al menú de clic derecho completo de Windows (el de Windows 10) en todo el sistema, " +
              "así \"Convertir con Mandarina\" aparece directo sin \"Mostrar más opciones\".\n\n" +
              "Para aplicarlo hay que reiniciar el Explorador: se cierran las ventanas de carpetas abiertas.\n\n¿Continuar?"
            : "Vuelve al menú corto de Windows 11. Hay que reiniciar el Explorador (se cierran las ventanas de carpetas).\n\n¿Continuar?";
        if (MessageBox.Show(question, "Mandarina", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ClassicMenu.Set(on);
        ClassicMenu.RestartExplorer();
    }

    /// <summary>Runs an action, reporting progress in the tray; rethrows so the window can show errors too.</summary>
    private async Task<IReadOnlyList<string>> RunAction(FileAction action, IReadOnlyList<string> files)
    {
        var what = files.Count == 1 ? $"{action.Label} ← {Path.GetFileName(files[0])}" : $"{action.Label} ({files.Count} archivos)";
        // The window shows its own status; only use balloons when it is not in front.
        bool quiet = _main is { IsActive: true };
        _tray!.Started(what);
        try
        {
            var outputs = await Task.Run(() => action.Run(files, CancellationToken.None));
            _tray.Finished(outputs, balloon: !quiet);
            return outputs;
        }
        catch (Exception ex)
        {
            _tray.Failed(ex.Message, balloon: !quiet);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _watcher?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>
/// Command-line mode, handy for scripting and tests:
///   Mandarina.exe --list  file...            lists the actions for those files
///   Mandarina.exe --run   action-id file...  runs one and prints the outputs
/// </summary>
internal static class Cli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    public static async Task<int> RunAsync(string[] args)
    {
        AttachConsole(-1);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            switch (args[0])
            {
                case "--list":
                    foreach (var a in Catalog.For(args[1..]))
                        Console.WriteLine($"{a.Mode,-8} {a.Id,-20} {a.Label}");
                    return 0;
                case "--preview": // --preview convert|edit out.png file...
                    new UI.WheelWindow().RenderPreview(args[3..], args[1] == "edit" ? WheelMode.Edit : WheelMode.Convert, 1, args[2]);
                    return 0;
                case "--run":
                    var files = args[2..];
                    var action = Catalog.For(files).FirstOrDefault(a => a.Id == args[1]);
                    if (action is null)
                    {
                        Console.Error.WriteLine($"La acción '{args[1]}' no aplica a esos archivos.");
                        return 2;
                    }
                    foreach (var o in await Task.Run(() => action.Run(files, CancellationToken.None)))
                        Console.WriteLine(o);
                    return 0;
                default:
                    Console.Error.WriteLine("Uso: Mandarina --list <archivos> | --run <acción> <archivos>");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
