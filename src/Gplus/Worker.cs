using Google.Apis.Auth.OAuth2.Responses;
using Gplus.Gmail;
using Microsoft.Extensions.Options;

namespace Gplus;

/// <summary>The service loop: sweep, sleep, repeat — surviving network blips and missing auth.</summary>
public sealed class Worker(Sweeper sweeper, IOptionsMonitor<GplusOptions> options, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.CurrentValue;
        logger.LogInformation("Gplus started: action={Action}, dryRun={DryRun}, every {Seconds}s, query \"{Query}\", data in {Dir}",
            opt.Action, opt.DryRun, opt.PollIntervalSeconds, opt.Query, AppPaths.DataDir);

        var failures = 0;
        var lastPrune = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                opt = options.CurrentValue;
                var r = await sweeper.SweepAsync(new SweepOptions(Apply: !opt.DryRun, UseSeen: true), stoppingToken);
                if (r.Examined > 0)
                    logger.LogInformation("Sweep: {Listed} unread, {Examined} new examined, {Junk} junk, {Removed} removed{Dry}",
                        r.Listed, r.Examined, r.Junk, r.Removed, opt.DryRun ? " (dry run)" : "");
                if (r.Trained + r.Restored > 0)
                    logger.LogInformation("Training: {Trained} learned from {Train}, {Restored} restored from {NotJunk}",
                        r.Trained, opt.TrainLabel, r.Restored, opt.NotJunkLabel);
                failures = 0;
                wait = TimeSpan.FromSeconds(Math.Max(30, opt.PollIntervalSeconds));

                if (DateTime.Now - lastPrune > TimeSpan.FromDays(1))
                {
                    sweeper.PruneLogs();
                    lastPrune = DateTime.Now;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (NotAuthorizedException ex)
            {
                logger.LogError("{Message}", ex.Message);
                sweeper.Reset();
                wait = TimeSpan.FromMinutes(5);
            }
            catch (TokenResponseException ex) when (ex.Error?.Error == "invalid_grant")
            {
                logger.LogError("Google rejected the saved login (revoked or expired). Run \"Gplus authorize\" again.");
                sweeper.Reset();
                wait = TimeSpan.FromMinutes(15);
            }
            catch (Exception ex)
            {
                // Network outage, Google 5xx, quota — back off exponentially up to 30 min.
                failures++;
                wait = TimeSpan.FromSeconds(Math.Min(1800, 30 * Math.Pow(2, Math.Min(failures, 6))));
                logger.LogWarning(ex, "Sweep failed (attempt {Failures}); retrying in {Wait}", failures, wait);
                sweeper.Reset();
            }

            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
