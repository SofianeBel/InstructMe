using Velopack;
using Velopack.Sources;

namespace InstructMe.Updates;

/// <summary>
/// Gets new versions from the GitHub releases. Only an installed copy can update:
/// a build started from the source code does nothing.
/// </summary>
internal sealed class AppUpdater
{
    private const string Repository = "https://github.com/SofianeBel/InstructMe";

    private readonly UpdateManager _manager = new(new GithubSource(Repository, accessToken: null, prerelease: false));

    /// <summary>The downloaded version that waits to be installed, or null.</summary>
    public string? ReadyVersion => _manager.UpdatePendingRestart?.Version.ToString();

    /// <summary>Downloads the newest version, if any. Returns its number, or null when there is nothing new.</summary>
    public async Task<string?> DownloadAsync()
    {
        if (!_manager.IsInstalled) return null;
        var update = await _manager.CheckForUpdatesAsync();
        if (update is null) return ReadyVersion;
        await _manager.DownloadUpdatesAsync(update);
        return update.TargetFullRelease.Version.ToString();
    }

    /// <summary>
    /// Installs the downloaded version after this process exits. The caller must shut
    /// the app down right after, so the tray icon is removed cleanly.
    /// </summary>
    public bool InstallOnExit(bool restart)
    {
        var ready = _manager.UpdatePendingRestart;
        if (ready is null) return false;
        _manager.WaitExitThenApplyUpdates(ready, silent: !restart, restart: restart);
        return true;
    }
}
