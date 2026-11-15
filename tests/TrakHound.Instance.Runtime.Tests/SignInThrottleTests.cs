// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Failed sign ins lock out the client address (and, at a higher limit, the username) for a while, longer each time
    /// </summary>
    public class SignInThrottleTests
    {
        private const string Scope = "admin";

        private DateTime _now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly TrakHoundSignInThrottle _throttle;


        public SignInThrottleTests()
        {
            _throttle = new TrakHoundSignInThrottle(() => _now);
        }

        private void Fail(string address, string username, int count = 1)
        {
            for (var i = 0; i < count; i++) _throttle.RecordFailure(Scope, address, username);
        }


        [Fact]
        public void FewFailures_AreNotLockedOut()
        {
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit - 1);

            Assert.Null(_throttle.GetLockout(Scope, "10.0.0.1", "admin"));
        }

        [Fact]
        public void AddressLimit_LocksOutTheAddress()
        {
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit);

            Assert.Equal(TrakHoundSignInThrottle.DefaultLockout, _throttle.GetLockout(Scope, "10.0.0.1", "admin"));
            Assert.Equal(TrakHoundSignInThrottle.DefaultLockout, _throttle.GetLockout(Scope, "10.0.0.1", "other-user"));

            // Another client can still sign in as the same user (the username limit is higher)
            Assert.Null(_throttle.GetLockout(Scope, "10.0.0.2", "admin"));
        }

        [Fact]
        public void UsernameLimit_LocksOutTheUsernameFromEveryAddress()
        {
            for (var i = 0; i < TrakHoundSignInThrottle.DefaultUsernameLimit; i++) Fail($"10.0.1.{i}", "admin");

            Assert.NotNull(_throttle.GetLockout(Scope, "10.0.2.1", "admin"));
            Assert.Null(_throttle.GetLockout(Scope, "10.0.2.1", "other-user"));
        }

        [Fact]
        public void Lockout_Ends()
        {
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit);

            _now += TrakHoundSignInThrottle.DefaultLockout;

            Assert.Null(_throttle.GetLockout(Scope, "10.0.0.1", "admin"));
        }

        [Fact]
        public void RepeatedLockouts_DoubleUpToTheMaximum()
        {
            var expected = TrakHoundSignInThrottle.DefaultLockout;
            for (var i = 0; i < 6; i++)
            {
                Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit);

                var lockout = _throttle.GetLockout(Scope, "10.0.0.1", "admin");
                Assert.Equal(expected, lockout);

                _now += lockout.Value;
                expected = expected * 2 > TrakHoundSignInThrottle.DefaultMaxLockout ? TrakHoundSignInThrottle.DefaultMaxLockout : expected * 2;
            }
        }

        [Fact]
        public void Success_ClearsFailures()
        {
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit - 1);
            _throttle.RecordSuccess(Scope, "10.0.0.1", "admin");
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit - 1);

            Assert.Null(_throttle.GetLockout(Scope, "10.0.0.1", "admin"));
        }

        [Fact]
        public void FailuresOutsideTheWindow_AreNotCounted()
        {
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit - 1);
            _now += TrakHoundSignInThrottle.DefaultWindow + TimeSpan.FromSeconds(1);
            Fail("10.0.0.1", "admin");

            Assert.Null(_throttle.GetLockout(Scope, "10.0.0.1", "admin"));
        }

        [Fact]
        public void Scopes_AreSeparate()
        {
            Fail("10.0.0.1", "admin", TrakHoundSignInThrottle.DefaultAddressLimit);

            Assert.Null(_throttle.GetLockout("ldap", "10.0.0.1", "admin"));
        }

        [Fact]
        public void Usernames_IgnoreCase()
        {
            for (var i = 0; i < TrakHoundSignInThrottle.DefaultUsernameLimit; i++) Fail($"10.0.1.{i}", i % 2 == 0 ? "Admin" : "admin");

            Assert.NotNull(_throttle.GetLockout(Scope, "10.0.2.1", "ADMIN"));
        }

        [Theory]
        [InlineData(30, "1 minute")]
        [InlineData(60, "1 minute")]
        [InlineData(61, "2 minutes")]
        public void Message_RoundsUpToMinutes(int seconds, string expected)
        {
            Assert.Contains(expected, TrakHoundSignInThrottle.GetMessage(TimeSpan.FromSeconds(seconds)));
        }
    }
}
