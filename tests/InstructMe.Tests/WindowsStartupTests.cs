using Microsoft.Win32;

namespace InstructMe.Tests;

public class WindowsStartupTests
{
    [Fact]
    public void Launch_at_startup_is_on_by_default_also_for_older_settings_files()
    {
        Assert.True(new AppSettings().LaunchAtStartup);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"hotkey":"Ctrl+Alt+L"}""")!.LaunchAtStartup);
    }

    [Fact]
    public void Apply_adds_the_quoted_app_path_and_removes_it()
    {
        // A throwaway key, so the test never changes the real Run entry.
        var keyPath = $@"Software\InstructMe.Tests\{Guid.NewGuid():N}";
        try
        {
            WindowsStartup.Apply(true, keyPath);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
                Assert.Equal($"\"{Environment.ProcessPath}\"", key.GetValue("InstructMe"));

            WindowsStartup.Apply(false, keyPath);
            WindowsStartup.Apply(false, keyPath);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
                Assert.Null(key.GetValue("InstructMe"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\InstructMe.Tests", throwOnMissingSubKey: false);
        }
    }
}
