// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Volumes;

namespace TrakHound.Packages.App
{
    public enum PackageNotificationType
    {
        Success,
        Warning,
        Error
    }

    public class PackageNotification
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public PackageNotificationType Type { get; set; }

        public string Title { get; set; }

        public string Message { get; set; }
    }

    public enum OverwriteDecision
    {
        Overwrite,
        Skip,
        Cancel
    }

    /// <summary>
    /// A pending question to the user : a Package Version already exists on the Target Server
    /// </summary>
    public class OverwriteRequest
    {
        public string PackageId { get; set; }

        public string PackageVersion { get; set; }

        /// <summary>
        /// The Package being uploaded (null if its information isn't known)
        /// </summary>
        public TrakHoundPackage Incoming { get; set; }

        /// <summary>
        /// The Package that already exists on the Target Server
        /// </summary>
        public TrakHoundPackage Existing { get; set; }

        public PackageServer Target { get; set; }

        /// <summary>
        /// More Packages follow in the same batch (offer "Apply to the remaining Packages")
        /// </summary>
        public bool HasRemaining { get; set; }

        internal TaskCompletionSource<(OverwriteDecision Decision, bool ApplyToRemaining)> Completion { get; } = new TaskCompletionSource<(OverwriteDecision, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// The Overwrite decision for a batch of Uploads (set when the user chooses "Apply to the remaining Packages")
    /// </summary>
    public class OverwriteBatch
    {
        public OverwriteDecision? Decision { get; set; }

        public bool Cancelled { get; set; }
    }

    public enum TransferStatus
    {
        Pending,
        Checking,
        Waiting,
        Downloading,
        Uploading,
        Completed,
        Skipped,
        Failed
    }

    public class TransferResult
    {
        public TransferStatus Status { get; set; }

        public string Message { get; set; }

        public bool Overwritten { get; set; }


        public TransferResult(TransferStatus status, string message = null)
        {
            Status = status;
            Message = message;
        }
    }


    /// <summary>
    /// State shared by the Packages App tabs : the saved Package Servers, notifications and Overwrite questions
    /// </summary>
    public class PackagesAppContext
    {
        public const string ServersFilename = "servers.json";
        public static readonly string[] Categories = new[] { "bundle", "app", "api", "function", "worker", "service", "identity" };

        private readonly object _lock = new object();
        private ITrakHoundVolume _volume;
        private List<PackageServer> _servers = new List<PackageServer>();


        public IReadOnlyList<PackageServer> Servers { get { lock (_lock) return _servers.ToList(); } }

        public bool IsLoaded { get; private set; }

        public List<PackageNotification> Notifications { get; } = new List<PackageNotification>();

        /// <summary>
        /// The Overwrite question shown to the user (null if none)
        /// </summary>
        public OverwriteRequest PendingOverwrite { get; private set; }

        /// <summary>
        /// The Server last selected on the Packages tab (kept when switching tabs)
        /// </summary>
        public string BrowseServerId { get; set; }

        /// <summary>
        /// The Server last selected as an Upload or Copy Target (kept when switching tabs)
        /// </summary>
        public string TargetServerId { get; set; }

        /// <summary>
        /// Raised when the Servers, Notifications or Overwrite question change (the page re-renders)
        /// </summary>
        public event Action Changed;


        #region "Servers"

        /// <summary>
        /// Read the saved Servers from the App's Volume
        /// </summary>
        public async Task Load(ITrakHoundVolume volume)
        {
            _volume = volume;

            List<PackageServer> servers = null;
            if (_volume != null)
            {
                try
                {
                    servers = (await _volume.ReadJson<List<PackageServer>>(ServersFilename))?.Where(o => o != null && !string.IsNullOrEmpty(o.Id)).ToList();
                }
                catch (Exception ex)
                {
                    Notify(PackageNotificationType.Error, "Error Reading Servers", ex.Message);
                }
            }
            else
            {
                Notify(PackageNotificationType.Warning, "No Volume Available", "Servers can be added but will not be saved");
            }

            lock (_lock) _servers = servers ?? new List<PackageServer>();
            IsLoaded = true;
            NotifyChanged();
        }

        public PackageServer GetServer(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            lock (_lock) return _servers.FirstOrDefault(o => o.Id == serverId);
        }

        /// <summary>
        /// Replace the saved Servers and write them to the App's Volume
        /// </summary>
        public async Task<bool> SaveServers(IEnumerable<PackageServer> servers)
        {
            var updated = servers?.ToList() ?? new List<PackageServer>();

            if (_volume != null)
            {
                try
                {
                    if (!await _volume.WriteJson(ServersFilename, updated))
                    {
                        Notify(PackageNotificationType.Error, "Error Saving Servers", $"Could not write {ServersFilename} to the App Volume");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Notify(PackageNotificationType.Error, "Error Saving Servers", ex.Message);
                    return false;
                }
            }

            lock (_lock) _servers = updated;

            if (GetServer(BrowseServerId) == null) BrowseServerId = null;
            if (GetServer(TargetServerId) == null) TargetServerId = null;

            NotifyChanged();
            return true;
        }

        public static TrakHoundPackageClient CreateClient(PackageServer server)
        {
            if (server == null || string.IsNullOrEmpty(server.BaseUrl)) return null;

            var client = new TrakHoundPackageClient(server.BaseUrl, server.Organization);
            if (!string.IsNullOrEmpty(server.AccessToken)) client.SetToken(server.AccessToken);

            return client;
        }

        /// <summary>
        /// Query the latest Version of each Package on a Server
        /// </summary>
        /// <returns>The Packages (null if the Server couldn't be reached) and an error description</returns>
        public static async Task<(List<TrakHoundPackage> Packages, string Error)> QueryLatest(TrakHoundPackageClient client)
        {
            if (client == null) return (null, "No Server selected");
            if (string.IsNullOrEmpty(client.Organization)) return (null, "The Server has no Organization");

            try
            {
                var packages = await client.QueryLatestByOrganization();
                if (packages == null) return (null, $"No response from {client.BaseUrl} (the Server is unreachable, or the Organization has no Packages)");

                return (packages.OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase).ToList(), null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// List the Versions of a Package on a Server, newest first
        /// </summary>
        public static async Task<List<string>> GetVersions(TrakHoundPackageClient client, string packageId)
        {
            if (client == null || string.IsNullOrEmpty(packageId)) return null;

            try
            {
                var versions = await client.GetVersions(packageId);
                return versions?.Where(o => !string.IsNullOrEmpty(o)).Distinct().OrderByDescending(o => o.ToVersion()).ToList();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region "Transfer"

        /// <summary>
        /// Upload a Package Version to a Target Server. If the Version already exists on the Target, the user is asked whether to Overwrite it.
        /// </summary>
        /// <param name="getBytes">Returns the Package file contents (called only if the Package is uploaded)</param>
        /// <param name="incoming">Information for the Package being uploaded (shown when asking to Overwrite)</param>
        /// <param name="hasRemaining">More Packages follow in the same batch</param>
        /// <param name="onStatus">Called as the transfer progresses</param>
        public async Task<TransferResult> Transfer(
            PackageServer target,
            TrakHoundPackageClient targetClient,
            string packageId,
            string packageVersion,
            Func<Task<byte[]>> getBytes,
            OverwriteBatch batch,
            TrakHoundPackage incoming = null,
            bool hasRemaining = false,
            Func<TransferStatus, Task> onStatus = null
            )
        {
            if (batch?.Cancelled == true) return new TransferResult(TransferStatus.Skipped, "Cancelled");
            if (targetClient == null) return new TransferResult(TransferStatus.Failed, "No Target Server selected");

            try
            {
                // Check if the Version already exists on the Target
                if (onStatus != null) await onStatus(TransferStatus.Checking);
                var existing = await targetClient.Get(packageId, packageVersion);

                var overwrite = false;
                if (existing != null)
                {
                    var decision = batch?.Decision;
                    if (decision == null)
                    {
                        if (onStatus != null) await onStatus(TransferStatus.Waiting);

                        var answer = await ConfirmOverwrite(new OverwriteRequest
                        {
                            PackageId = packageId,
                            PackageVersion = packageVersion,
                            Incoming = incoming,
                            Existing = existing,
                            Target = target,
                            HasRemaining = hasRemaining
                        });

                        decision = answer.Decision;
                        if (answer.ApplyToRemaining && batch != null && decision != OverwriteDecision.Cancel) batch.Decision = decision;
                    }

                    switch (decision)
                    {
                        case OverwriteDecision.Cancel:
                            if (batch != null) batch.Cancelled = true;
                            return new TransferResult(TransferStatus.Skipped, "Cancelled");

                        case OverwriteDecision.Skip:
                            return new TransferResult(TransferStatus.Skipped, $"v{packageVersion} already exists on {target?.DisplayName}");
                    }

                    overwrite = true;
                }

                if (onStatus != null) await onStatus(TransferStatus.Downloading);
                var bytes = await getBytes();
                if (bytes.IsNullOrEmpty()) return new TransferResult(TransferStatus.Failed, $"Could not download {packageId} v{packageVersion} from the source Server");

                if (onStatus != null) await onStatus(TransferStatus.Uploading);
                var uploaded = await targetClient.Upload(bytes);
                if (uploaded == null) return new TransferResult(TransferStatus.Failed, $"Upload to {target?.DisplayName} failed (the Server is unreachable or rejected the Package)");

                return new TransferResult(TransferStatus.Completed, overwrite ? "Overwrote the existing Version" : null) { Overwritten = overwrite };
            }
            catch (Exception ex)
            {
                return new TransferResult(TransferStatus.Failed, ex.Message);
            }
        }

        /// <summary>
        /// Ask the user whether to Overwrite a Package Version that already exists on the Target Server
        /// </summary>
        public Task<(OverwriteDecision Decision, bool ApplyToRemaining)> ConfirmOverwrite(OverwriteRequest request)
        {
            lock (_lock)
            {
                // Only one question at a time (a previous unanswered question is Skipped)
                PendingOverwrite?.Completion.TrySetResult((OverwriteDecision.Skip, false));
                PendingOverwrite = request;
            }

            NotifyChanged();
            return request.Completion.Task;
        }

        public void ResolveOverwrite(OverwriteDecision decision, bool applyToRemaining)
        {
            OverwriteRequest request;
            lock (_lock)
            {
                request = PendingOverwrite;
                PendingOverwrite = null;
            }

            request?.Completion.TrySetResult((decision, applyToRemaining));
            NotifyChanged();
        }

        #endregion

        #region "Notifications"

        public void Notify(PackageNotificationType type, string title, string message = null)
        {
            var notification = new PackageNotification { Type = type, Title = title, Message = message };
            lock (Notifications) Notifications.Add(notification);
            NotifyChanged();

            // Success notifications close automatically (Warnings and Errors stay until closed)
            if (type == PackageNotificationType.Success)
            {
                _ = Task.Delay(6000).ContinueWith(_ => Dismiss(notification.Id));
            }
        }

        public void Dismiss(string notificationId)
        {
            lock (Notifications) Notifications.RemoveAll(o => o.Id == notificationId);
            NotifyChanged();
        }

        public void NotifyChanged() => Changed?.Invoke();

        #endregion


        public static string GetFilename(string packageId, string packageVersion) => $"{packageId}-{packageVersion}{TrakHoundPackage.FileExtension}";

        public static string GetDescription(TrakHoundPackage package)
        {
            if (package?.Metadata == null) return null;
            var entry = package.Metadata.FirstOrDefault(o => string.Equals(o.Key, TrakHoundPackage.DescriptionName, StringComparison.OrdinalIgnoreCase));
            return entry.Value?.ToString();
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:0.00} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }

        public static string FormatBuildDate(TrakHoundPackage package)
        {
            if (package == null || package.BuildDate <= DateTime.MinValue) return null;
            return package.BuildDate.ToLocalTime().ToString("g");
        }
    }
}
