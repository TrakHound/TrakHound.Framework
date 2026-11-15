using System.Collections.Concurrent;

namespace TrakHound.Common.Tests;

public class TrakHoundStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);


    [Fact]
    public async Task InputItems_AreDeliveredInOrder_WhenHandlerIsAsync()
    {
        var input = new TrakHoundStream<int>();
        var stream = new TrakHoundStream<int>(input);
        var received = new ConcurrentQueue<int>();
        var done = new TaskCompletionSource();

        stream.OnReceivedAsync = async item =>
        {
            // Earlier Items wait longer, so they would finish last if processed concurrently
            await Task.Delay(item == 0 ? 50 : 1);
            received.Enqueue(item);
            if (received.Count == 20) done.TrySetResult();
        };

        for (var i = 0; i < 20; i++) await input.Push(i);

        await done.Task.WaitAsync(Timeout);
        Assert.Equal(Enumerable.Range(0, 20), received);
    }

    [Fact]
    public async Task ThrowingSubscriber_RaisesError_AndOtherSubscribersStillReceive()
    {
        var stream = new TrakHoundStream<string>();
        var errors = new List<Exception>();
        var received = new List<string>();

        stream.Error += (s, ex) => errors.Add(ex);
        stream.OnReceived = item => throw new InvalidOperationException("handler");
        stream.Received += (s, item) => throw new InvalidOperationException("first");
        stream.Received += (s, item) => received.Add(item);

        Assert.True(await stream.Push("a"));

        Assert.Equal(new[] { "a" }, received);
        Assert.Equal(new[] { "handler", "first" }, errors.Select(o => o.Message));
    }

    [Fact]
    public async Task InputConversionError_RaisesError_AndLaterItemsStillArrive()
    {
        var input = new TrakHoundStream<string>();
        var stream = new TrakHoundStream<string>(input);
        var error = new TaskCompletionSource<Exception>();
        var received = new TaskCompletionSource<string>();

        stream.Error += (s, ex) => error.TrySetResult(ex);
        stream.OnInputReceived = item => item == "bad" ? throw new FormatException() : item;
        stream.OnReceived = item => received.TrySetResult(item);

        await input.Push("bad");
        await input.Push("good");

        Assert.IsType<FormatException>(await error.Task.WaitAsync(Timeout));
        Assert.Equal("good", await received.Task.WaitAsync(Timeout));
    }

    [Fact]
    public void Dispose_CancelsStreamToken_WithoutCancellingCallerToken()
    {
        using var caller = new CancellationTokenSource();
        var stream = new TrakHoundStream<int>(caller.Token);
        var token = stream.CancellationToken;

        stream.Dispose();

        Assert.True(token.IsCancellationRequested);
        Assert.False(caller.IsCancellationRequested);
    }

    [Fact]
    public void CallerToken_CancelsStreamToken()
    {
        using var caller = new CancellationTokenSource();
        var stream = new TrakHoundStream<int>(caller.Token);

        caller.Cancel();

        Assert.True(stream.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Push_Null_IsForwardedOnlyWhenAllowed()
    {
        var strict = new TrakHoundStream<string>();
        var lenient = new TrakHoundStream<string>(allowNull: true);
        var lenientCount = 0;
        lenient.OnReceived = item => lenientCount++;

        Assert.False(await strict.Push(null));
        Assert.True(await lenient.Push(null));
        Assert.Equal(1, lenientCount);
    }

    [Fact]
    public async Task Push_UsesOnPushAsync_InsteadOfOnPush()
    {
        var stream = new TrakHoundStream<string>();
        string received = null;

        stream.OnPush = item => item + "-sync";
        stream.OnPushAsync = item => Task.FromResult(item + "-async");
        stream.OnReceived = item => received = item;

        await stream.Push("a");

        Assert.Equal("a-async", received);
    }

    [Fact]
    public async Task Push_AfterDispose_ReturnsFalse()
    {
        var stream = new TrakHoundStream<string>();
        var count = 0;
        stream.OnReceived = item => count++;

        stream.Dispose();

        Assert.False(await stream.Push("a"));
        Assert.Equal(0, count);
    }


    [Fact]
    public async Task Converting_UsesOnInputReceivedAsync_InsteadOfOnInputReceived()
    {
        var stream = new TrakHoundStream<int, string>();
        string received = null;

        stream.OnInputReceived = item => "sync";
        stream.OnInputReceivedAsync = item => Task.FromResult($"async-{item}");
        stream.OnReceived = item => received = item;

        Assert.True(await stream.PushInput(1));
        Assert.Equal("async-1", received);
    }

    [Fact]
    public async Task Converting_WithoutConverter_PassesAssignableItemsAndDropsOthers()
    {
        var passThrough = new TrakHoundStream<string, object>();
        object passed = null;
        passThrough.OnReceived = item => passed = item;

        var dropping = new TrakHoundStream<int, string>(allowNull: true);
        var droppedCount = 0;
        dropping.OnReceived = item => droppedCount++;

        Assert.True(await passThrough.PushInput("a"));
        Assert.False(await dropping.PushInput(1));

        Assert.Equal("a", passed);
        Assert.Equal(0, droppedCount);
    }

    [Fact]
    public async Task Converting_InputStreamItems_AreConvertedAndForwarded()
    {
        var input = new TrakHoundStream<int>();
        var stream = new TrakHoundStream<int, string>(input, onReceived: item => $"#{item}");
        var received = new TaskCompletionSource<string>();
        stream.OnReceived = item => received.TrySetResult(item);

        await input.Push(5);

        Assert.Equal("#5", await received.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Converting_BatchInput_ForwardsEachItem()
    {
        var input = new TrakHoundStream<IEnumerable<int>>();
        var stream = new TrakHoundStream<int, string>(input);
        stream.OnInputReceived = item => item.ToString();
        var received = new ConcurrentQueue<string>();
        var done = new TaskCompletionSource();
        stream.OnReceived = item =>
        {
            received.Enqueue(item);
            if (received.Count == 3) done.TrySetResult();
        };

        await input.Push(new[] { 1, 2, 3 });

        await done.Task.WaitAsync(Timeout);
        Assert.Equal(new[] { "1", "2", "3" }, received);
    }

    [Fact]
    public async Task Converting_Push_SendsConvertedItemToInputStream()
    {
        var input = new TrakHoundStream<int>();
        var pushedToInput = new List<int>();
        input.OnReceived = item => pushedToInput.Add(item);

        var stream = new TrakHoundStream<int, string>(input);
        stream.OnPush = item => int.Parse(item);

        Assert.True(await stream.Push("42"));
        Assert.Equal(new[] { 42 }, pushedToInput);
    }

    [Fact]
    public async Task Converting_Push_ReturnsFalse_WhenItemCannotBeConverted()
    {
        var input = new TrakHoundStream<int>();
        var stream = new TrakHoundStream<int, string>(input);

        Assert.False(await stream.Push("42"));
        Assert.False(await new TrakHoundStream<int, string>().Push("42"));
    }


    [Fact]
    public void InputDisposed_DisposesWrapper()
    {
        var input = new TrakHoundStream<int>();
        var stream = new TrakHoundStream<int, string>(input);
        var disposed = false;
        stream.Disposed += (s, id) => disposed = true;

        input.Dispose();

        Assert.True(disposed);
        Assert.True(stream.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void InputDisposed_DisposesFanIn_OnlyAfterLastInput()
    {
        var first = new TrakHoundStream<int>();
        var second = new TrakHoundStream<int>();
        var stream = new TrakHoundStream<int>(new[] { first, second });
        var disposed = false;
        stream.Disposed += (s, id) => disposed = true;

        first.Dispose();
        Assert.False(disposed);

        second.Dispose();
        Assert.True(disposed);
    }

    [Fact]
    public void Dispose_ContinuesWhenOnDisposedThrows()
    {
        var input = new TrakHoundStream<int>();
        var inputDisposed = false;
        input.Disposed += (s, id) => inputDisposed = true;

        var stream = new TrakHoundStream<int>(input);
        var errors = new List<Exception>();
        var disposed = false;
        stream.Error += (s, ex) => errors.Add(ex);
        stream.Disposed += (s, id) => disposed = true;
        stream.OnDisposed = () => throw new InvalidOperationException();

        stream.Dispose();

        Assert.True(inputDisposed);
        Assert.True(disposed);
        Assert.Single(errors);
    }

    [Fact]
    public void Dispose_LeavesInputsOpen_WhenRequested()
    {
        var input = new TrakHoundStream<int>();
        var inputDisposed = false;
        input.Disposed += (s, id) => inputDisposed = true;

        var stream = new TrakHoundStream<int, int>(input) { LeaveInputsOpen = true };
        stream.Dispose();

        Assert.False(inputDisposed);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromInputs()
    {
        var input = new TrakHoundStream<int>();
        var stream = new TrakHoundStream<int>(input) { LeaveInputsOpen = true };
        var count = 0;
        stream.Received += (s, item) => count++;

        stream.Dispose();
        await input.Push(1);
        await Task.Delay(50);

        Assert.Equal(0, count);
    }


    [Fact]
    public async Task ReadAllAsync_YieldsItems_UntilStreamIsDisposed()
    {
        var stream = new TrakHoundStream<int>();
        var read = new List<int>();

        // The first MoveNextAsync subscribes to the Stream before it waits for an Item
        await using var enumerator = stream.ReadAllAsync().GetAsyncEnumerator();
        var next = enumerator.MoveNextAsync();

        await stream.Push(1);
        await stream.Push(2);
        stream.Dispose();

        while (await next.AsTask().WaitAsync(Timeout))
        {
            read.Add(enumerator.Current);
            next = enumerator.MoveNextAsync();
        }

        Assert.Equal(new[] { 1, 2 }, read);
    }

    [Fact]
    public async Task ReadAllAsync_CompletesImmediately_ForDisposedStream()
    {
        var stream = new TrakHoundStream<int>();
        stream.Dispose();

        var count = 0;
        await foreach (var item in stream.ReadAllAsync().WithCancellation(new CancellationTokenSource(Timeout).Token)) count++;

        Assert.Equal(0, count);
    }


    [Fact]
    public async Task QueueConsumer_EmitsBatches()
    {
        using var consumer = new TrakHoundQueueConsumer<int>("queue", interval: 20);
        var received = new TaskCompletionSource<IEnumerable<int>>();
        consumer.OnReceived = items => received.TrySetResult(items.ToList());

        consumer.Push(1);
        consumer.Push(2);

        Assert.Equal(new[] { 1, 2 }, await received.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task SingleQueueConsumer_ConvertsEachItem()
    {
        using var consumer = new TrakHoundSingleQueueConsumer<int, string>("queue", interval: 20);
        consumer.OnConvert = item => $"#{item}";
        var received = new TaskCompletionSource<string>();
        consumer.OnReceived = item => received.TrySetResult(item);

        await consumer.PushInput(7);

        Assert.Equal("#7", await received.Task.WaitAsync(Timeout));
    }
}
