// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Functions;
using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Packages that fail because they were built against a different (incompatible) version of the TrakHound libraries,
    /// ex. "Method not found". These usually need the Package to be rebuilt (or updated) for this version of the Instance.
    /// Also reports Package Dependencies whose installed versions don't satisfy them (ex. below the Dependency's Minimum Version).
    /// </summary>
    public class PackagesHealthCheck : ITrakHoundHealthCheck
    {
        private const string _packagesLink = "_admin/packages"; // Not an Explorer page

        private readonly TrakHoundPackageCompatibilityTracker _tracker;


        public string Id => "packages";

        public string Title => "Packages";


        public PackagesHealthCheck(TrakHoundPackageCompatibilityTracker tracker)
        {
            _tracker = tracker;
        }


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            // Function errors are returned in the Run result (not logged), so read them from the Function Metrics
            ReportFunctionErrors(context);

            var items = new List<TrakHoundHealthCheckItem>();

            var incidents = _tracker.GetIncidents(context.Now) ?? Enumerable.Empty<TrakHoundPackageCompatibilityTracker.Incident>();
            foreach (var incident in incidents)
            {
                var description = $"Not compatible with this Instance (v{context.Instance.Version})";

                // Packages may specify a version pattern (ex. *-*) instead of the version they were built with
                var builtFor = GetBuiltForVersion(context.Instance, incident.PackageId);
                if (!string.IsNullOrEmpty(builtFor) && !builtFor.Contains('*')) description += $" · built for TrakHound {builtFor}";

                description += $" · {incident.Count:N0} errors, last {incident.LastSeen.ToLocalTime():HH:mm:ss} : {incident.Message}";

                items.Add(new TrakHoundHealthCheckItem
                {
                    Id = incident.PackageId,
                    Name = incident.PackageId,
                    Status = TrakHoundHealthStatus.Degraded,
                    Description = description
                });
            }

            var incompatibleCount = items.Count;

            var unmetDependencies = GetUnmetDependencyItems(context.Instance);
            items.AddRange(unmetDependencies);

            if (items.Count < 1)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, "No compatibility errors or unmet dependencies", _packagesLink));
            }

            var summaries = new List<string>();
            if (incompatibleCount > 0) summaries.Add($"{incompatibleCount} Packages are not compatible with this Instance (rebuild or update them)");
            if (unmetDependencies.Count > 0) summaries.Add($"{unmetDependencies.Count} Package dependencies are not satisfied (update them)");

            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, string.Join(" · ", summaries), _packagesLink, items));
        }

        /// <summary>
        /// Dependencies of the latest installed Package versions whose installed versions don't satisfy them (ex. below the Minimum Version)
        /// </summary>
        private static List<TrakHoundHealthCheckItem> GetUnmetDependencyItems(ITrakHoundInstance instance)
        {
            var items = new List<TrakHoundHealthCheckItem>();

            try
            {
                var packageManager = instance.PackageManager;
                var packages = packageManager?.Packages?.ToList();
                if (packages.IsNullOrEmpty()) return items;

                foreach (var group in packages.GroupBy(o => o.Id))
                {
                    var latest = group.OrderByDescending(o => o.Version.ToVersion()).First();
                    if (latest.Dependencies.IsNullOrEmpty()) continue;

                    foreach (var dependency in latest.Dependencies)
                    {
                        var issue = packageManager.GetDependencyIssue(dependency);
                        if (issue == null) continue;

                        items.Add(new TrakHoundHealthCheckItem
                        {
                            Id = $"{latest.Id}/dependencies/{dependency.Id}",
                            Name = latest.Id,
                            Status = TrakHoundHealthStatus.Degraded,
                            Description = $"Depends on {dependency.Id} {TrakHoundPackageDependency.ToVersionString(dependency)} : {issue}"
                        });
                    }
                }
            }
            catch { }

            return items;
        }


        private void ReportFunctionErrors(TrakHoundHealthContext context)
        {
            if (!(context.Instance.Metrics?.FunctionsEnabled ?? true)) return;

            var functions = context.Instance.FunctionManager?.GetMetrics();
            if (functions.IsNullOrEmpty()) return;

            foreach (var function in functions)
            {
                if (function.LastError != null &&
                    context.Now - function.LastError.Value <= TrakHoundPackageCompatibilityTracker.Retention &&
                    TrakHoundPackageCompatibilityTracker.IsIncompatibilityError(function.LastErrorMessage))
                {
                    // Reporting the same error again only updates it (the timestamp is the error's)
                    _tracker.Report(function.PackageId, $"functions/{function.FunctionId}", function.LastErrorMessage, function.LastError.Value);
                }
            }
        }

        private static string GetBuiltForVersion(ITrakHoundInstance instance, string packageId)
        {
            try
            {
                return instance.PackageManager?.GetLatest(packageId)?.GetMetadata(TrakHoundPackage.TrakHoundVersionName);
            }
            catch
            {
                return null;
            }
        }
    }
}
