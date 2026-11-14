using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Demo;

/// <summary>
/// Global concurrency gate for cold investigation runs on a shared 2-vCPU demo server.
/// Cached replays never touch this gate.
/// </summary>
public sealed class ColdRunGate : IDisposable
{
    private readonly SemaphoreSlim? _semaphore;
    private readonly object _sync = new();
    private int _waiting;

    [ActivatorUtilitiesConstructor]
    public ColdRunGate(IOptions<DemoOptions> options)
        : this(options.Value.MaxConcurrentColdRuns)
    {
    }

    public ColdRunGate(int maxConcurrent)
    {
        MaxConcurrent = maxConcurrent;
        if (maxConcurrent > 0)
        {
            _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }
    }

    public int MaxConcurrent { get; }

    public int AvailableSlots => _semaphore?.CurrentCount ?? int.MaxValue;

    public int WaitingCount
    {
        get
        {
            lock (_sync)
            {
                return _waiting;
            }
        }
    }

    public Lease Enter(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_semaphore is null)
        {
            return new Lease(null, 0, acquired: false, null, null);
        }

        lock (_sync)
        {
            if (_waiting == 0 && _semaphore.Wait(0))
            {
                return new Lease(this, 0, acquired: true, null, null);
            }

            var position = ++_waiting;
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var rawWait = _semaphore.WaitAsync(linkedCts.Token);
            var lease = new Lease(this, position, acquired: false, linkedCts, null);
            lease.SetWaitTask(WaitCoreAsync(rawWait, lease, ct));
            return lease;
        }
    }

    private async Task WaitCoreAsync(Task rawWait, Lease lease, CancellationToken callerCt)
    {
        var acquired = false;
        try
        {
            await rawWait.ConfigureAwait(false);
            acquired = true;
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw new OperationCanceledException(callerCt);
        }
        finally
        {
            lock (_sync)
            {
                _waiting--;
                if (acquired)
                {
                    if (lease.Disposed)
                    {
                        _semaphore!.Release();
                    }
                    else
                    {
                        lease.Acquired = true;
                    }
                }
            }

            lease.DisposeCts();
        }
    }

    private void ReleaseLease(Lease lease)
    {
        lock (_sync)
        {
            if (lease.Disposed)
            {
                return;
            }

            lease.Disposed = true;
            if (lease.Acquired)
            {
                lease.Acquired = false;
                _semaphore!.Release();
            }
        }

        lease.CancelWait();
    }

    public void Dispose() => _semaphore?.Dispose();

    public sealed class Lease : IDisposable
    {
        private readonly ColdRunGate? _gate;
        private CancellationTokenSource? _cts;
        private Task? _waitTask;

        internal Lease(
            ColdRunGate? gate,
            int queuedPosition,
            bool acquired,
            CancellationTokenSource? cts,
            Task? waitTask)
        {
            _gate = gate;
            QueuedPosition = queuedPosition;
            Acquired = acquired;
            _cts = cts;
            _waitTask = waitTask;
        }

        /// <summary>1-based position in the queue when waiting; 0 when a slot was acquired immediately.</summary>
        public int QueuedPosition { get; }

        internal bool Acquired { get; set; }

        internal bool Disposed { get; set; }

        internal void SetWaitTask(Task waitTask) => _waitTask = waitTask;

        public Task WaitAsync() => _waitTask ?? Task.CompletedTask;

        internal void CancelWait()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        internal void DisposeCts()
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            cts?.Dispose();
        }

        public void Dispose() => _gate?.ReleaseLease(this);
    }
}
