using Cronos;
using Selcomm.Data.Common;
using FileLoading.Interfaces;
using FileLoading.Models;

namespace FileLoading.Workers;

/// <summary>
/// Background worker that runs scheduled file transfers.
/// Checks transfer sources on CRON schedules and fetches files.
/// </summary>
public class FileTransferWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FileTransferWorker> _logger;
    private readonly Dictionary<int, DateTime> _lastRunTimes = new();
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public FileTransferWorker(IServiceProvider serviceProvider, ILogger<FileTransferWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileTransferWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledTransfersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FileTransferWorker");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("FileTransferWorker stopping");
    }

    private async Task ProcessScheduledTransfersAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var transferService = scope.ServiceProvider.GetRequiredService<IFileTransferService>();

        // Create a system security context
        var context = SecurityContext.Anonymous("default", "file_transfer_worker");

        // Get all enabled transfer sources
        var sourcesResult = await transferService.GetSourceConfigsAsync(context);
        if (!sourcesResult.IsSuccess || sourcesResult.Data == null)
        {
            _logger.LogWarning("Failed to get transfer sources: {Error}", sourcesResult.ErrorMessage);
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var source in sourcesResult.Data.Where(s => s.IsEnabled && !string.IsNullOrEmpty(s.CronSchedule)))
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                if (ShouldRunNow(source.SourceId, source.CronSchedule!, now))
                {
                    _logger.LogInformation("Running scheduled transfer for source: {SourceId}", source.SourceId);

                    var result = await transferService.FetchFilesFromSourceAsync(source.SourceId, context);

                    if (result.IsSuccess && result.Data != null)
                    {
                        _logger.LogInformation(
                            "Scheduled transfer complete for {SourceId}: Found={Found}, Downloaded={Downloaded}, Skipped={Skipped}, Failed={Failed}",
                            source.SourceId,
                            result.Data.FilesFound,
                            result.Data.FilesDownloaded,
                            result.Data.FilesSkipped,
                            result.Data.FilesFailed);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Scheduled transfer failed for {SourceId}: {Error}",
                            source.SourceId,
                            result.ErrorMessage);
                    }

                    _lastRunTimes[source.SourceId] = now;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled transfer for source: {SourceId}", source.SourceId);
            }
        }
    }

    private bool ShouldRunNow(int sourceId, string cronExpression, DateTime now)
    {
        try
        {
            var cron = CronExpression.Parse(cronExpression, CronFormat.IncludeSeconds);

            // Get last run time, or a time before the check interval
            if (!_lastRunTimes.TryGetValue(sourceId, out var lastRun))
            {
                lastRun = now.AddMinutes(-2);
            }

            // Check if next occurrence is before or at current time
            var nextOccurrence = cron.GetNextOccurrence(lastRun, TimeZoneInfo.Utc);

            return nextOccurrence.HasValue && nextOccurrence.Value <= now;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid CRON expression for source {SourceId}: {Cron}", sourceId, cronExpression);
            return false;
        }
    }
}

/// <summary>
/// Options for the file transfer worker.
/// </summary>
public class FileTransferWorkerOptions
{
    /// <summary>
    /// Whether the worker is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval to check for scheduled transfers (in seconds).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 60;
}
