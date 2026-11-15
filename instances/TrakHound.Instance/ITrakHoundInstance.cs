// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.Instances
{
    public interface ITrakHoundInstance
    {
        string Id { get; }

        string Version { get; }

        /// <summary>
        /// The Configuration directory (for the Configuration Profile the Instance was started with)
        /// </summary>
        string ConfigurationPath { get; }

        string PackagesPath { get; }

        string VolumesPath { get; }

        string VariablesPath { get; }

        TrakHoundInstanceStatus Status { get; }

        DateTime LastUpdated { get; }

        DateTime CreatedTime { get; }

        DateTime? StartTime { get; }

        DateTime? StopTime { get; }


        TrakHoundInstanceInformation Information { get; }

        TrakHoundInstanceConfiguration Configuration { get; }

        /// <summary>
        /// The Metrics settings the Runtime was started with (Configuration.Metrics may have been changed since)
        /// </summary>
        TrakHoundInstanceMetricsConfiguration Metrics { get; }

        /// <summary>
        /// Evaluates the overall Health of the Instance in the background
        /// </summary>
        TrakHound.Instance.Health.TrakHoundHealthMonitor Health { get; }

        ITrakHoundConfigurationProfile ConfigurationProfile { get; }


        TrakHoundPackageManager PackageManager { get; }
        ITrakHoundModuleProvider ModuleProvider { get; }
        ITrakHoundSecurityManager SecurityManager { get; }
        ITrakHoundAppProvider AppProvider { get; }
        TrakHoundApiProvider ApiProvider { get; }
        TrakHound.Zones.TrakHoundZoneManager ZoneManager { get; }

        /// <summary>
        /// Records changes to Packages and Configuration files in the Audit Log
        /// </summary>
        TrakHound.Instance.Audit.TrakHoundAuditMonitor AuditMonitor { get; }

        /// <summary>
        /// Issues the Module Tokens that let code running outside of the Instance (ex. a worker process started by an Api) call the System API as the Module
        /// </summary>
        TrakHoundModuleTokenManager ModuleTokens { get; }
        TrakHoundFunctionManager FunctionManager { get; }
        TrakHoundWorkerManager WorkerManager { get; }
        ITrakHoundVolumeProvider VolumeProvider { get; }
        TrakHoundServicesProvider ServicesProvider { get; }
        ITrakHoundClientProvider ClientProvider { get; }
        ITrakHoundLogProvider LogProvider { get; }

        /// <summary>
        /// Metrics for the HTTP Stream connections, WebSockets and Server-Sent Events (lives for the lifetime of the Instance)
        /// </summary>
        TrakHound.Http.TrakHoundStreamMetricsCollector StreamMetrics { get; }

        /// <summary>
        /// Metrics for the MCP Server (lives for the lifetime of the Instance)
        /// </summary>
        TrakHound.Instance.Mcp.TrakHoundMcpMetricsCollector McpMetrics { get; }

        /// <summary>
        /// Metrics for the HTTP Requests (lives for the lifetime of the Instance)
        /// </summary>
        TrakHound.Http.TrakHoundHttpMetricsCollector HttpMetrics { get; }

        /// <summary>
        /// The most recent HTTP Requests (lives for the lifetime of the Instance)
        /// </summary>
        TrakHound.Http.TrakHoundHttpRequestHistory HttpRequests { get; }

        IServiceProvider ServiceProvider { get; }


        event EventHandler Starting;

        event EventHandler Started;

        event EventHandler Stopping;

        event EventHandler Stopped;

        event TrakHoundInstanceStatusHandler StatusUpdated;

        event TrakHoundInstanceLogHandler LogUpdated;


        Task Start();

        Task Stop();


        Task InstallPackage(TrakHoundPackage package, bool downloadPackage = true, TrakHoundPackageClient packageClient = null);

        void CleanPackages();

        /// <summary>
        /// Get the installed Packages that are not used by any Configuration (what CleanPackages removes)
        /// </summary>
        IEnumerable<TrakHoundPackage> GetUnusedPackages();

        /// <summary>
        /// Returns true if the Package is applied by the Configuration Profile (and is not referenced by a Configuration)
        /// </summary>
        bool IsProfilePackage(TrakHoundPackage package);

        /// <summary>
        /// Get the Configurations that use each installed Package
        /// </summary>
        IEnumerable<TrakHoundPackageUsage> GetPackageUsage();

        /// <summary>
        /// Get whether the Package can be reverted to an older installed Version (the previous Version if not specified)
        /// </summary>
        TrakHoundPackageRevert GetPackageRevert(string packageId, string targetVersion = null);

        /// <summary>
        /// Get the Revert for each older installed Version of the Package (highest first)
        /// </summary>
        IEnumerable<TrakHoundPackageRevert> GetPackageRevertOptions(string packageId);

        /// <summary>
        /// Revert the Package to an older installed Version (the previous Version if not specified) by removing the Versions above it.
        /// Configurations using a removed Version reload with the Target Version.
        /// </summary>
        TrakHoundPackageRevert RevertPackage(string packageId, string targetVersion = null);
    }
}
