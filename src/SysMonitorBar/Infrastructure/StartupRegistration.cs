using System.IO;
using System.Security;
using Microsoft.Win32;
using SysMonitorBar.Core.Startup;

namespace SysMonitorBar.Infrastructure;

internal sealed record StartupState(string? RegisteredCommand, bool DisabledByUser)
{
    public bool IsEnabled => RegisteredCommand is not null && !DisabledByUser;
}

/// <summary>
/// "Start with Windows" through the current user's Run key only (never HKLM). The registry is the
/// source of truth: the settings file just mirrors it, because the user can also switch the entry
/// off in Task Manager's / Settings' "Startup apps" list (the StartupApproved key).
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "SysMonitorBar";

    public static string CurrentExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("The executable path is unknown.");

    public static StartupState Read()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return new StartupState(
            run?.GetValue(ValueName) as string,
            StartupCommand.IsDisabledByUser(approved?.GetValue(ValueName) as byte[]));
    }

    /// <summary>Registers the current exe. Also clears a "disabled" mark from the Startup apps list, since the user just asked for it.</summary>
    public static void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue(ValueName, StartupCommand.For(CurrentExePath), RegistryValueKind.String);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        if (approved?.GetValue(ValueName) is byte[] flags && StartupCommand.IsDisabledByUser(flags))
        {
            // 0x02 followed by zeros means "enabled" in the Startup apps list.
            approved.SetValue(ValueName, new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
    }

    public static void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Rewrites the entry when it points at an exe that no longer exists (the app folder was
    /// moved). Returns true when it did.
    /// </summary>
    public static bool RepairIfMoved()
    {
        var state = Read();
        if (!StartupCommand.NeedsRepair(state.RegisteredCommand, CurrentExePath, File.Exists))
        {
            return false;
        }
        Enable();
        return true;
    }

    public static bool IsRegistryError(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException;
}
