// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Components
{
    /// <summary>
    /// The Breadcrumbs of the Admin interface page being shown. A page sets them with AreaBreadcrumbs and its area's AreaHeader shows them.
    /// </summary>
    public class AreaBreadcrumbService
    {
        private static readonly IReadOnlyList<BreadcrumbItem> _empty = new List<BreadcrumbItem>();

        private object _owner;


        public IReadOnlyList<BreadcrumbItem> Items { get; private set; } = _empty;

        public event Action Changed;


        public void Set(object owner, IEnumerable<BreadcrumbItem> items)
        {
            var list = items != null ? items.ToList() : new List<BreadcrumbItem>();
            var changed = !IsSame(Items, list);

            _owner = owner;
            Items = list;

            if (changed) Changed?.Invoke();
        }

        // The next page can set its Breadcrumbs before the last one is disposed, so only the page that set them clears them
        public void Clear(object owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;

            _owner = null;
            if (Items.Count > 0)
            {
                Items = _empty;
                Changed?.Invoke();
            }
        }

        private static bool IsSame(IReadOnlyList<BreadcrumbItem> a, IReadOnlyList<BreadcrumbItem> b)
        {
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].Display != b[i].Display || a[i].Link != b[i].Link) return false;
            }

            return true;
        }
    }
}
