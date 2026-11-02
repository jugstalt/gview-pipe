namespace gView.Pipe.Abstraction.Execution;

public enum PipeQueueState
{
    /// <summary>Not yet seen by the runner.</summary>
    Pending,

    /// <summary>Waits for a free slot of the workflow.</summary>
    Queued,

    Started,

    /// <summary>Skipped: no free slot and no room in the queue.</summary>
    Rejected,

    /// <summary>Taken out of the queue by its caller before it started.</summary>
    Abandoned
}

/// <summary>
/// Lets the caller of a triggered run follow it through the queue (see ADR 0016). Start and abandon are decided
/// atomically: once <see cref="TryAbandon"/> returned true, the run never starts.
/// </summary>
public sealed class PipeQueueTicket
{
    private readonly CancellationTokenSource _abandoned = new();
    private int _state;

    public PipeQueueState State => (PipeQueueState)Volatile.Read(ref _state);

    /// <summary>Canceled when the ticket is abandoned; the runner stops waiting for a slot.</summary>
    public CancellationToken AbandonedToken => _abandoned.Token;

    /// <summary>Takes the run out of the queue. False if it has already started or was rejected.</summary>
    public bool TryAbandon()
    {
        if (!TryMove(PipeQueueState.Abandoned, PipeQueueState.Pending, PipeQueueState.Queued))
        {
            return false;
        }
        _abandoned.Cancel();
        return true;
    }

    // ---- runner side ----

    public bool MarkQueued() => TryMove(PipeQueueState.Queued, PipeQueueState.Pending);

    public bool TryStart() => TryMove(PipeQueueState.Started, PipeQueueState.Pending, PipeQueueState.Queued);

    public bool Reject() => TryMove(PipeQueueState.Rejected, PipeQueueState.Pending, PipeQueueState.Queued);

    private bool TryMove(PipeQueueState to, params PipeQueueState[] from)
    {
        foreach (var state in from)
        {
            if (Interlocked.CompareExchange(ref _state, (int)to, (int)state) == (int)state)
            {
                return true;
            }
        }
        return false;
    }
}
