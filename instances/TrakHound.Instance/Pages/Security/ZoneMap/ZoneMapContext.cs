// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Pages.Security.ZoneMap
{
    /// <summary>
    /// What is being dragged on the Zone Map: a Module (or a Member pattern), and the Zone it is being dragged out of (null if it isn't in one)
    /// </summary>
    public class ZoneMapDrag
    {
        public string Member { get; set; }

        public string FromZoneId { get; set; }
    }

    /// <summary>
    /// Shared by the Zone Map page and its node widgets, so that Members can be dragged between Zones (and out of them)
    /// </summary>
    public class ZoneMapContext
    {
        /// <summary>
        /// What is currently being dragged (null if nothing)
        /// </summary>
        public ZoneMapDrag Drag { get; private set; }

        /// <summary>
        /// The Zone the drag is over (shown as a drop target)
        /// </summary>
        public string DropZoneId { get; set; }

        /// <summary>
        /// Moves a Member from one Zone to another (either can be null): Member, From Zone ID, To Zone ID
        /// </summary>
        public Func<string, string, string, Task> MoveMember { get; set; }

        /// <summary>
        /// Raised when the drag state changes (so the widgets can show drop targets)
        /// </summary>
        public event Action Changed;


        public void StartDrag(string member, string fromZoneId)
        {
            Drag = new ZoneMapDrag { Member = member, FromZoneId = fromZoneId };
            Changed?.Invoke();
        }

        public void EndDrag()
        {
            Drag = null;
            DropZoneId = null;
            Changed?.Invoke();
        }

        public void SetDropZone(string zoneId)
        {
            if (DropZoneId == zoneId) return;
            DropZoneId = zoneId;
            Changed?.Invoke();
        }

        /// <summary>
        /// Drops what is being dragged into the Zone (or out of every Zone if null)
        /// </summary>
        public async Task Drop(string toZoneId)
        {
            var drag = Drag;
            EndDrag();

            if (drag != null && MoveMember != null && !string.Equals(drag.FromZoneId, toZoneId, StringComparison.OrdinalIgnoreCase))
            {
                await MoveMember(drag.Member, drag.FromZoneId, toZoneId);
            }
        }
    }
}
