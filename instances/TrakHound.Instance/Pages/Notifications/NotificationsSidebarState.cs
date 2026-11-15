// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Pages.Notifications
{
    /// <summary>
    /// Whether the Notifications sidebar is open (one per browser session, shared by the Header button and the Layout)
    /// </summary>
    public class NotificationsSidebarState
    {
        public bool IsOpen { get; private set; }

        public event Action Changed;


        public void Toggle() => SetOpen(!IsOpen);

        public void Open() => SetOpen(true);

        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            if (IsOpen == open) return;

            IsOpen = open;
            Changed?.Invoke();
        }
    }
}
