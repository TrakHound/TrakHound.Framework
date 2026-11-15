// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Health
{
    /// <summary>
    /// Evaluates the Health of one area of the Instance (ex. Workers). Called periodically by the <see cref="TrakHoundHealthMonitor"/>.
    /// </summary>
    public interface ITrakHoundHealthCheck
    {
        string Id { get; }

        string Title { get; }

        /// <summary>
        /// Evaluate the current Health. Should be quick and must not depend on the previous result (the Monitor handles flapping).
        /// </summary>
        Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context);
    }

    public class TrakHoundHealthContext
    {
        public ITrakHoundInstance Instance { get; init; }

        /// <summary>
        /// The time (UTC) of this evaluation
        /// </summary>
        public DateTime Now { get; init; }

        public TrakHoundHealthCheckResult Result(ITrakHoundHealthCheck check, TrakHoundHealthStatus status, string description, string link = null, IEnumerable<TrakHoundHealthCheckItem> items = null)
        {
            return new TrakHoundHealthCheckResult
            {
                Id = check.Id,
                Title = check.Title,
                Status = status,
                Description = description,
                Link = link,
                Items = items?.ToList()
            };
        }
    }
}
