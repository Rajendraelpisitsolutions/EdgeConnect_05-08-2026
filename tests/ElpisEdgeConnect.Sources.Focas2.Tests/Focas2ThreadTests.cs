// ============================================================================
// File: Focas2ThreadTests.cs
// Purpose: Unit tests for Focas2Thread — dedicated thread with work queue.
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public sealed class Focas2ThreadTests
{
    [Fact]
    public async Task RunAsync_ExecutesOnDedicatedThread()
    {
        await using var thread = new Focas2Thread("test-1");

        var executedOnThreadId = await thread.RunAsync(() => Environment.CurrentManagedThreadId);

        executedOnThreadId.Should().Be(thread.ManagedThreadId);
        executedOnThreadId.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task RunAsync_ReturnsResult()
    {
        await using var thread = new Focas2Thread("test-2");

        var result = await thread.RunAsync(() => 42);

        result.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_PropagatesException()
    {
        await using var thread = new Focas2Thread("test-3");

        var act = () => thread.RunAsync<int>(() => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public async Task RunAsync_VoidOverload_Works()
    {
        await using var thread = new Focas2Thread("test-4");
        var tcs = new TaskCompletionSource<bool>();

        await thread.RunAsync(() => tcs.SetResult(true));

        var completed = await tcs.Task;
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_CancellationBeforeDequeue_CancelsTask()
    {
        await using var thread = new Focas2Thread("test-5");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => thread.RunAsync(() => 1, cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task RunAsync_MultipleItems_ExecuteInOrder()
    {
        await using var thread = new Focas2Thread("test-6");
        var results = new List<int>();

        await thread.RunAsync(() => results.Add(1));
        await thread.RunAsync(() => results.Add(2));
        await thread.RunAsync(() => results.Add(3));

        results.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task DisposeAsync_CompletesGracefully()
    {
        var thread = new Focas2Thread("test-7");

        var result = await thread.RunAsync(() => "done");
        result.Should().Be("done");

        await thread.DisposeAsync();

        // After dispose, the thread should have exited. No hang = success.
    }

    [Fact]
    public async Task RunAsync_AfterDispose_Throws()
    {
        var thread = new Focas2Thread("test-8");
        await thread.DisposeAsync();

        var act = () => thread.RunAsync(() => 1);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
