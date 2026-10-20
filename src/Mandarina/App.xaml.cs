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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0].StartsWith("--"))
        {
            Shutdown(await Cli.RunAsync(e.Args));
            return;
        }

        _singleInstance = new Mutex(true, @"Local\Mandarina", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        _tray = new Tray();
        _tray.ExitRequested += () => Shutdown();

        _wheel = new WheelWindow();
        _wheel.ActionChosen += (action, files) => _ = RunAction(action, files);

        _watcher = new DragWatcher(Dispatcher);
        _watcher.Triggered += (mode, pt) => _wheel.ShowAt(pt, mode);
        _watcher.MouseReleased += () =>
        {
            // A drop on the wheel hides it itself; this covers releasing anywhere else.
            Dispatcher.BeginInvoke(() => { if (_wheel.IsOpen) _wheel.HideWheel(); },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

        _tray.Notify("Mandarina está activa", "Arrastrá un archivo y tocá Mayús para convertirlo, o Alt para editarlo.");
    }

    private async Task RunAction(FileAction action, IReadOnlyList<string> files)
    {
        var what = files.Count == 1 ? $"{action.Label} ← {Path.GetFileName(files[0])}" : $"{action.Label} ({files.Count} archivos)";
        _tray!.Started(what);
        try
        {
            var outputs = await Task.Run(() => action.Run(files, CancellationToken.None));
            _tray.Finished(outputs);
        }
        catch (Exception ex)
        {
            _tray.Failed(ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
