using System.IO;

namespace Bubbles;

/// <summary>Opt-in trace log, enabled by setting the BUBBLES_LOG environment variable.
/// Writes to %APPDATA%\Bubbles\log.txt. Off by default and free when off.</summary>
internal static class Diagnostics
{
    private static readonly bool Enabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUBBLES_LOG"));

    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(Settings.Directory, "log.txt");

    public static void Log(string message)
    {
        if (!Enabled) return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Directory);
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
