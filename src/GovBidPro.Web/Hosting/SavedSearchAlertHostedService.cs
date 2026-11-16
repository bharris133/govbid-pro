using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Collaboration;
using Microsoft.Extensions.Options;

namespace GovBidPro.Web.Hosting;

/// <summary>
/// Sends Daily and Weekly saved-search alerts (P1.6). An in-process timer keeps the job portable; it can
/// move to a worker container or an external scheduler without changing the application service.
/// </summary>
internal sealed class SavedSearchAlertHostedService(IServiceScopeFactory scopes, IOptions<CollaborationOptions> options, ILogger<SavedSearchAlertHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = options.Value.Alerts;
        if (!job.Enabled)
        {
            logger.LogInformation("Saved-search alert job disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(job.IntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContextHolder>().SetSystem();
                var summary = await scope.ServiceProvider.GetRequiredService<SavedSearchAlertService>().RunAsync(stoppingToken);
                if (summary.AlertsSent > 0)
                {
                    logger.LogInformation("Saved-search alerts: {Checked} searches checked, {Sent} alerts sent.", summary.SearchesChecked, summary.AlertsSent);
                }
            }
#pragma warning disable CA1031 // The job must survive transient failures; errors are logged and retried next interval.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Saved-search alert run failed.");
            }
        }
    }
}
