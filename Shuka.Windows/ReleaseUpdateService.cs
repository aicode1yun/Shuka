using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shuka;

internal static class ReleaseUpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/seizue/Shuka/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/seizue/Shuka/releases";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private static readonly string StateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shuka");
    private static readonly string StateFile = Path.Combine(StateDir, "update-state.json");

    static ReleaseUpdateService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Shuka-Windows-Updater/1.0");
        Http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    public static void StartBackgroundCheck(Action<string> notify)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await CheckOnceAsync(notify);
            }
            catch
            {
                // Silent updater by design.
            }
        });
    }

    private static async Task CheckOnceAsync(Action<string> notify)
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            return;

        var state = await LoadStateAsync();
        var now = DateTimeOffset.UtcNow;
        if (now - state.LastCheckedUtc < CheckInterval)
            return;

        state.LastCheckedUtc = now;

        var latest = await GetLatestReleaseAsync();
        if (latest == null)
        {
            await SaveStateAsync(state);
            return;
        }

        if (TryGetCurrentVersion(out var current) && latest.Version <= current)
        {
            await SaveStateAsync(state);
            return;
        }

        if (string.Equals(state.LastNotifiedTag, latest.Tag, StringComparison.OrdinalIgnoreCase))
        {
            await SaveStateAsync(state);
            return;
        }

        state.LastNotifiedTag = latest.Tag;
        await SaveStateAsync(state);

        string message =
            $"[Update] New release {latest.Tag} is available: {latest.ReleasePageUrl}";
        notify(message);
    }

    private static bool TryGetCurrentVersion(out Version version)
    {
        version = new Version(1, 0, 0);
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var match = Regex.Match(info, @"\d+(\.\d+){1,3}");
            if (match.Success &&
                Version.TryParse(match.Value, out var parsed) &&
                parsed is not null)
            {
                version = parsed;
                return true;
            }
        }

        if (asm.GetName().Version is Version nameVersion)
        {
            version = nameVersion;
            return true;
        }

        return false;
    }

    private static async Task<ReleaseCheckResult?> GetLatestReleaseAsync()
    {
        try
        {
            string json = await Http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string page = root.TryGetProperty("html_url", out var html)
                ? (html.GetString() ?? ReleasesPageUrl)
                : ReleasesPageUrl;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return null;

            return new ReleaseCheckResult(tag, version, page);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<UpdateState> LoadStateAsync()
    {
        try
        {
            if (!File.Exists(StateFile))
                return new UpdateState();
            string json = await File.ReadAllTextAsync(StateFile);
            return JsonSerializer.Deserialize<UpdateState>(json) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    private static async Task SaveStateAsync(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            string json = JsonSerializer.Serialize(state);
            await File.WriteAllTextAsync(StateFile, json);
        }
        catch
        {
            // Silent updater by design.
        }
    }

    private sealed class UpdateState
    {
        public DateTimeOffset LastCheckedUtc { get; set; } = DateTimeOffset.MinValue;
        public string LastNotifiedTag { get; set; } = "";
    }

    private sealed record ReleaseCheckResult(string Tag, Version Version, string ReleasePageUrl);
}
