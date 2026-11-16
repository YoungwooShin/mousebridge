using System.Diagnostics;
namespace MouseBridge.Core;
public record RttMeasurement(double RoundTripMilliseconds, double SendMilliseconds);
public sealed class RttTimeoutException(bool sendCompleted) : TimeoutException("RTT response timed out")
{ public bool SendCompleted { get; } = sendCompleted; }
public sealed class RttProbe
{
    private sealed record Pending(long Id, long Started, TaskCompletionSource<double> Completion);
    private readonly object sync = new();
    private long nextId;
    private Pending? pending;
    public async Task<RttMeasurement> MeasureAsync(Func<long, CancellationToken, Task> send,
        TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Pending p;
        lock (sync)
        {
            if (pending != null) throw new InvalidOperationException("An RTT measurement is already in progress.");
            p = new(++nextId, Stopwatch.GetTimestamp(), new(TaskCreationOptions.RunContinuationsAsynchronously));
            pending = p;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        bool sendCompleted = false;
        try
        {
            await send(p.Id, deadline.Token).ConfigureAwait(false);
            sendCompleted = true;
            double sent = Stopwatch.GetElapsedTime(p.Started).TotalMilliseconds;
            double rtt = await p.Completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            return new(rtt, sent);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new RttTimeoutException(sendCompleted); }
        finally { lock (sync) { if (pending == p) pending = null; } }
    }
    public bool Complete(long id)
    {
        lock (sync)
        {
            if (pending == null || pending.Id != id) return false;
            return pending.Completion.TrySetResult(Stopwatch.GetElapsedTime(pending.Started).TotalMilliseconds);
        }
    }
}
