using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Documents;
using Microsoft.Extensions.Options;

namespace GovBidPro.Web.Hosting;

/// <summary>
/// Periodically re-screens stored document versions that have no malware verdict yet (P1.5).
/// An in-process timer keeps the job portable; it can move to a worker container unchanged.
/// </summary>
internal sealed class DocumentRescanHostedService(IServiceScopeFactory scopes, IOptions<DocumentRescanOptions> options, ILogger<DocumentRescanHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = options.Value;
        if (!job.Enabled)
        {
            logger.LogInformation("Document rescan job disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(job.IntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContextHolder>().SetSystem();
                var summary = await scope.ServiceProvider.GetRequiredService<DocumentRescanService>().RunAsync(job.BatchSize, stoppingToken);
                if (summary.Scanned > 0 || summary.StillPending > 0)
                {
                    logger.LogInformation("Document rescan: {Scanned} scanned, {Infected} infected, {Pending} still pending.", summary.Scanned, summary.Infected, summary.StillPending);
                }
            }
#pragma warning disable CA1031 // The job must survive transient failures; errors are logged and retried next interval.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Document rescan run failed.");
            }
        }
    }
}
