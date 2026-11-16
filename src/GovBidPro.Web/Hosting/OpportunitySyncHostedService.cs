using GovBidPro.Application.Abstractions;
using GovBidPro.Application.Opportunities;
using Microsoft.Extensions.Options;

namespace GovBidPro.Web.Hosting;

/// <summary>
/// Scheduled, bounded sync of enabled live opportunity sources (P1.2). Disabled unless
/// OpportunitySources:Sync:Enabled is true. An in-process timer keeps the job portable across hosts;
/// it can move to a separate worker container later without changing the application service.
/// </summary>
internal sealed class OpportunitySyncHostedService(IServiceScopeFactory scopes, IOptions<OpportunitySyncOptions> options, ILogger<OpportunitySyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var job = options.Value;
        if (!job.Enabled)
        {
            logger.LogInformation("Opportunity sync job disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, job.IntervalMinutes)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContextHolder>().SetSystem();
                await scope.ServiceProvider.GetRequiredService<OpportunitySyncService>().RunAsync(job.MaxPagesPerRun, stoppingToken);
            }
#pragma warning disable CA1031 // The job must survive transient failures; errors are logged and retried next interval.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Opportunity sync run failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
