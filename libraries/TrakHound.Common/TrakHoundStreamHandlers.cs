// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;

namespace TrakHound
{
    internal static class TrakHoundStreamHandlers
    {
        /// <summary>
        /// Calls OnReceived, OnReceivedAsync and each Received subscriber. Each is called separately so one that throws doesn't stop the rest
        /// </summary>
        public static async Task Deliver<T>(object sender, T item, Action<T> onReceived, Func<T, Task> onReceivedAsync, EventHandler<T> received, Action<Exception> onError)
        {
            if (onReceived != null)
            {
                try { onReceived(item); }
                catch (Exception ex) { onError(ex); }
            }

            if (onReceivedAsync != null)
            {
                try
                {
                    var task = onReceivedAsync(item);
                    if (task != null) await task;
                }
                catch (Exception ex) { onError(ex); }
            }

            if (received != null)
            {
                foreach (EventHandler<T> handler in received.GetInvocationList())
                {
                    try { handler(sender, item); }
                    catch (Exception ex) { onError(ex); }
                }
            }
        }

        /// <summary>
        /// Runs a Dispose step, reporting an exception instead of throwing so the remaining steps still run
        /// </summary>
        public static void Run(Action action, Action<Exception> onError)
        {
            if (action != null)
            {
                try { action(); }
                catch (Exception ex) { onError(ex); }
            }
        }
    }
}
