// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// A Stream that receives and forwards Items, optionally from one or more input Streams
    /// </summary>
    public interface ITrakHoundStream : IDisposable
    {
        /// <summary>
        /// The unique Identifier of the Stream
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The CancellationToken that is cancelled when the Stream is disposed (or the token supplied when the Stream was created)
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Action that is called when the Stream is disposed
        /// </summary>
        Action OnDisposed { get; }


        /// <summary>
        /// Raised with the Stream Id when the Stream is disposed
        /// </summary>
        event EventHandler<string> Disposed;


        /// <summary>
        /// Starts the Stream and any input Streams
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the Stream and any input Streams
        /// </summary>
        void Stop();
    }

    /// <summary>
    /// A Stream that outputs Items of Type T
    /// </summary>
    public interface ITrakHoundStream<T> : ITrakHoundStream
    {
        /// <summary>
        /// The Type of the Items output by the Stream
        /// </summary>
        Type OutputType { get; }

        /// <summary>
        /// Action that is called for each Item received by the Stream
        /// </summary>
        Action<T> OnReceived { get; set; }

        /// <summary>
        /// Async function that is awaited for each Item received by the Stream
        /// </summary>
        Func<T,Task> OnReceivedAsync { get; set; }

        /// <summary>
        /// Event handler for when a new <typeparamref name="T"/> is received from the stream
        /// </summary>
        event EventHandler<T> Received;

        /// <summary>
        /// Pushes an Item into the Stream. A Stream that wraps a connection or input Streams sends the Item to them; a standalone Stream forwards it to its subscribers
        /// </summary>
        Task<bool> Push(T value);
    }

    /// <summary>
    /// A Stream that converts Items of Type TInput into Items of Type TOutput
    /// </summary>
    public interface ITrakHoundStream<TInput, TOutput> : ITrakHoundStream<TOutput>
    {
        /// <summary>
        /// Function used to convert each received TInput into a TOutput
        /// </summary>
        Func<TInput, TOutput> OnInputReceived { get; set; }

        /// <summary>
        /// Async function used to convert each received TInput into a TOutput
        /// </summary>
        Func<TInput, Task<TOutput>> OnInputReceivedAsync { get; set; }
    }
}
