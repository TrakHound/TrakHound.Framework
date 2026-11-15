using System;
using System.Threading;
using System.Threading.Tasks;
using TrakHound;
using TrakHound.Workers;

public class Worker : TrakHoundWorker
{
    private const int DefaultInterval = 5000;

    private CancellationTokenSource _stop;
    private Task _loop;


    // The Instance creates the Worker with this constructor, then sets Client, Volume and Variables
    public Worker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }


    protected override Task OnStartAsync()
    {
        // The "interval" Parameter (milliseconds) set in the Worker's Configuration
        var interval = Configuration.GetParameter<int>("interval");
        if (interval <= 0) interval = DefaultInterval;

        _stop = new CancellationTokenSource();
        _loop = Run(TimeSpan.FromMilliseconds(interval), _stop.Token);

        Log(TrakHoundLogLevel.Information, $"UserPackageId Worker STARTED (every {interval}ms)");
        return Task.CompletedTask;
    }

    protected override async Task OnStopAsync()
    {
        _stop.Cancel();
        await _loop;
        _stop.Dispose();

        Log(TrakHoundLogLevel.Information, "UserPackageId Worker STOPPED");
    }

    private async Task Run(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    // Do the Worker's work here (ex. read from a device and publish to a Service using Client)
                    Log(TrakHoundLogLevel.Debug, "UserPackageId Worker RUN");
                }
                catch (Exception ex)
                {
                    // Log and continue, so one failure doesn't stop the Worker
                    Log(TrakHoundLogLevel.Error, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
