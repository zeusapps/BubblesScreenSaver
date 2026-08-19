using Microsoft.Win32;

namespace Bubbles.Session;

/// <summary>Registers the app in the per-user Run key, so it comes back after a reboot.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Bubbles";

    private static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string existing && existing.Contains("Bubbles", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;

            if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }
}
