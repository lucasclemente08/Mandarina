using System.IO.Pipes;
using System.Text;

namespace Mandarina.Interop;

/// <summary>
/// Lets later launches (e.g. Explorer's context menu, which starts one process per selected file)
/// hand their files to the instance already running in the tray.
/// Protocol: one UTF-8 line with the command ("open" or "quit"), then one path per line.
/// </summary>
internal static class InstancePipe
{
    private static string Name => $"Mandarina-{Environment.UserName}";

    public static bool TrySend(string command, IEnumerable<string> paths)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            w.WriteLine(command);
            foreach (var p in paths) w.WriteLine(p);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            return false;
        }
    }

    /// <summary>Serves requests forever on a background thread; the callback runs on that thread.</summary>
    public static void Serve(Action<string, IReadOnlyList<string>> onMessage)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var r = new StreamReader(server, Encoding.UTF8);
                    var command = r.ReadLine() ?? "";
                    var paths = new List<string>();
                    while (r.ReadLine() is { } line)
                        if (line.Length > 0) paths.Add(line);
                    onMessage(command, paths);
                }
                catch (IOException) { }
            }
        }) { IsBackground = true, Name = "Mandarina pipe" };
        t.Start();
    }
}
