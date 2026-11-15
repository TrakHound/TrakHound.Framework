// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// A Stream of Items of Type T that can receive Items from one or more input Streams and forwards them to its subscribers
    /// </summary>
    /// <remarks>
    /// Items from input Streams are processed one at a time, in the order they arrive. The Stream disposes itself when its last input Stream is disposed
    /// </remarks>
    public class TrakHoundStream<T> : ITrakHoundStream<T>
    {
        private readonly string _id;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly CancellationToken _cancellationToken;
        private readonly List<ITrakHoundStream<T>> _streams = new List<ITrakHoundStream<T>>();
        private readonly object _lock = new object();
        private readonly TrakHoundStreamQueue _inputQueue;
        private readonly bool _allowNull;
        private int _disposed;


        /// <inheritdoc/>
        public string Id => _id;

        /// <inheritdoc/>
        public CancellationToken CancellationToken => _cancellationToken;

        /// <inheritdoc/>
        public Type OutputType => typeof(T);

        /// <summary>
        /// Event for when a new <typeparamref name="T"/> is received from the stream
        /// </summary>
        public event EventHandler<T> Received;

        /// <inheritdoc/>
        public event EventHandler<string> Disposed;

        /// <summary>
        /// Raised when a hook or subscriber throws while an Item is processed or the Stream is disposed
        /// </summary>
        public event EventHandler<Exception> Error;

        /// <inheritdoc/>
        public Action<T> OnReceived { get; set; }

        /// <inheritdoc/>
        public Func<T, Task> OnReceivedAsync { get; set; }

        /// <summary>
        /// Function applied to each Item received from an input Stream before it is forwarded (a null result is dropped unless null is allowed)
        /// </summary>
        public Func<T, T> OnInputReceived { get; set; }

        /// <summary>
        /// Async function applied to each Item received from an input Stream before it is forwarded. Used instead of OnInputReceived when set
        /// </summary>
        public Func<T, Task<T>> OnInputReceivedAsync { get; set; }

        /// <summary>
        /// Function applied to each Item passed to Push before it is forwarded (a null result is dropped unless null is allowed)
        /// </summary>
        public Func<T, T> OnPush { get; set; }

        /// <summary>
        /// Async function applied to each Item passed to Push before it is forwarded. Used instead of OnPush when set
        /// </summary>
        public Func<T, Task<T>> OnPushAsync { get; set; }

        /// <inheritdoc/>
        public Action OnDisposed { get; set; }

        /// <summary>
        /// When true, Dispose leaves the input Streams running (for input Streams that are shared with other Streams)
        /// </summary>
        public bool LeaveInputsOpen { get; set; }


        /// <summary>
        /// Creates a new Stream with a generated Id. When allowNull is true, null Items are still forwarded
        /// </summary>
        public TrakHoundStream(bool allowNull = false) : this(null, CancellationToken.None, allowNull) { }

        /// <summary>
        /// Creates a new Stream with a generated Id that is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(CancellationToken cancellationToken, bool allowNull = false) : this(null, cancellationToken, allowNull) { }

        /// <summary>
        /// Creates a new Stream with the specified Id (a new Id is generated if empty)
        /// </summary>
        public TrakHoundStream(string streamId, bool allowNull = false) : this(streamId, CancellationToken.None, allowNull) { }

        /// <summary>
        /// Creates a new Stream with the specified Id (a new Id is generated if empty) that is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(string streamId, CancellationToken cancellationToken, bool allowNull = false)
        {
            _id = !string.IsNullOrEmpty(streamId) ? streamId : Guid.NewGuid().ToString();

            // Linked so Dispose cancels the Stream's token even when the caller supplied one
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationToken = _cancellationTokenSource.Token;

            _allowNull = allowNull;
            _inputQueue = new TrakHoundStreamQueue(RaiseError);
        }

        /// <summary>
        /// Creates a new Stream that receives Items from the specified input Stream
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<T> stream, string streamId = null, bool allowNull = false) : this(stream, CancellationToken.None, streamId, allowNull) { }

        /// <summary>
        /// Creates a new Stream that receives Items from the specified input Stream and is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<T> stream, CancellationToken cancellationToken, string streamId = null, bool allowNull = false) : this(streamId, cancellationToken, allowNull)
        {
            if (stream != null) AddInput(stream);
        }

        /// <summary>
        /// Creates a new Stream that receives Items from each of the specified input Streams
        /// </summary>
        public TrakHoundStream(IEnumerable<ITrakHoundStream<T>> streams, string streamId = null, bool allowNull = false) : this(streams, CancellationToken.None, streamId, allowNull) { }

        /// <summary>
        /// Creates a new Stream that receives Items from each of the specified input Streams and is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(IEnumerable<ITrakHoundStream<T>> streams, CancellationToken cancellationToken, string streamId = null, bool allowNull = false) : this(streamId, cancellationToken, allowNull)
        {
            if (streams != null)
            {
                foreach (var stream in streams)
                {
                    if (stream != null) AddInput(stream);
                }
            }
        }


        /// <summary>
        /// Cancels the Stream, calls OnDisposed, disposes the input Streams (unless LeaveInputsOpen is set), and raises the Disposed event
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            TrakHoundStreamHandlers.Run(_cancellationTokenSource.Cancel, RaiseError);
            _inputQueue.Complete();

            TrakHoundStreamHandlers.Run(OnDisposed, RaiseError);

            ITrakHoundStream<T>[] streams;
            lock (_lock)
            {
                streams = _streams.ToArray();
                _streams.Clear();
            }

            foreach (var stream in streams)
            {
                // Unsubscribe first so the input's Disposed event doesn't call back into this Stream
                stream.Received -= InputReceived;
                stream.Disposed -= InputDisposed;

                if (!LeaveInputsOpen) TrakHoundStreamHandlers.Run(stream.Dispose, RaiseError);
            }

            _cancellationTokenSource.Dispose();

            TrakHoundStreamHandlers.Run(() => Disposed?.Invoke(this, Id), RaiseError);
        }

        /// <summary>
        /// Calls OnStart and starts each input Stream
        /// </summary>
        public void Start()
        {
            if (_disposed != 0) return;

            OnStart();

            foreach (var stream in GetInputs()) stream.Start();
        }

        /// <summary>
        /// Calls OnStop and stops each input Stream
        /// </summary>
        public void Stop()
        {
            if (_disposed != 0) return;

            OnStop();

            foreach (var stream in GetInputs()) stream.Stop();
        }

        /// <summary>
        /// Called when the Stream is started (override to add start logic)
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// Called when the Stream is stopped (override to add stop logic)
        /// </summary>
        protected virtual void OnStop() { }


        /// <summary>
        /// Applies OnPushAsync (or OnPush) to the Item and forwards it to the Stream's subscribers. Returns false when the Stream is disposed or the Item was dropped
        /// </summary>
        public virtual async Task<bool> Push(T item)
        {
            if (_disposed != 0) return false;

            if (OnPushAsync != null)
            {
                var task = OnPushAsync(item);
                if (task == null) return false;
                item = await task;
            }
            else if (OnPush != null)
            {
                item = OnPush(item);
            }

            return await Emit(item);
        }

        /// <summary>
        /// Forwards the Item to OnReceived, OnReceivedAsync and the Received event without applying any hooks. Returns false when the Stream is disposed or the Item is null and null is not allowed
        /// </summary>
        protected async Task<bool> Emit(T item)
        {
            if (_disposed != 0 || (item == null && !_allowNull)) return false;

            await TrakHoundStreamHandlers.Deliver(this, item, OnReceived, OnReceivedAsync, Received, RaiseError);
            return true;
        }

        /// <summary>
        /// Raises the Error event (an exception thrown by an Error subscriber is ignored)
        /// </summary>
        protected void RaiseError(Exception exception)
        {
            try { Error?.Invoke(this, exception); }
            catch { }
        }


        private void AddInput(ITrakHoundStream<T> stream)
        {
            lock (_lock) _streams.Add(stream);

            stream.Received += InputReceived;
            stream.Disposed += InputDisposed;
        }

        private ITrakHoundStream<T>[] GetInputs()
        {
            lock (_lock) return _streams.ToArray();
        }

        private void InputReceived(object sender, T item)
        {
            _inputQueue.Add(() => ReceiveInput(item));
        }

        private void InputDisposed(object sender, string streamId)
        {
            ITrakHoundStream<T> stream = null;
            bool last;

            lock (_lock)
            {
                var index = _streams.FindIndex(o => ReferenceEquals(o, sender));
                if (index < 0) index = _streams.FindIndex(o => o.Id == streamId);
                if (index < 0) return;

                stream = _streams[index];
                _streams.RemoveAt(index);
                last = _streams.Count == 0;
            }

            stream.Received -= InputReceived;
            stream.Disposed -= InputDisposed;

            if (last) Dispose();
        }

        private async Task ReceiveInput(T item)
        {
            if (_disposed != 0) return;

            if (OnInputReceivedAsync != null)
            {
                var task = OnInputReceivedAsync(item);
                if (task == null) return;
                item = await task;
            }
            else if (OnInputReceived != null)
            {
                item = OnInputReceived(item);
            }

            await Emit(item);
        }
    }


    /// <summary>
    /// A Stream that converts Items of Type TInput received from input Streams into Items of Type TOutput and forwards them to its subscribers
    /// </summary>
    /// <remarks>
    /// Items from input Streams are processed one at a time, in the order they arrive. The Stream disposes itself when its last input Stream is disposed
    /// </remarks>
    public class TrakHoundStream<TInput, TOutput> : ITrakHoundStream<TInput, TOutput>
    {
        private readonly string _id;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly CancellationToken _cancellationToken;
        private readonly List<Input> _inputs = new List<Input>();
        private readonly object _lock = new object();
        private readonly TrakHoundStreamQueue _inputQueue;
        private readonly bool _allowNull;
        private int _disposed;


        /// <inheritdoc/>
        public string Id => _id;

        /// <inheritdoc/>
        public CancellationToken CancellationToken => _cancellationToken;

        /// <inheritdoc/>
        public Type OutputType => typeof(TOutput);

        /// <summary>
        /// Event for when a new <typeparamref name="TOutput"/> is received from the stream
        /// </summary>
        public event EventHandler<TOutput> Received;

        /// <inheritdoc/>
        public event EventHandler<string> Disposed;

        /// <summary>
        /// Raised when a hook or subscriber throws while an Item is processed or the Stream is disposed
        /// </summary>
        public event EventHandler<Exception> Error;

        /// <inheritdoc/>
        public Action<TOutput> OnReceived { get; set; }

        /// <inheritdoc/>
        public Func<TOutput, Task> OnReceivedAsync { get; set; }

        /// <summary>
        /// Function used to convert each received TInput into a TOutput. Without a conversion function, a TInput that is also a TOutput is forwarded as is and any other is dropped
        /// </summary>
        public Func<TInput, TOutput> OnInputReceived { get; set; }

        /// <summary>
        /// Async function used to convert each received TInput into a TOutput. Used instead of OnInputReceived when set
        /// </summary>
        public Func<TInput, Task<TOutput>> OnInputReceivedAsync { get; set; }

        /// <summary>
        /// Function used to convert a TOutput passed to Push(TOutput) into a TInput for the input Streams
        /// </summary>
        public Func<TOutput, TInput> OnPush { get; set; }

        /// <summary>
        /// Async function used to convert a TOutput passed to Push(TOutput) into a TInput for the input Streams. Used instead of OnPush when set
        /// </summary>
        public Func<TOutput, Task<TInput>> OnPushAsync { get; set; }

        /// <inheritdoc/>
        public Action OnDisposed { get; set; }

        /// <summary>
        /// When true, Dispose leaves the input Streams running (for input Streams that are shared with other Streams)
        /// </summary>
        public bool LeaveInputsOpen { get; set; }


        /// <summary>
        /// Creates a new Stream with a generated Id. When allowNull is true, null converted Items are still forwarded
        /// </summary>
        public TrakHoundStream(bool allowNull = false) : this(null, CancellationToken.None, allowNull) { }

        /// <summary>
        /// Creates a new Stream with a generated Id that is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(CancellationToken cancellationToken, bool allowNull = false) : this(null, cancellationToken, allowNull) { }

        /// <summary>
        /// Creates a new Stream with the specified Id (a new Id is generated if empty)
        /// </summary>
        public TrakHoundStream(string streamId, bool allowNull = false) : this(streamId, CancellationToken.None, allowNull) { }

        /// <summary>
        /// Creates a new Stream with the specified Id (a new Id is generated if empty) that is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(string streamId, CancellationToken cancellationToken, bool allowNull = false)
        {
            _id = !string.IsNullOrEmpty(streamId) ? streamId : Guid.NewGuid().ToString();

            // Linked so Dispose cancels the Stream's token even when the caller supplied one
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationToken = _cancellationTokenSource.Token;

            _allowNull = allowNull;
            _inputQueue = new TrakHoundStreamQueue(RaiseError);
        }

        /// <summary>
        /// Creates a new Stream that receives Items from the specified input Stream and converts them using the onReceived / onReceivedAsync functions
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<TInput> stream, string streamId = null, bool allowNull = false, Func<TInput, TOutput> onReceived = null, Func<TInput, Task<TOutput>> onReceivedAsync = null)
            : this(stream, CancellationToken.None, streamId, allowNull, onReceived, onReceivedAsync) { }

        /// <summary>
        /// Creates a new Stream that receives Items from the specified input Stream, converts them using the onReceived / onReceivedAsync functions, and is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<TInput> stream, CancellationToken cancellationToken, string streamId = null, bool allowNull = false, Func<TInput, TOutput> onReceived = null, Func<TInput, Task<TOutput>> onReceivedAsync = null)
            : this(streamId, cancellationToken, allowNull)
        {
            OnInputReceived = onReceived;
            OnInputReceivedAsync = onReceivedAsync;

            if (stream != null) AddInput(stream);
        }

        /// <summary>
        /// Creates a new Stream that forwards the output of the specified input/output Stream
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<TInput, TOutput> stream, string streamId = null, bool allowNull = false) : this(stream, CancellationToken.None, streamId, allowNull) { }

        /// <summary>
        /// Creates a new Stream that forwards the output of the specified input/output Stream and is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<TInput, TOutput> stream, CancellationToken cancellationToken, string streamId = null, bool allowNull = false) : this(streamId, cancellationToken, allowNull)
        {
            if (stream != null) AddInput(stream);
        }

        /// <summary>
        /// Creates a new Stream that receives Items from each of the specified input Streams and converts them using the onReceived / onReceivedAsync functions
        /// </summary>
        public TrakHoundStream(IEnumerable<ITrakHoundStream<TInput>> streams, string streamId = null, bool allowNull = false, Func<TInput, TOutput> onReceived = null, Func<TInput, Task<TOutput>> onReceivedAsync = null)
            : this(streams, CancellationToken.None, streamId, allowNull, onReceived, onReceivedAsync) { }

        /// <summary>
        /// Creates a new Stream that receives Items from each of the specified input Streams, converts them using the onReceived / onReceivedAsync functions, and is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(IEnumerable<ITrakHoundStream<TInput>> streams, CancellationToken cancellationToken, string streamId = null, bool allowNull = false, Func<TInput, TOutput> onReceived = null, Func<TInput, Task<TOutput>> onReceivedAsync = null)
            : this(streamId, cancellationToken, allowNull)
        {
            OnInputReceived = onReceived;
            OnInputReceivedAsync = onReceivedAsync;

            if (streams != null)
            {
                foreach (var stream in streams)
                {
                    if (stream != null) AddInput(stream);
                }
            }
        }

        /// <summary>
        /// Creates a new Stream that forwards the output of each of the specified input/output Streams
        /// </summary>
        public TrakHoundStream(IEnumerable<ITrakHoundStream<TInput, TOutput>> streams, string streamId = null, bool allowNull = false) : this(streams, CancellationToken.None, streamId, allowNull) { }

        /// <summary>
        /// Creates a new Stream that forwards the output of each of the specified input/output Streams and is also cancelled by the specified CancellationToken
        /// </summary>
        public TrakHoundStream(IEnumerable<ITrakHoundStream<TInput, TOutput>> streams, CancellationToken cancellationToken, string streamId = null, bool allowNull = false) : this(streamId, cancellationToken, allowNull)
        {
            if (streams != null)
            {
                foreach (var stream in streams)
                {
                    if (stream != null) AddInput(stream);
                }
            }
        }

        /// <summary>
        /// Creates a new Stream that receives batches of Items from the specified Stream and converts each Item individually
        /// </summary>
        public TrakHoundStream(ITrakHoundStream<IEnumerable<TInput>> stream, string streamId = null, bool allowNull = false) : this(streamId, CancellationToken.None, allowNull)
        {
            if (stream != null) AddInput(stream);
        }


        /// <summary>
        /// Cancels the Stream, calls OnDisposed, disposes the input Streams (unless LeaveInputsOpen is set), and raises the Disposed event
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            TrakHoundStreamHandlers.Run(_cancellationTokenSource.Cancel, RaiseError);
            _inputQueue.Complete();

            TrakHoundStreamHandlers.Run(OnDisposed, RaiseError);

            Input[] inputs;
            lock (_lock)
            {
                inputs = _inputs.ToArray();
                _inputs.Clear();
            }

            foreach (var input in inputs)
            {
                // Unsubscribe first so the input's Disposed event doesn't call back into this Stream
                input.Unsubscribe();

                if (!LeaveInputsOpen) TrakHoundStreamHandlers.Run(input.Stream.Dispose, RaiseError);
            }

            _cancellationTokenSource.Dispose();

            TrakHoundStreamHandlers.Run(() => Disposed?.Invoke(this, Id), RaiseError);
        }

        /// <summary>
        /// Calls OnStart and starts each input Stream
        /// </summary>
        public void Start()
        {
            if (_disposed != 0) return;

            OnStart();

            foreach (var input in GetInputs()) input.Stream.Start();
        }

        /// <summary>
        /// Calls OnStop and stops each input Stream
        /// </summary>
        public void Stop()
        {
            if (_disposed != 0) return;

            OnStop();

            foreach (var input in GetInputs()) input.Stream.Stop();
        }

        /// <summary>
        /// Called when the Stream is started (override to add start logic)
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// Called when the Stream is stopped (override to add stop logic)
        /// </summary>
        protected virtual void OnStop() { }


        /// <summary>
        /// Converts the Item using OnInputReceivedAsync (or OnInputReceived) and forwards the result to the Stream's subscribers, as if it came from an input Stream. Returns false when the Item was dropped
        /// </summary>
        public virtual Task<bool> PushInput(TInput item)
        {
            return ReceiveInput(item);
        }

        /// <summary>
        /// Sends the Item back to the input Streams (ex. to send it over the connection a Stream wraps). TInput Streams get it converted with OnPushAsync (or OnPush). Returns false when no input Stream accepted it
        /// </summary>
        public virtual async Task<bool> Push(TOutput item)
        {
            if (_disposed != 0 || (item == null && !_allowNull)) return false;

            var accepted = false;
            foreach (var input in GetInputs())
            {
                if (await input.Push(item)) accepted = true;
            }

            return accepted;
        }

        /// <summary>
        /// Forwards the Item to OnReceived, OnReceivedAsync and the Received event without converting it. Returns false when the Stream is disposed or the Item is null and null is not allowed
        /// </summary>
        protected async Task<bool> Emit(TOutput item)
        {
            if (_disposed != 0 || (item == null && !_allowNull)) return false;

            await TrakHoundStreamHandlers.Deliver(this, item, OnReceived, OnReceivedAsync, Received, RaiseError);
            return true;
        }

        /// <summary>
        /// Raises the Error event (an exception thrown by an Error subscriber is ignored)
        /// </summary>
        protected void RaiseError(Exception exception)
        {
            try { Error?.Invoke(this, exception); }
            catch { }
        }


        private sealed class Input
        {
            public ITrakHoundStream Stream { get; init; }

            public Action Unsubscribe { get; init; }

            public Func<TOutput, Task<bool>> Push { get; init; }
        }

        private void AddInput(ITrakHoundStream<TInput> stream)
        {
            EventHandler<TInput> received = (s, item) => _inputQueue.Add(() => ReceiveInput(item));
            stream.Received += received;

            AddInput(stream, () => stream.Received -= received, async item =>
            {
                var (converted, input) = await ConvertToInput(item);
                return converted && await stream.Push(input);
            });
        }

        private void AddInput(ITrakHoundStream<TInput, TOutput> stream)
        {
            EventHandler<TOutput> received = (s, item) => _inputQueue.Add(() => Emit(item));
            stream.Received += received;

            AddInput(stream, () => stream.Received -= received, stream.Push);
        }

        private void AddInput(ITrakHoundStream<IEnumerable<TInput>> stream)
        {
            EventHandler<IEnumerable<TInput>> received = (s, items) =>
            {
                if (items != null)
                {
                    foreach (var item in items) _inputQueue.Add(() => ReceiveInput(item));
                }
            };
            stream.Received += received;

            AddInput(stream, () => stream.Received -= received, async item =>
            {
                var (converted, input) = await ConvertToInput(item);
                return converted && await stream.Push(new[] { input });
            });
        }

        private void AddInput(ITrakHoundStream stream, Action unsubscribe, Func<TOutput, Task<bool>> push)
        {
            stream.Disposed += InputDisposed;

            var input = new Input
            {
                Stream = stream,
                Unsubscribe = () =>
                {
                    unsubscribe();
                    stream.Disposed -= InputDisposed;
                },
                Push = push
            };

            lock (_lock) _inputs.Add(input);
        }

        private Input[] GetInputs()
        {
            lock (_lock) return _inputs.ToArray();
        }

        private void InputDisposed(object sender, string streamId)
        {
            Input input = null;
            bool last;

            lock (_lock)
            {
                var index = _inputs.FindIndex(o => ReferenceEquals(o.Stream, sender));
                if (index < 0) index = _inputs.FindIndex(o => o.Stream.Id == streamId);
                if (index < 0) return;

                input = _inputs[index];
                _inputs.RemoveAt(index);
                last = _inputs.Count == 0;
            }

            input.Unsubscribe();

            if (last) Dispose();
        }

        private async Task<bool> ReceiveInput(TInput item)
        {
            if (_disposed != 0) return false;

            TOutput output;
            if (OnInputReceivedAsync != null)
            {
                var task = OnInputReceivedAsync(item);
                if (task == null) return false;
                output = await task;
            }
            else if (OnInputReceived != null)
            {
                output = OnInputReceived(item);
            }
            else if (item is TOutput o)
            {
                output = o;
            }
            else
            {
                return false;
            }

            return await Emit(output);
        }

        private async Task<(bool Converted, TInput Input)> ConvertToInput(TOutput item)
        {
            if (OnPushAsync != null)
            {
                var task = OnPushAsync(item);
                if (task == null) return (false, default);
                return (true, await task);
            }

            if (OnPush != null) return (true, OnPush(item));

            if (item is TInput input) return (true, input);

            return (false, default);
        }
    }
}
