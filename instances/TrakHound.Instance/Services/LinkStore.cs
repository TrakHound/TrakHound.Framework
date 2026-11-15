namespace TrakHound.Instance.Services
{
    public static class LinkStore
    {
        // How long a Link is kept (ex. while the user signs in with an Identity Provider)
        private static readonly TimeSpan _linkLifetime = TimeSpan.FromMinutes(15);

        private static readonly Dictionary<string, (string Link, DateTime Added)> _links = new Dictionary<string, (string, DateTime)>();
        private static readonly object _lock = new object();


        public static string Get(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                lock (_lock)
                {
                    if (_links.Remove(id, out var link) && DateTime.UtcNow - link.Added < _linkLifetime)
                    {
                        return link.Link;
                    }
                }
            }

            return null;
        }

        public static void Add(string id, string link)
        {
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(link))
            {
                lock (_lock)
                {
                    // Remove expired Links
                    var now = DateTime.UtcNow;
                    foreach (var expiredId in _links.Where(o => now - o.Value.Added >= _linkLifetime).Select(o => o.Key).ToList())
                    {
                        _links.Remove(expiredId);
                    }

                    _links.TryAdd(id, (link, now));
                }
            }
        }
    }
}
