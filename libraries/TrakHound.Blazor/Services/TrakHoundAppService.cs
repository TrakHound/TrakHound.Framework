// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Blazor.Services
{
    public class TrakHoundAppService : ITrakHoundScopedAppInjectionService, IDisposable
    {
        private readonly ITrakHoundAppProvider _appProvider;
        private int _refreshVersion;
        private int _claimedRefreshVersion;


        public event EventHandler RefreshRequested;


        public string AppId { get; set; }

        public string ConnectionId { get; set; }

       
        public TrakHoundAppService() { }

        public TrakHoundAppService(ITrakHoundAppProvider appProvider)
        {
            _appProvider = appProvider;
            _appProvider.AppRefreshRequested += AppRefreshRequested;
            _appProvider.ConnectionRefreshRequested += ConnectionRefreshRequested;
        }

        public void Dispose()
        {
            if (_appProvider != null)
            {
                _appProvider.AppRefreshRequested -= AppRefreshRequested;
                _appProvider.ConnectionRefreshRequested -= ConnectionRefreshRequested;
            }
        }

        private void AppRefreshRequested(string appId, int delay)
        {
            if (AppId == appId) OnRefreshRequested();
        }

        private void ConnectionRefreshRequested(string connectionId, int delay)
        {
            if (ConnectionId == connectionId) OnRefreshRequested();
        }

        private void OnRefreshRequested()
        {
            Interlocked.Increment(ref _refreshVersion);
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Returns true for only the first subscriber that claims the current Refresh request
        /// (so a page with many components only reloads once)
        /// </summary>
        public bool TryClaimRefresh()
        {
            var version = Volatile.Read(ref _refreshVersion);
            return Interlocked.Exchange(ref _claimedRefreshVersion, version) != version;
        }

        public string RewriteUrl(string url)
        {
            if (_appProvider != null) return _appProvider.RewriteUrl(url);
            else return url;
        }
    }
}
