// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace TrakHound
{
    public static class TrakHoundStreamExtensions
    {
        /// <summary>
        /// Reads the Items the Stream receives, in order, from now until the Stream is disposed or its CancellationToken is cancelled. Cancelling cancellationToken stops reading with an OperationCanceledException
        /// </summary>
        /// <example>
        /// <code>
        /// await foreach (var item in stream.ReadAllAsync(cancellationToken)) { ... }
        /// </code>
        /// </example>
        public static async IAsyncEnumerable<T> ReadAllAsync<T>(this ITrakHoundStream<T> stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (stream == null) yield break;

            var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });

            EventHandler<T> received = (s, item) => channel.Writer.TryWrite(item);
            EventHandler<string> disposed = (s, id) => channel.Writer.TryComplete();

            stream.Received += received;
            stream.Disposed += disposed;

            // Also covers a Stream that was already disposed before the handlers were added
            using var registration = stream.CancellationToken.Register(() => channel.Writer.TryComplete());

            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return item;
                }
            }
            finally
            {
                stream.Received -= received;
                stream.Disposed -= disposed;
            }
        }
    }
}
