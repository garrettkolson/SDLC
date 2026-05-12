using FluentAssertions;
using NUnit.Framework;

namespace SDLC.Orchestrator.Tests;

[TestFixture, SingleThreaded]
public class ProcessHandleTests
{
    [Test]
    public void Task_ReturnsUnderlyingTask()
    {
        var tcs = new TaskCompletionSource<object>();
        var handle = new ProcessHandle(tcs.Task);

        handle.Task.Should().Be(tcs.Task);
    }

    [Test]
    public void Task_CompletedTask_ReturnsImmediately()
    {
        var tcs = new TaskCompletionSource<object>();
        tcs.SetResult(null!);
        var handle = new ProcessHandle(tcs.Task);

        handle.Task.IsCompleted.Should().BeTrue();
    }

    [Test]
    public void Task_PendingTask_ReturnsUncompleted()
    {
        var tcs = new TaskCompletionSource<object>();
        var handle = new ProcessHandle(tcs.Task);

        handle.Task.IsCompleted.Should().BeFalse();
    }
}
