// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text;
using TrakHound.Logging;

namespace TrakHound.Instance.Apps
{
    /// <summary>
    /// Writes an Access Log entry for each completed App Page View (the Client Connection and how long the Page was viewed for).
    /// Entries are written to a Logger for each App ("access/apps/{packageId}") in Log History at the Information level
    /// </summary>
    public class TrakHoundAppAccessLog
    {
        public const string LoggerName = "access/apps";

        private readonly ITrakHoundLogProvider _logProvider;


        public TrakHoundAppAccessLog(ITrakHoundLogProvider logProvider)
        {
            _logProvider = logProvider;
        }

        public void Write(TrakHoundAppPageView view)
        {
            if (view == null) return;

            // A Logger for each App (Log History is queried by Logger Name, so this lets each App's entries be viewed on their own)
            var logger = _logProvider?.GetLoggerByName(GetLoggerName(view.PackageId ?? view.AppId));
            logger?.Log(new TrakHoundLogItem(null, TrakHoundLogLevel.Information, CreateMessage(view), timestamp: view.Ended.ToUnixTime()));
        }

        public static string GetLoggerName(string packageId) => !string.IsNullOrEmpty(packageId) ? $"{LoggerName}/{packageId}" : LoggerName;

        /// <summary>
        /// The Logs page showing the Access Log History (of every App, or of the App's Package), optionally filtered by a Search (ex. a Connection Code)
        /// </summary>
        public static string GetLink(string instanceId, string packageId = null, string search = null)
        {
            var link = $"_admin/explorer/{instanceId}/diagnostics/logs/view?pattern={Uri.EscapeDataString(GetLoggerName(packageId) + "/**")}&view=history";
            if (!string.IsNullOrEmpty(search)) link += $"&search={Uri.EscapeDataString(search)}";
            return link;
        }

        /// <summary>
        /// ex. Page View : /dashboard/line-1 (/dashboard/{id}) : Duration = 00:02:14.512 : App = Dashboard : Client = 192.168.1.20 (pc-104.local) : Code = K7QF : Connection = 0HNA1... : Agent = "Mozilla/5.0 ..."
        /// </summary>
        public static string CreateMessage(TrakHoundAppPageView view)
        {
            var connection = view.Connection;

            var message = new StringBuilder();
            message.Append("Page View : ");
            message.Append(!string.IsNullOrEmpty(view.Path) ? view.Path : "/");
            if (!string.IsNullOrEmpty(view.Route) && view.Route != view.Path) message.Append($" ({view.Route})");
            message.Append($" : Duration = {FormatDuration(view.Duration)}");
            message.Append($" : App = {GetAppName(view)}");

            message.Append($" : Client = {(!string.IsNullOrEmpty(connection?.ClientAddress) ? connection.ClientAddress : "Unknown")}");
            if (!string.IsNullOrEmpty(connection?.ClientHostname) && connection.ClientHostname != connection.ClientAddress) message.Append($" ({connection.ClientHostname})");
            if (!string.IsNullOrEmpty(connection?.Code)) message.Append($" : Code = {connection.Code}");
            if (!string.IsNullOrEmpty(connection?.ConnectionId)) message.Append($" : Connection = {connection.ConnectionId}");
            if (!string.IsNullOrEmpty(connection?.ClientAgent)) message.Append($" : Agent = \"{connection.ClientAgent}\"");

            return message.ToString();
        }

        private static string GetAppName(TrakHoundAppPageView view)
        {
            if (!string.IsNullOrEmpty(view.AppName)) return view.AppName;
            if (!string.IsNullOrEmpty(view.PackageId)) return view.PackageId;
            return view.AppId;
        }

        private static string FormatDuration(double milliseconds)
        {
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return duration.TotalDays >= 1 ? duration.ToString(@"d\.hh\:mm\:ss\.fff") : duration.ToString(@"hh\:mm\:ss\.fff");
        }
    }
}
