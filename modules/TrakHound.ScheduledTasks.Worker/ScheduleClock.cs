// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// The time source for the Scheduled Task Engines (replaced in tests)
    /// </summary>
    internal interface IScheduleClock
    {
        DateTime UtcNow { get; }

        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal class SystemScheduleClock : IScheduleClock
    {
        public static readonly SystemScheduleClock Instance = new SystemScheduleClock();

        public DateTime UtcNow => DateTime.UtcNow;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }
}
