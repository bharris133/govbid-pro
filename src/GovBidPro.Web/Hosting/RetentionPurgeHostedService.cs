using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Retention;
using Microsoft.Extensions.Options;

namespace GovBidPro.Web.Hosting;

/// <summary>
/// Periodic retention purge. Disabled unless Retention:PurgeJob:Enabled is true (off outside
/// Development until LEG-01). Moves to the separate Worker process with the ingestion milestone.
/// </summary>
internal sealed class RetentionPurgeHostedService(IServiceScopeFactory scopes, IOptions<RetentionOptions> options, ILogger<RetentionPurgeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = options.Value.PurgeJob;
        if (!job.Enabled)
        {
            logger.LogInformation("Retention purge job disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, job.IntervalMinutes)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContextHolder>().SetSystem();
                var summary = await scope.ServiceProvider.GetRequiredService<RetentionPurgeService>().RunOnceAsync(stoppingToken);
                logger.LogInformation("Retention purge: {Purged} purged, {Failed} failed, {Held} on legal hold.", summary.Purged, summary.Failed, summary.SkippedForLegalHold);
            }
#pragma warning disable CA1031 // The job must survive transient failures; errors are logged and retried next interval.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Retention purge run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
