// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Instance.Apps
{
    public enum TrakHoundAppActivityStatus
    {
        /// <summary>
        /// The page hasn't reported its Activity (ex. it was loaded before Activity was reported, or is still connecting)
        /// </summary>
        Unknown,

        /// <summary>
        /// A text field (or other editable element) has focus
        /// </summary>
        Editing,

        /// <summary>
        /// The page has unsaved changes (declared by the App, or input has been typed when the App doesn't declare it)
        /// </summary>
        Unsaved,

        /// <summary>
        /// The user interacted with the page recently
        /// </summary>
        Active,

        /// <summary>
        /// No interaction for at least <see cref="TrakHoundAppConnectionActivity.IdleTime"/>
        /// </summary>
        Idle,

        /// <summary>
        /// The page is in a Background tab (or a minimized window)
        /// </summary>
        Background
    }

    /// <summary>
    /// Whether an App Connection's page is in use, and so whether it is safe to reload it
    /// </summary>
    public static class TrakHoundAppConnectionActivity
    {
        /// <summary>
        /// How long without any interaction before a page is Idle (safe to reload)
        /// </summary>
        public static readonly TimeSpan IdleTime = TimeSpan.FromMinutes(5);


        public static TrakHoundAppActivityStatus GetStatus(TrakHoundAppConnection connection, DateTime now)
        {
            if (connection?.LastActivity == null) return TrakHoundAppActivityStatus.Unknown;
            if (connection.IsEditing) return TrakHoundAppActivityStatus.Editing;
            if (HasUnsavedChanges(connection)) return TrakHoundAppActivityStatus.Unsaved;
            if (connection.IsHidden) return TrakHoundAppActivityStatus.Background;
            return GetIdleTime(connection, now) >= IdleTime ? TrakHoundAppActivityStatus.Idle : TrakHoundAppActivityStatus.Active;
        }

        /// <summary>
        /// Nothing is lost by reloading the page and no one is using it (Idle, or in a Background tab without unsaved changes)
        /// </summary>
        public static bool IsSafeToReload(TrakHoundAppConnection connection, DateTime now)
        {
            var status = GetStatus(connection, now);
            return status == TrakHoundAppActivityStatus.Idle || status == TrakHoundAppActivityStatus.Background;
        }

        /// <summary>
        /// Declared by the App, or (when the App doesn't declare it) whether any input has been typed on the page
        /// </summary>
        public static bool HasUnsavedChanges(TrakHoundAppConnection connection) => connection?.HasUnsavedChanges ?? connection?.HasInput ?? false;

        public static TimeSpan GetIdleTime(TrakHoundAppConnection connection, DateTime now)
        {
            if (connection?.LastActivity == null) return TimeSpan.Zero;
            var idle = now - connection.LastActivity.Value;
            return idle > TimeSpan.Zero ? idle : TimeSpan.Zero;
        }

        /// <summary>
        /// ex. "Idle · 14m", "Editing · used 12s ago"
        /// </summary>
        public static string GetDescription(TrakHoundAppConnection connection, DateTime now)
        {
            var status = GetStatus(connection, now);
            var idle = FormatTime(GetIdleTime(connection, now));

            switch (status)
            {
                case TrakHoundAppActivityStatus.Editing: return $"Editing · used {idle} ago";
                case TrakHoundAppActivityStatus.Unsaved: return $"Unsaved · used {idle} ago";
                case TrakHoundAppActivityStatus.Active: return $"Active · {idle} ago";
                case TrakHoundAppActivityStatus.Idle: return $"Idle · {idle}";
                case TrakHoundAppActivityStatus.Background: return $"Background · idle {idle}";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// The details behind the Status (for a Tooltip)
        /// </summary>
        public static string GetDetails(TrakHoundAppConnection connection, DateTime now)
        {
            if (connection?.LastActivity == null) return "The page hasn't reported its activity (it may have been loaded before activity was reported, or is still connecting). Its state is unknown, so it isn't treated as safe to reload";

            var lines = new List<string>();
            lines.Add($"Last interaction : {connection.LastActivity.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({FormatTime(GetIdleTime(connection, now))} ago)");
            if (connection.IsEditing) lines.Add("A text field has focus");
            if (connection.HasUnsavedChanges.HasValue) lines.Add(connection.HasUnsavedChanges.Value ? "The App reports unsaved changes" : "The App reports no unsaved changes");
            else if (connection.HasInput) lines.Add("Input has been typed on the page (it may not be saved)");
            if (connection.IsHidden) lines.Add("In a Background tab");
            lines.Add(IsSafeToReload(connection, now) ? "Safe to reload" : $"Not safe to reload (safe once idle for {FormatTime(IdleTime)} without unsaved changes)");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// A short description of how the pages are being used (ex. "2 idle, 1 in use (1 with unsaved changes)")
        /// </summary>
        public static string Summarize(IEnumerable<TrakHoundAppConnection> connections, DateTime now)
        {
            var statuses = connections?.Select(o => GetStatus(o, now)).ToList() ?? new List<TrakHoundAppActivityStatus>();
            if (statuses.Count == 0) return null;

            var safe = statuses.Count(o => o == TrakHoundAppActivityStatus.Idle || o == TrakHoundAppActivityStatus.Background);
            var unsaved = statuses.Count(o => o == TrakHoundAppActivityStatus.Editing || o == TrakHoundAppActivityStatus.Unsaved);
            var active = statuses.Count(o => o == TrakHoundAppActivityStatus.Active);
            var unknown = statuses.Count(o => o == TrakHoundAppActivityStatus.Unknown);

            var parts = new List<string>();
            parts.Add($"{safe:N0} idle");
            if (active + unsaved > 0) parts.Add($"{active + unsaved:N0} in use{(unsaved > 0 ? $" ({unsaved:N0} with unsaved changes)" : "")}");
            if (unknown > 0) parts.Add($"{unknown:N0} unknown");
            return string.Join(", ", parts);
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalSeconds < 60) return $"{Math.Max(0, (int)time.TotalSeconds)}s";
            if (time.TotalMinutes < 60) return $"{(int)time.TotalMinutes}m";
            if (time.TotalHours < 24) return $"{(int)time.TotalHours}h {time.Minutes}m";
            return $"{(int)time.TotalDays}d {time.Hours}h";
        }
    }
}
