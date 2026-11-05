using System.Collections.Concurrent;

namespace PDFile.Office;

/// <summary>One dedicated STA thread running queued work items in order. Background thread: never blocks process exit.</summary>
internal sealed class StaWorker
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name, Action onExit)
    {
        _thread = new Thread(() =>
        {
            try { foreach (var item in _queue.GetConsumingEnumerable()) item(); }
            finally { onExit(); }
        }) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); } // forwarded to the awaiting caller
        });
        return tcs.Task;
    }

    /// <summary>No more work accepted; the thread exits after the queue drains (or after a blocked call returns).</summary>
    public void Complete() => _queue.CompleteAdding();

    public bool Join(TimeSpan timeout) => _thread.Join(timeout);
}
