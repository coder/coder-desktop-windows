using Coder.Desktop.Vpn.Utilities;

namespace Coder.Desktop.Tests.Vpn.Utilities;

[TestFixture]
public class TaskUtilitiesTest
{
    [Test(Description = "CancellableWhenAll with no tasks should complete immediately")]
    public void CancellableWhenAll_NoTasks()
    {
        var task = TaskUtilities.CancellableWhenAll(new CancellationTokenSource());
        Assert.That(task.IsCompleted, Is.True);
    }

    [Test(Description = "CancellableWhenAll with a single task should complete")]
    public async Task CancellableWhenAll_SingleTask()
    {
        var innerTask = new TaskCompletionSource();
        var task = TaskUtilities.CancellableWhenAll(new CancellationTokenSource(), innerTask.Task);
        Assert.That(task.IsCompleted, Is.False);
        innerTask.SetResult();
        await task;
    }

    [Test(Description = "CancellableWhenAll with a single task that faults should propagate the exception")]
    public void CancellableWhenAll_SingleTaskFault()
    {
        var cts = new CancellationTokenSource();
        var innerTask = new TaskCompletionSource();
        var task = TaskUtilities.CancellableWhenAll(cts, innerTask.Task);
        Assert.That(task.IsCompleted, Is.False);
        innerTask.SetException(new InvalidOperationException("Test"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test(Description = "CancellableWhenAll with a single task that is canceled should propagate the cancellation")]
    public void CancellableWhenAll_SingleTaskCanceled()
    {
        var cts = new CancellationTokenSource();
        var innerTask = new TaskCompletionSource();
        var task = TaskUtilities.CancellableWhenAll(cts, innerTask.Task);
        Assert.That(task.IsCompleted, Is.False);
        innerTask.SetCanceled();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test(Description = "CancellableWhenAll with multiple tasks should complete when all tasks are completed")]
    public async Task CancellableWhenAll_MultipleTasks()
    {
        var cts = new CancellationTokenSource();
        var innerTask1 = new TaskCompletionSource();
        var innerTask2 = new TaskCompletionSource();

        var task = TaskUtilities.CancellableWhenAll(cts, innerTask1.Task, innerTask2.Task);
        Assert.That(task.IsCompleted, Is.False);
        // This dance of awaiting a newly added continuation task before
        // completing the TCS is to ensure that the original continuation task
        // finished since it's inlinable.
        var task1ContinueTask = innerTask1.Task.ContinueWith(_ => { });
        innerTask1.SetResult();
        await task1ContinueTask;
        Assert.That(task.IsCompleted, Is.False);
        var task2ContinueTask = innerTask2.Task.ContinueWith(_ => { });
        innerTask2.SetResult();
        await task2ContinueTask;
        await task;
    }

    [Test(Description = "CancellableWhenAll with multiple tasks that fault should propagate the first exception only")]
    public async Task CancellableWhenAll_MultipleTasksFault()
    {
        var cts = new CancellationTokenSource();
        var innerTask1 = new TaskCompletionSource();
        var innerTask2 = new TaskCompletionSource();

        var task = TaskUtilities.CancellableWhenAll(cts, innerTask1.Task, innerTask2.Task);
        Assert.That(task.IsCompleted, Is.False);
        var task1ContinueTask = innerTask1.Task.ContinueWith(_ => { });
        innerTask1.SetException(new Exception("Test1"));
        await task1ContinueTask;
        Assert.That(task.IsCompleted, Is.False);
        var task2ContinueTask = innerTask2.Task.ContinueWith(_ => { });
        innerTask2.SetException(new Exception("Test2"));
        await task2ContinueTask;
        var ex = Assert.ThrowsAsync<Exception>(async () => await task);
        Assert.That(ex.Message, Is.EqualTo("Test1"));
    }

    [Test(Description = "CancellableWhenAll with an exception and a cancellation should propagate the first thing")]
    public async Task CancellableWhenAll_MultipleTasksFaultAndCanceled()
    {
        var cts = new CancellationTokenSource();
        var innerTask1 = new TaskCompletionSource();
        var innerTask2 = new TaskCompletionSource();
        var innertask3 = Task.CompletedTask;

        var task = TaskUtilities.CancellableWhenAll(cts, innerTask1.Task, innerTask2.Task, innertask3);
        Assert.That(task.IsCompleted, Is.False);
        var task1ContinueTask = innerTask1.Task.ContinueWith(_ => { });
        innerTask1.SetException(new Exception("Test1"));
        await task1ContinueTask;
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(cts.IsCancellationRequested, Is.True);
        var task2ContinueTask = innerTask2.Task.ContinueWith(_ => { });
        innerTask2.SetCanceled();
        await task2ContinueTask;
        var ex = Assert.ThrowsAsync<Exception>(async () => await task);
        Assert.That(ex.Message, Is.EqualTo("Test1"));
    }

    [Test(Description = "CancellableWhenAll with a cancellation and an exception should propagate the first thing")]
    public async Task CancellableWhenAll_MultipleTasksCanceledAndFault()
    {
        var cts = new CancellationTokenSource();
        var innerTask1 = new TaskCompletionSource();
        var innerTask2 = new TaskCompletionSource();
        var innertask3 = Task.CompletedTask;

        var task = TaskUtilities.CancellableWhenAll(cts, innerTask1.Task, innerTask2.Task, innertask3);
        Assert.That(task.IsCompleted, Is.False);
        var task1ContinueTask = innerTask1.Task.ContinueWith(_ => { });
        innerTask1.SetCanceled();
        await task1ContinueTask;
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(cts.IsCancellationRequested, Is.True);
        var task2ContinueTask = innerTask2.Task.ContinueWith(_ => { });
        innerTask2.SetException(new Exception("Test2"));
        await task2ContinueTask;
        Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }
}
