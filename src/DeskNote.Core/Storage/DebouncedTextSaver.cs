namespace DeskNote.Core.Storage;

/// <summary>
/// Coalesces rapid editor changes while serializing actual writes. The caller supplies
/// immutable text snapshots, so no background thread touches a UI control.
/// </summary>
public sealed class DebouncedTextSaver(Func<string, CancellationToken, Task> saveAsync) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly HashSet<Task> _scheduledTasks = [];
    private CancellationTokenSource? _delayCancellation;
    private bool _disposed;

    public event EventHandler<Exception>? SaveFailed;

    public void Schedule(string content, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _delayCancellation?.Cancel();
            _delayCancellation?.Dispose();
            _delayCancellation = new CancellationTokenSource();
            Task scheduled = SaveAfterDelayAsync(content, delay, _delayCancellation.Token);
            _scheduledTasks.Add(scheduled);
            _ = scheduled.ContinueWith(
                completed =>
                {
                    lock (_sync)
                    {
                        _scheduledTasks.Remove(completed);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    public async Task FlushAsync(string content, CancellationToken cancellationToken = default)
    {
        Task[] scheduledTasks;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _delayCancellation?.Cancel();
            scheduledTasks = _scheduledTasks.ToArray();
        }

        await Task.WhenAll(scheduledTasks);
        await SaveSerializedAsync(content, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] scheduledTasks;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _delayCancellation?.Cancel();
            _delayCancellation?.Dispose();
            scheduledTasks = _scheduledTasks.ToArray();
        }

        await Task.WhenAll(scheduledTasks);
        _saveGate.Dispose();
    }

    private async Task SaveAfterDelayAsync(string content, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await SaveSerializedAsync(content, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke(this, exception);
        }
    }

    private async Task SaveSerializedAsync(string content, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            await saveAsync(content, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
