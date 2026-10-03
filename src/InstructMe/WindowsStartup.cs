using Microsoft.Win32;

namespace InstructMe;

/// <summary>Starts InstructMe when the user signs in to Windows, with the per-user Run key.</summary>
internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InstructMe";

    /// <summary>
    /// Adds or removes the Run entry. Writes only when it changes, and also fixes the path
    /// after the app moved. Task Manager can still turn the entry off: Windows keeps that in
    /// a separate key that this does not touch.
    /// </summary>
    public static void Apply(bool enabled, string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var command = $"\"{Environment.ProcessPath}\"";
        if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command);
    }
}
