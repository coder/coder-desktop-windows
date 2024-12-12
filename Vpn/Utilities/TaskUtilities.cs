namespace Coder.Desktop.Vpn.Utilities;

public static class TaskUtilities
{
    /// <summary>
    ///     Waits for all tasks to complete, but cancels the provided <c>CancellationTokenSource</c> if any task is canceled or
    ///     faulted. The first cancel or fault will be propagated to the returned Task. All passed in tasks must be using the
    ///     same <c>CancellationTokenSource</c>.
    ///     The returned task will wait for all tasks to be completed.
    /// </summary>
    /// <example>
    ///     <code lang="csharp">
    ///         var cts = new CancellationTokenSource();
    ///         var task1 = Task.Delay(1000, cts.Token);
    ///         var task2 = Task.Delay(2000, cts.Token);
    ///         await TaskUtilities.CancellableWhenAll(cts, task1, task2);
    ///     </code>
    /// </example>
    /// <param name="tasks">Tasks to wait on</param>
    /// <param name="cts">The cancellation token source that was provided to each task</param>
    /// <returns>
    ///     A task that completes when all tasks are completed, with the cancellation or exception state of the first
    ///     non-successful task
    /// </returns>
    public static async Task CancellableWhenAll(CancellationTokenSource cts, params Task[] tasks)
    {
        var taskList = tasks.ToList();
        if (taskList.Count == 0) return;
        var tcs = new TaskCompletionSource();

        var tasksWithCancellation = taskList.Select(task =>
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    cts.Cancel();
                    tcs.TrySetException(t.Exception.InnerExceptions.First());
                }
                else if (t.IsCanceled)
                {
                    cts.Cancel();
                    tcs.TrySetCanceled();
                }
            }));

        // Wait for all the task continuations to complete.
        try
        {
            await Task.WhenAll(tasksWithCancellation);
            tcs.TrySetResult();
        }
        catch
        {
            // Exception was already propagated.
        }

        await tcs.Task;
    }
}
