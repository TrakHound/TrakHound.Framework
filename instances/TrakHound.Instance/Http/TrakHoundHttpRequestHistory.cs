namespace TrakHound.Http
{
    /// <summary>
    /// Keeps the most recent HTTP Requests (for the HTTP Requests Diagnostics)
    /// </summary>
    public class TrakHoundHttpRequestHistory
    {
        public const int DefaultCapacity = TrakHoundInstanceMetricsConfiguration.DefaultHistory;
        public const int MaxCapacity = 10000;

        private CircularBuffer<TrakHoundHttpRequestRecord> _buffer = new(DefaultCapacity);
        private long _lastId;


        /// <summary>
        /// The most Requests kept (0 keeps none). Changing it clears the Requests kept so far.
        /// </summary>
        public int Capacity
        {
            get => _buffer?.Limit ?? 0;
            set
            {
                var capacity = Math.Clamp(value, 0, MaxCapacity);
                if (capacity == Capacity) return;

                _buffer = capacity > 0 ? new CircularBuffer<TrakHoundHttpRequestRecord>(capacity) : null;
            }
        }


        /// <summary>
        /// Add a completed Request (assigns its Id)
        /// </summary>
        public void Add(TrakHoundHttpRequestRecord request)
        {
            var buffer = _buffer;
            if (request != null && buffer != null)
            {
                request.Id = Interlocked.Increment(ref _lastId);
                buffer.Add(request);
            }
        }

        /// <summary>
        /// The Requests kept, newest first
        /// </summary>
        public TrakHoundHttpRecentRequests Get()
        {
            var buffer = _buffer;
            return new TrakHoundHttpRecentRequests
            {
                Capacity = buffer?.Limit ?? 0,
                Requests = buffer?.Get() ?? Array.Empty<TrakHoundHttpRequestRecord>()
            };
        }
    }
}
