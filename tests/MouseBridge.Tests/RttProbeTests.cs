using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class RttProbeTests
{
    [Fact] public async Task ReplyCompletesImmediatelyAndAllowsAnotherProbe()
    {
        var probe = new RttProbe(); long previous = 0;
        for (int i = 0; i < 2; i++)
        {
            var result = await probe.MeasureAsync((id, ct) =>
            { Assert.True(id > previous); previous = id; Assert.True(probe.Complete(id)); return Task.CompletedTask; },
                TimeSpan.FromSeconds(5), default).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(result.RoundTripMilliseconds >= 0);
        }
    }
    [Fact] public async Task WrongAndOldRepliesDoNotCompleteNewProbe()
    {
        var probe = new RttProbe(); long old = 0;
        await probe.MeasureAsync((id, ct) => { old = id; probe.Complete(id); return Task.CompletedTask; }, TimeSpan.FromSeconds(5), default);
        var ready = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var measurement = probe.MeasureAsync((id, ct) => { ready.SetResult(id); return Task.CompletedTask; }, TimeSpan.FromSeconds(5), default);
        long next = await ready.Task;
        Assert.False(probe.Complete(old)); Assert.False(probe.Complete(next + 1)); Assert.False(measurement.IsCompleted);
        Assert.True(probe.Complete(next)); await measurement;
    }
    [Fact] public async Task ConcurrentProbeRejectedAndCancellationReleasesSlot()
    {
        var probe = new RttProbe(); using var cancel = new CancellationTokenSource();
        var first = probe.MeasureAsync((id, ct) => Task.CompletedTask, TimeSpan.FromSeconds(5), cancel.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => probe.MeasureAsync((id, ct) => Task.CompletedTask, TimeSpan.FromSeconds(5), default));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await probe.MeasureAsync((id, ct) => { probe.Complete(id); return Task.CompletedTask; }, TimeSpan.FromSeconds(5), default);
    }
    [Fact] public async Task SendFailureReleasesSlot()
    {
        var probe = new RttProbe();
        await Assert.ThrowsAsync<IOException>(() => probe.MeasureAsync((id, ct) => throw new IOException(), TimeSpan.FromSeconds(5), default));
        await probe.MeasureAsync((id, ct) => { probe.Complete(id); return Task.CompletedTask; }, TimeSpan.FromSeconds(5), default);
    }
    [Fact] public async Task TimeoutDuringSendIsDistinguishedFromMissingReply()
    {
        var probe = new RttProbe();
        var ex = await Assert.ThrowsAsync<RttTimeoutException>(() => probe.MeasureAsync((id, ct) => Task.Delay(Timeout.Infinite, ct), TimeSpan.FromMilliseconds(10), default));
        Assert.False(ex.SendCompleted);
        ex = await Assert.ThrowsAsync<RttTimeoutException>(() => probe.MeasureAsync((id, ct) => Task.CompletedTask, TimeSpan.FromMilliseconds(10), default));
        Assert.True(ex.SendCompleted);
        Assert.False(probe.Complete(2));
    }
    [Fact] public async Task CancelledSessionDoesNotSend()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel(); var probe = new RttProbe();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.MeasureAsync((id, token) => throw new Exception("Must not send"), TimeSpan.FromSeconds(5), ct.Token));
    }
}
