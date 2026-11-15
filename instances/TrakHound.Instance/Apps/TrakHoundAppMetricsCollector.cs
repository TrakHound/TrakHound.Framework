// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Instance.Apps
{
    /// <summary>
    /// Collects App Page Views and Page Durations for each Client Connection.
    /// A Page View starts when a Connection navigates to an App Page and ends when it navigates to a different Page,
    /// leaves the App or disconnects. Totals are kept since the Instance started. Closed Connections are kept for a while
    /// (see <see cref="ClosedConnectionRetention"/> and <see cref="MaxClosedConnections"/>) so recent visits can be reviewed.
    /// </summary>
    public class TrakHoundAppMetricsCollector
    {
        public static readonly TimeSpan ClosedConnectionRetention = TimeSpan.FromHours(24);
        public const int MaxClosedConnections = 1000;

        private readonly Dictionary<string, ConnectionState> _connections = new Dictionary<string, ConnectionState>(); // Connection ID => State
        private readonly Dictionary<string, PageStats> _pages = new Dictionary<string, PageStats>(); // App ID + Route => Totals
        private readonly Dictionary<string, long> _appConnectionCounts = new Dictionary<string, long>(); // App ID => Connections that viewed the App
        private readonly object _lock = new object();


        /// <summary>
        /// Record Page Views (when false, nothing is recorded)
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Raised when a Page View ends (with how long the Page was viewed for). Raised outside of the lock
        /// </summary>
        public event Action<TrakHoundAppPageView> PageViewEnded;


        private class PageStats
        {
            public string AppId;
            public string AppName;
            public string PackageId;
            public string Route;
            public string LastPath;
            public long Views;
            public double TotalDuration;
            public double MaxDuration;
            public DateTime? LastViewed;

            public void AddDuration(double duration)
            {
                TotalDuration += duration;
                if (duration > MaxDuration) MaxDuration = duration;
            }
        }

        private class ConnectionState
        {
            public string Id;
            public TrakHoundAppConnection Connection;
            public DateTime Connected;
            public DateTime? Disconnected;
            public long Views;
            public double TotalDuration;
            public PageStats Current; // Identifies the open Page (App, Route, Path)
            public DateTime CurrentStarted;
            public readonly Dictionary<string, PageStats> Pages = new Dictionary<string, PageStats>();
            public readonly HashSet<string> ViewedApps = new HashSet<string>();
        }


        /// <summary>
        /// Record that a Connection navigated to an App Page.
        /// Navigating to the same Path again (ex. only the Query changed) continues the current Page View.
        /// </summary>
        public void PageViewed(string id, TrakHoundAppConnection connection, string appId, string appName, string packageId, string route, string path)
        {
            if (!Enabled || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(appId)) return;

            path = RemoveQuery(path);
            if (string.IsNullOrEmpty(route)) route = path ?? "/";

            var now = DateTime.UtcNow;
            TrakHoundAppPageView ended;

            lock (_lock)
            {
                var state = GetOrAddConnection(id, connection, now);

                var current = state.Current;
                if (current != null && current.AppId == appId && current.Route == route && current.LastPath == path) return;

                ended = EndPage(state, now);

                var pageKey = GetPageKey(appId, route);

                // Instance Totals
                if (!_pages.TryGetValue(pageKey, out var page))
                {
                    page = new PageStats { AppId = appId, Route = route };
                    _pages.Add(pageKey, page);
                }
                page.AppName = appName;
                page.PackageId = packageId;
                page.LastPath = path;
                page.LastViewed = now;
                page.Views++;

                if (state.ViewedApps.Add(appId))
                {
                    _appConnectionCounts[appId] = _appConnectionCounts.GetValueOrDefault(appId) + 1;
                }

                // Connection Totals
                if (!state.Pages.TryGetValue(pageKey, out var connectionPage))
                {
                    connectionPage = new PageStats { AppId = appId, Route = route };
                    state.Pages.Add(pageKey, connectionPage);
                }
                connectionPage.AppName = appName;
                connectionPage.PackageId = packageId;
                connectionPage.LastPath = path;
                connectionPage.LastViewed = now;
                connectionPage.Views++;

                state.Views++;
                state.Current = new PageStats { AppId = appId, AppName = appName, PackageId = packageId, Route = route, LastPath = path };
                state.CurrentStarted = now;
            }

            OnPageViewEnded(ended);
        }

        /// <summary>
        /// Record that a Connection left its current App Page (ex. navigated away from the App or to a Path without a Page)
        /// </summary>
        public void PageEnded(string id)
        {
            if (!Enabled || string.IsNullOrEmpty(id)) return;

            TrakHoundAppPageView ended = null;

            lock (_lock)
            {
                if (_connections.TryGetValue(id, out var state)) ended = EndPage(state, DateTime.UtcNow);
            }

            OnPageViewEnded(ended);
        }

        /// <summary>
        /// Record that a Connection closed (its current Page View ends)
        /// </summary>
        public void ConnectionClosed(string id)
        {
            if (!Enabled || string.IsNullOrEmpty(id)) return;

            var now = DateTime.UtcNow;
            TrakHoundAppPageView ended = null;

            lock (_lock)
            {
                if (_connections.TryGetValue(id, out var state))
                {
                    ended = EndPage(state, now);
                    state.Disconnected = now;
                }

                PruneClosedConnections(now);
            }

            OnPageViewEnded(ended);
        }


        public IEnumerable<TrakHoundAppMetrics> GetMetrics()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                return _pages.Values
                    .GroupBy(o => o.AppId)
                    .Select(o => CreateAppMetrics(o.Key, o, now))
                    .OrderBy(o => o.AppName ?? o.AppId)
                    .ToList();
            }
        }

        public TrakHoundAppMetrics GetMetrics(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;

            lock (_lock)
            {
                var pages = _pages.Values.Where(o => o.AppId == appId).ToList();
                return pages.Count > 0 ? CreateAppMetrics(appId, pages, DateTime.UtcNow) : null;
            }
        }

        public IEnumerable<TrakHoundAppConnectionMetrics> GetConnectionMetrics(string appId = null)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                PruneClosedConnections(now);

                return _connections.Values
                    .Where(o => appId == null || o.Pages.Values.Any(p => p.AppId == appId))
                    .Select(o => CreateConnectionMetrics(o, appId, now))
                    .OrderByDescending(o => o.IsConnected)
                    .ThenByDescending(o => o.Connected)
                    .ToList();
            }
        }


        private ConnectionState GetOrAddConnection(string id, TrakHoundAppConnection connection, DateTime now)
        {
            if (!_connections.TryGetValue(id, out var state))
            {
                state = new ConnectionState
                {
                    Id = id,
                    Connection = connection,
                    Connected = connection != null && connection.Created > DateTime.MinValue ? connection.Created : now
                };
                _connections.Add(id, state);
            }
            else if (connection != null)
            {
                state.Connection = connection;
            }

            // A closed Connection that navigates again (shouldn't happen, but don't lose the View)
            state.Disconnected = null;

            return state;
        }

        private TrakHoundAppPageView EndPage(ConnectionState state, DateTime now)
        {
            var current = state.Current;
            if (current == null) return null;

            var duration = (now - state.CurrentStarted).TotalMilliseconds;
            var pageKey = GetPageKey(current.AppId, current.Route);

            if (_pages.TryGetValue(pageKey, out var page)) page.AddDuration(duration);
            if (state.Pages.TryGetValue(pageKey, out var connectionPage)) connectionPage.AddDuration(duration);
            state.TotalDuration += duration;

            state.Current = null;

            var view = new TrakHoundAppPageView();
            view.Id = state.Id;
            view.Connection = state.Connection;
            view.AppId = current.AppId;
            view.AppName = current.AppName;
            view.PackageId = current.PackageId;
            view.Route = current.Route;
            view.Path = current.LastPath;
            view.Started = state.CurrentStarted;
            view.Ended = now;
            view.Duration = duration;
            return view;
        }

        private void OnPageViewEnded(TrakHoundAppPageView view)
        {
            if (view == null) return;

            // A failing handler (ex. logging) shouldn't break Navigation
            try { PageViewEnded?.Invoke(view); } catch { }
        }

        private void PruneClosedConnections(DateTime now)
        {
            var closed = _connections.Values.Where(o => o.Disconnected != null).OrderByDescending(o => o.Disconnected).ToList();

            // Remove expired Connections, and the oldest when there are too many
            var remove = closed.Where((o, i) => i >= MaxClosedConnections || now - o.Disconnected.Value > ClosedConnectionRetention);
            foreach (var state in remove.ToList())
            {
                _connections.Remove(state.Id);
            }
        }

        /// <summary>
        /// Open Page Views (the time since each started is included in the Durations)
        /// </summary>
        private IEnumerable<(string PageKey, double Duration)> GetOpenViews(DateTime now)
        {
            return _connections.Values
                .Where(o => o.Current != null)
                .Select(o => (GetPageKey(o.Current.AppId, o.Current.Route), (now - o.CurrentStarted).TotalMilliseconds));
        }

        private TrakHoundAppMetrics CreateAppMetrics(string appId, IEnumerable<PageStats> pages, DateTime now)
        {
            var openViews = GetOpenViews(now).ToLookup(o => o.PageKey, o => o.Duration);
            var pageMetrics = pages.Select(o => CreatePageMetrics(o, openViews[GetPageKey(o.AppId, o.Route)])).OrderBy(o => o.Route).ToList();
            var latest = pages.OrderByDescending(o => o.LastViewed).First();

            var metrics = new TrakHoundAppMetrics();
            metrics.AppId = appId;
            metrics.AppName = latest.AppName;
            metrics.PackageId = latest.PackageId;
            metrics.TotalPageViews = pageMetrics.Sum(o => o.TotalViews);
            metrics.ActivePageViews = pageMetrics.Sum(o => o.ActiveViews);
            metrics.TotalConnections = _appConnectionCounts.GetValueOrDefault(appId);
            metrics.TotalPageDuration = pageMetrics.Sum(o => o.TotalDuration);
            metrics.AveragePageDuration = metrics.TotalPageViews > 0 ? metrics.TotalPageDuration / metrics.TotalPageViews : 0;
            metrics.MaxPageDuration = pageMetrics.Count > 0 ? pageMetrics.Max(o => o.MaxDuration) : 0;
            metrics.LastViewed = latest.LastViewed;
            metrics.Pages = pageMetrics;
            return metrics;
        }

        private TrakHoundAppConnectionMetrics CreateConnectionMetrics(ConnectionState state, string appId, DateTime now)
        {
            var current = state.Current;
            var currentDuration = current != null ? (now - state.CurrentStarted).TotalMilliseconds : 0;
            var currentPageKey = current != null ? GetPageKey(current.AppId, current.Route) : null;

            var metrics = new TrakHoundAppConnectionMetrics();
            metrics.Id = state.Id;
            metrics.ConnectionId = state.Connection?.ConnectionId;
            metrics.Code = state.Connection?.Code;
            metrics.ClientAddress = state.Connection?.ClientAddress;
            metrics.ClientHostname = state.Connection?.ClientHostname;
            metrics.ClientAgent = state.Connection?.ClientAgent;
            metrics.Connected = state.Connected;
            metrics.Disconnected = state.Disconnected;
            metrics.IsConnected = state.Disconnected == null;
            metrics.TotalPageViews = state.Views;
            metrics.TotalPageDuration = state.TotalDuration + currentDuration;
            metrics.CurrentAppId = current?.AppId;
            metrics.CurrentAppName = current?.AppName;
            metrics.CurrentRoute = current?.Route;
            metrics.CurrentPath = current?.LastPath;
            metrics.CurrentPageDuration = currentDuration;
            metrics.Pages = state.Pages
                .Where(o => appId == null || o.Value.AppId == appId)
                .Select(o => CreatePageMetrics(o.Value, o.Key == currentPageKey ? new[] { currentDuration } : Enumerable.Empty<double>()))
                .OrderByDescending(o => o.LastViewed)
                .ToList();
            return metrics;
        }

        private static TrakHoundAppPageMetrics CreatePageMetrics(PageStats page, IEnumerable<double> openDurations)
        {
            var open = openDurations?.ToList() ?? new List<double>();
            var totalDuration = page.TotalDuration + open.Sum();

            var metrics = new TrakHoundAppPageMetrics();
            metrics.AppId = page.AppId;
            metrics.AppName = page.AppName;
            metrics.Route = page.Route;
            metrics.LastPath = page.LastPath;
            metrics.TotalViews = page.Views;
            metrics.ActiveViews = open.Count;
            metrics.TotalDuration = totalDuration;
            metrics.AverageDuration = page.Views > 0 ? totalDuration / page.Views : 0;
            metrics.MaxDuration = open.Count > 0 ? Math.Max(page.MaxDuration, open.Max()) : page.MaxDuration;
            metrics.LastViewed = page.LastViewed;
            return metrics;
        }

        private static string GetPageKey(string appId, string route) => $"{appId}|{route}";

        private static string RemoveQuery(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var i = path.IndexOfAny(new[] { '?', '#' });
            return i >= 0 ? path.Substring(0, i) : path;
        }
    }
}
