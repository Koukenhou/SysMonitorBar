namespace SysMonitorBar.Core.Startup;

/// <summary>
/// Pure rules for the "start with Windows" entry (the <c>HKCU\...\Run</c> value); the registry
/// access itself lives in the app.
/// </summary>
public static class StartupCommand
{
    /// <summary>The command written to the Run value: the exe's absolute path in quotes.</summary>
    public static string For(string exePath) => $"\"{exePath}\"";

    /// <summary>The executable path inside a Run command, with or without quotes and arguments.</summary>
    public static string? ExePathOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        command = command.Trim();
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? command[..(exe + 4)] : command.Split(' ')[0];
    }

    /// <summary>
    /// True when the registered entry points at an exe that no longer exists (the app was moved),
    /// so it should be rewritten to the current exe. An entry pointing at another copy that still
    /// exists is left alone.
    /// </summary>
    public static bool NeedsRepair(string? registeredCommand, string currentExePath, Func<string, bool> fileExists)
    {
        var registered = ExePathOf(registeredCommand);
        if (registered is null || string.Equals(registered, currentExePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !fileExists(registered);
    }

    /// <summary>
    /// Whether Windows' "Startup apps" list (Task Manager / Settings) has switched the entry off:
    /// the <c>StartupApproved\Run</c> value's first byte has its low bit set (0x03 = disabled, 0x02 = enabled).
    /// </summary>
    public static bool IsDisabledByUser(byte[]? startupApproved) =>
        startupApproved is { Length: > 0 } && (startupApproved[0] & 1) == 1;
}
