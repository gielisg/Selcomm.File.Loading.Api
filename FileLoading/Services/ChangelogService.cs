using System.Text.Json;
using FileLoading.Models;

namespace FileLoading.Services;

public interface IChangelogService
{
    Task<List<ChangelogEntry>> GetChangelogAsync();
}

public class ChangelogService : IChangelogService
{
    private readonly ILogger<ChangelogService> _logger;
    private List<ChangelogEntry>? _cachedChangelog;
    private readonly object _cacheLock = new();

    public ChangelogService(ILogger<ChangelogService> logger)
    {
        _logger = logger;
    }

    public async Task<List<ChangelogEntry>> GetChangelogAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedChangelog != null)
            {
                return _cachedChangelog;
            }
        }

        try
        {
            var changelogPath = Path.Combine(AppContext.BaseDirectory, "Data", "changelog.json");

            if (!File.Exists(changelogPath))
            {
                _logger.LogWarning("Changelog file not found at {Path}", changelogPath);
                return new List<ChangelogEntry>();
            }

            var json = await File.ReadAllTextAsync(changelogPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var changelog = JsonSerializer.Deserialize<List<ChangelogEntry>>(json, options) ?? new List<ChangelogEntry>();

            lock (_cacheLock)
            {
                _cachedChangelog = changelog;
            }

            _logger.LogInformation("Loaded changelog with {Count} entries", changelog.Count);
            return changelog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading changelog");
            return new List<ChangelogEntry>();
        }
    }
}
