using Mandarina.Interop;

namespace Mandarina.Actions;

public static class MediaActions
{
    // H.264 needs even dimensions; this rounds down without changing the aspect ratio noticeably.
    private const string EvenSize = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    private static readonly string[] VideoTargets = ["mp4", "mkv", "webm", "mov", "avi", "gif"];
    private static readonly string[] AudioTargets = ["mp3", "wav", "m4a", "ogg", "flac", "opus"];

    public static IEnumerable<FileAction> For(IReadOnlyList<string> files)
    {
        bool video = files.All(f => FileKinds.Is(f, FileKinds.Video));
        bool audio = files.All(f => FileKinds.Is(f, FileKinds.Audio));
        if (!video && !audio) yield break;

        var exts = files.Select(FileKinds.Ext).ToHashSet();
        var targets = video ? VideoTargets.Concat(AudioTargets) : AudioTargets;
        foreach (var target in targets)
        {
            if (exts.Count == 1 && exts.Contains(target)) continue;
            var t = target;
            yield return new FileAction($"media.to.{t}", t.ToUpperInvariant(), WheelMode.Convert, (fs, ct) => Convert(fs, t, ct));
        }

        if (video)
        {
            yield return Edit("video.compress", "Comprimir", "comprimido", "mp4",
                ["-c:v", "libx264", "-preset", "medium", "-crf", "28", "-vf", EvenSize, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]);
            yield return Edit("video.720", "720p", "720p", "mp4",
                ["-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-vf", "scale=-2:'min(720,ih)'", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart"]);
            yield return Edit("video.480", "480p", "480p", "mp4",
                ["-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-vf", "scale=-2:'min(480,ih)'", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]);
            yield return Edit("video.mute", "Sin audio", "sin audio", null, ["-c:v", "copy", "-an"]);
            yield return Edit("video.rotr", "Rotar ↻", "rotado", "mp4",
                ["-vf", "transpose=1", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "copy"]);
            yield return Edit("video.fast", "Velocidad ×2", "x2", "mp4",
                ["-filter_complex", "[0:v]setpts=0.5*PTS," + EvenSize + "[v];[0:a]atempo=2.0[a]", "-map", "[v]", "-map", "[a]?",
                 "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac"]);
            yield return new FileAction("video.frame", "Fotograma PNG", WheelMode.Edit, async (fs, ct) =>
            {
                var outputs = new List<string>();
                foreach (var f in fs)
                {
                    var o = OutputPaths.Sibling(f, "png", "fotograma");
                    await Tools.FFmpegAsync(ct, "-ss", "1", "-i", f, "-frames:v", "1", o);
                    if (!File.Exists(o)) await Tools.FFmpegAsync(ct, "-i", f, "-frames:v", "1", o); // clip shorter than 1 s
                    outputs.Add(o);
                }
                return outputs;
            });
        }
        else
        {
            yield return Edit("audio.compress", "Comprimir", "comprimido", "mp3", ["-vn", "-c:a", "libmp3lame", "-b:a", "128k"]);
            yield return Edit("audio.mono", "Mono", "mono", null, ["-vn", "-ac", "1"]);
            yield return Edit("audio.norm", "Normalizar", "normalizado", null, ["-vn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11"]);
            yield return Edit("audio.trim", "Quitar silencios", "sin silencios", null,
                ["-vn", "-af", "silenceremove=start_periods=1:start_threshold=-50dB:stop_periods=-1:stop_threshold=-50dB:stop_duration=0.7"]);
            yield return Edit("audio.fast", "Velocidad ×1.5", "x1.5", null, ["-vn", "-af", "atempo=1.5"]);
        }

        if (files.Count > 1)
        {
            yield return new FileAction("media.concat", "Unir", WheelMode.Edit, (fs, ct) => Concat(fs, video, ct));
        }
    }

    private static FileAction Edit(string id, string label, string suffix, string? forceExt, string[] args) =>
        new(id, label, WheelMode.Edit, async (fs, ct) =>
        {
            var outputs = new List<string>();
            foreach (var f in fs)
            {
                var o = OutputPaths.Sibling(f, forceExt ?? FileKinds.Ext(f), suffix);
                await Run(f, o, args, ct);
                outputs.Add(o);
            }
            return outputs;
        });

    public static async Task<IReadOnlyList<string>> Convert(IReadOnlyList<string> files, string target, CancellationToken ct)
    {
        var outputs = new List<string>();
        foreach (var f in files)
        {
            var o = OutputPaths.Sibling(f, target);
            await Run(f, o, ArgsFor(target), ct);
            outputs.Add(o);
        }
        return outputs;
    }

    private static async Task Run(string input, string output, string[] args, CancellationToken ct)
    {
        try
        {
            await Tools.FFmpegAsync(ct, ["-i", input, .. args, output]);
        }
        catch
        {
            try { File.Delete(output); } catch { }
            throw;
        }
    }

    public static string[] ArgsFor(string target) => target switch
    {
        "mp4" or "m4v" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-vf", EvenSize, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
        "mov" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-vf", EvenSize, "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"],
        "mkv" => ["-map", "0", "-c", "copy"],
        "webm" => ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1", "-c:a", "libopus", "-b:a", "128k"],
        "avi" => ["-c:v", "mpeg4", "-q:v", "3", "-vf", EvenSize, "-c:a", "libmp3lame", "-q:a", "3"],
        "gif" => ["-vf", "fps=12,scale='min(640,iw)':-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0"],
        "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
        "wav" => ["-vn", "-c:a", "pcm_s16le"],
        "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
        "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
        "flac" => ["-vn", "-c:a", "flac"],
        "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
        _ => throw new NotSupportedException(target),
    };

    private static async Task<IReadOnlyList<string>> Concat(IReadOnlyList<string> files, bool video, CancellationToken ct)
    {
        var ordered = files.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        var output = OutputPaths.Sibling(ordered[0], video ? "mp4" : "mp3", "unido");
        // Re-encoding through the concat filter tolerates mismatched codecs and sample rates.
        var inputs = ordered.SelectMany(f => new[] { "-i", f }).ToList();
        var streams = string.Concat(ordered.Select((_, i) => video ? $"[{i}:v][{i}:a]" : $"[{i}:a]"));
        var filter = video
            ? $"{streams}concat=n={ordered.Count}:v=1:a=1[v][a]"
            : $"{streams}concat=n={ordered.Count}:v=0:a=1[a]";
        var maps = video ? new[] { "-map", "[v]", "-map", "[a]" } : ["-map", "[a]"];
        string[] codec = video
            ? ["-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart"]
            : ["-c:a", "libmp3lame", "-q:a", "2"];
        await Tools.FFmpegAsync(ct, [.. inputs, "-filter_complex", filter, .. maps, .. codec, output]);
        return [output];
    }
}
