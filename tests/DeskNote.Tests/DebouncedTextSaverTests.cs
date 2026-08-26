using DeskNote.Core.Storage;

namespace DeskNote.Tests;

[TestClass]
public sealed class DebouncedTextSaverTests
{
    [TestMethod]
    public async Task Schedule_CoalescesRapidChangesToLatestSnapshot()
    {
        List<string> saved = [];
        await using DebouncedTextSaver saver = new((content, _) =>
        {
            lock (saved) saved.Add(content);
            return Task.CompletedTask;
        });

        saver.Schedule("a", TimeSpan.FromMilliseconds(50));
        saver.Schedule("ab", TimeSpan.FromMilliseconds(50));
        saver.Schedule("abc", TimeSpan.FromMilliseconds(50));
        await Task.Delay(250);

        CollectionAssert.AreEqual(new[] { "abc" }, saved);
    }

    [TestMethod]
    public async Task Flush_CancelsDelayAndImmediatelySavesCurrentSnapshot()
    {
        List<string> saved = [];
        await using DebouncedTextSaver saver = new((content, _) =>
        {
            saved.Add(content);
            return Task.CompletedTask;
        });

        saver.Schedule("stale", TimeSpan.FromSeconds(10));
        await saver.FlushAsync("current");

        CollectionAssert.AreEqual(new[] { "current" }, saved);
    }

    [TestMethod]
    public async Task DisposeAsync_WaitsForAnAlreadyRunningSave()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DebouncedTextSaver saver = new(async (_, _) =>
        {
            started.SetResult();
            await release.Task;
        });
        saver.Schedule("in progress", TimeSpan.Zero);
        await started.Task;

        Task disposeTask = saver.DisposeAsync().AsTask();
        Assert.IsFalse(disposeTask.IsCompleted);
        release.SetResult();
        await disposeTask;
    }
}
