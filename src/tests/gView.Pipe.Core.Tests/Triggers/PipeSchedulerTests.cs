using gView.Pipe.Abstraction.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Core.Tests.Triggers;

public class PipeSchedulerTests
{
    [Fact]
    public async Task Six_field_expressions_fire_every_second_until_disposed()
    {
        using var services = PipeTestHost.Create();
        var host = services.GetRequiredService<IPipeTriggerHost>();
        var fired = 0;

        var registration = host.RegisterSchedule("* * * * * *", TimeZoneInfo.Utc, _ =>
        {
            Interlocked.Increment(ref fired);
            return Task.CompletedTask;
        });

        await Task.Delay(2500);
        registration.Dispose();
        var afterDispose = fired;
        await Task.Delay(1200);

        Assert.InRange(afterDispose, 1, 4);
        Assert.Equal(afterDispose, fired);
    }

    [Fact]
    public void Invalid_expressions_throw_on_registration()
    {
        using var services = PipeTestHost.Create();
        var host = services.GetRequiredService<IPipeTriggerHost>();

        Assert.ThrowsAny<Exception>(() => host.RegisterSchedule("not a cron", TimeZoneInfo.Utc, _ => Task.CompletedTask));
    }
}
