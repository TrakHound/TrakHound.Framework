// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Audit
{
    /// <summary>
    /// Who is making changes in the current async flow (a System API Request, or an event in an Admin Interface Circuit).
    /// Package and Configuration changes are recorded with it, without every method needing to pass it along
    /// </summary>
    public sealed class TrakHoundAuditContext
    {
        private static readonly AsyncLocal<TrakHoundAuditContext> _current = new AsyncLocal<TrakHoundAuditContext>();


        public static TrakHoundAuditContext Current => _current.Value;

        public string Source { get; }
        public string User { get; }
        public string ClientAddress { get; }


        public TrakHoundAuditContext(string source, string user, string clientAddress)
        {
            Source = source;
            User = user;
            ClientAddress = clientAddress;
        }


        public static IDisposable Begin(string source, string user, string clientAddress)
        {
            var previous = _current.Value;
            _current.Value = new TrakHoundAuditContext(source, user, clientAddress);
            return new Scope(previous);
        }


        private sealed class Scope : IDisposable
        {
            private readonly TrakHoundAuditContext _previous;

            public Scope(TrakHoundAuditContext previous) => _previous = previous;

            public void Dispose() => _current.Value = _previous;
        }
    }
}
