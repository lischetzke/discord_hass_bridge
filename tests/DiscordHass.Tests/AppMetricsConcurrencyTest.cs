using System.Threading;
using System.Threading.Tasks;
using DiscordHass.App;
using Xunit;

namespace DiscordHass.Tests;

public class AppMetricsConcurrencyTest
{
    private static void Reset()
    {
        // Tests share process state — reset the relevant counter before each test.
        Interlocked.Exchange(ref AppMetrics.DiscordEventsReceived, 0);
        Interlocked.Exchange(ref AppMetrics.HaFramesSent, 0);
        Interlocked.Exchange(ref AppMetrics.CameraRegistryPolls, 0);
    }

    [Fact]
    public async Task IncrementDiscordEvent_IsAtomicUnderParallelStress()
    {
        Reset();
        const int Tasks = 8;
        const int IncrementsPerTask = 10_000;

        Task[] runners = new Task[Tasks];
        for (int i = 0; i < Tasks; i++)
        {
            runners[i] = Task.Run(() =>
            {
                for (int j = 0; j < IncrementsPerTask; j++)
                    AppMetrics.IncrementDiscordEvent();
            });
        }
        await Task.WhenAll(runners);

        Assert.Equal(Tasks * IncrementsPerTask, AppMetrics.DiscordEventsReceived);
    }

    [Fact]
    public async Task DifferentCountersAreIndependent()
    {
        Reset();
        await Task.WhenAll(
            Task.Run(() => { for (int i = 0; i < 1000; i++) AppMetrics.IncrementHaFrameSent(); }),
            Task.Run(() => { for (int i = 0; i < 500;  i++) AppMetrics.IncrementCameraPoll();   }));

        Assert.Equal(1000, AppMetrics.HaFramesSent);
        Assert.Equal(500,  AppMetrics.CameraRegistryPolls);
        Assert.Equal(0,    AppMetrics.DiscordEventsReceived);
    }
}
