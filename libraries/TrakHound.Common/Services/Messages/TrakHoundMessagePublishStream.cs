// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// A Stream that publishes each Item pushed to it as a Message, one at a time. Push returns false and raises PublishFailed when an Item isn't published
    /// </summary>
    public class TrakHoundMessagePublishStream<T> : TrakHoundStream<T>, ITrakHoundMessagePublishStream<T>
    {
        private readonly Func<T, CancellationToken, Task<TrakHoundMessagePublishFailure>> _publish;
        private readonly SemaphoreSlim _publishLock = new SemaphoreSlim(1, 1);


        /// <inheritdoc/>
        public event EventHandler<TrakHoundMessagePublishFailure> PublishFailed;


        /// <summary>
        /// Creates a Publish Stream that publishes each Item with the publish function (which returns null when the Item was published)
        /// </summary>
        public TrakHoundMessagePublishStream(Func<T, CancellationToken, Task<TrakHoundMessagePublishFailure>> publish, CancellationToken cancellationToken = default)
            : base((string)null, cancellationToken)
        {
            _publish = publish;
        }


        /// <summary>
        /// Publishes the Item. Returns false (and raises PublishFailed) when it wasn't published
        /// </summary>
        public override async Task<bool> Push(T item)
        {
            if (CancellationToken.IsCancellationRequested) return false;

            TrakHoundMessagePublishFailure failure;

            // One at a time so Messages are published in the order they were pushed
            try
            {
                await _publishLock.WaitAsync(CancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                failure = await Publish(item, CancellationToken);
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                failure = new TrakHoundMessagePublishFailure(null, 500, ex.Message);
            }
            finally
            {
                _publishLock.Release();
            }

            if (failure != null)
            {
                RaisePublishFailed(failure);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Publishes the Item, returning null when it was published
        /// </summary>
        protected virtual Task<TrakHoundMessagePublishFailure> Publish(T item, CancellationToken cancellationToken)
        {
            return _publish != null ? _publish(item, cancellationToken) : Task.FromResult(new TrakHoundMessagePublishFailure(null, 501, "No Publish Function"));
        }

        /// <summary>
        /// Raises PublishFailed (an exception thrown by a subscriber is raised on the Error event)
        /// </summary>
        protected void RaisePublishFailed(TrakHoundMessagePublishFailure failure)
        {
            try
            {
                PublishFailed?.Invoke(this, failure);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }
    }
}
