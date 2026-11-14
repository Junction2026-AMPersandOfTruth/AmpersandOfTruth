namespace AmpersandOfTruth.Tests.Features.Investigations.View;

/// <summary>
/// Counts a fake's calls in flight and can hold them open, so a test proves that fetches overlap without a stopwatch:
/// <see cref="Hold"/>, start the work, <see cref="WaitForAsync"/> N calls in flight together, <see cref="Release"/>.
/// While calls are held, code that fetches one after another never starts its second call, so the wait times out and
/// the test fails. Nothing depends on how fast the thread pool is.
/// </summary>
public sealed class InFlightCalls
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];
    private TaskCompletionSource? _held;
    private int _current;
    private int _max;

    /// <summary>The most calls that were ever in flight at the same moment.</summary>
    public int Max
    {
        get
        {
            lock (_gate)
            {
                return _max;
            }
        }
    }

    /// <summary>Calls that enter from now on stay in flight until <see cref="Release"/>.</summary>
    public void Hold()
    {
        lock (_gate)
        {
            _held ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _held?.TrySetResult();
            _held = null;
        }
    }

    /// <summary>
    /// Completes once <paramref name="count"/> calls have been in flight together; fails the test if that does not
    /// happen within the timeout. Only a failing run ever waits: overlapping calls are counted as soon as they start.
    /// </summary>
    public async Task WaitForAsync(int count)
    {
        Task reached;
        lock (_gate)
        {
            if (_max >= count)
            {
                return;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, waiter));
            reached = waiter.Task;
        }

        try
        {
            await reached.WaitAsync(WaitLimit);
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"Expected {count} calls in flight together, saw at most {Max} within {WaitLimit.TotalSeconds:0} s: "
                + "the fetches run one after another instead of concurrently.");
        }
    }

    /// <summary>A fake wraps each call in <c>using var call = await Calls.EnterAsync(ct);</c>.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        Task held;
        lock (_gate)
        {
            _current++;
            if (_current > _max)
            {
                _max = _current;
                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (_max >= _waiters[i].Count)
                    {
                        _waiters[i].Reached.TrySetResult();
                        _waiters.RemoveAt(i);
                    }
                }
            }

            held = _held?.Task ?? Task.CompletedTask;
        }

        try
        {
            // Completes synchronously unless calls are being held, so a fake without Hold() behaves as before.
            await held.WaitAsync(ct);
        }
        catch
        {
            Exit();
            throw;
        }

        return new Call(this);
    }

    private void Exit()
    {
        lock (_gate)
        {
            _current--;
        }
    }

    private sealed class Call(InFlightCalls owner) : IDisposable
    {
        public void Dispose() => owner.Exit();
    }
}
