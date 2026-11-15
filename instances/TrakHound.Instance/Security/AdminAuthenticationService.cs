// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components.Authorization;
using TrakHound.Instances;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Signs in to the Admin Interface (for one Blazor Circuit). The Session itself is an HttpOnly Cookie (see TrakHoundAdminSessions),
    /// read from the Circuit's Authentication State
    /// </summary>
    public class AdminAuthenticationService
    {
        public const string ThrottleScope = "admin";

        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundAdminSessions _sessions;
        private readonly TrakHoundSignInThrottle _throttle;
        private readonly AuthenticationStateProvider _authenticationStateProvider;


        public struct AuthenticationResult
        {
            public bool Success { get; set; }
            public string Username { get; set; }

            /// <summary>
            /// The one time Code to exchange for the Session Cookie at /_admin/session/signin
            /// </summary>
            public string Code { get; set; }

            public string Message { get; set; }
        }


        public AdminAuthenticationService(ITrakHoundInstance instance, TrakHoundAdminSessions sessions, TrakHoundSignInThrottle throttle, AuthenticationStateProvider authenticationStateProvider)
        {
            _instance = instance;
            _sessions = sessions;
            _throttle = throttle;
            _authenticationStateProvider = authenticationStateProvider;
        }


        public async Task<AuthenticationResult> Login(string username, string password)
        {
            var result = new AuthenticationResult();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                result.Message = "Invalid Username or Password";
                return result;
            }

            // The client address of the Circuit's connection (the HttpContext isn't available in a Circuit, see TrakHoundAdminSessionMiddleware)
            var clientAddress = TrakHoundAdminSessions.GetClientAddress(await GetPrincipal());

            var lockout = _throttle.GetLockout(ThrottleScope, clientAddress, username);
            if (lockout.HasValue)
            {
                result.Message = TrakHoundSignInThrottle.GetMessage(lockout.Value);
                return result;
            }

            if (!AdminUserExists())
            {
                // Removed (or never created) while the Login page was open
                result.Message = "No Admin Account exists. Reload the page";
                return result;
            }

            // Checks both the Username and the Password. PBKDF2 is CPU bound, so it isn't run on the Circuit's thread
            var user = await Task.Run(() => _sessions.Accounts.Verify(username.Trim(), password));
            if (user == null)
            {
                _throttle.RecordFailure(ThrottleScope, clientAddress, username);

                // Same message for either field (and for a disabled Account) so the Username can't be discovered
                result.Message = "Invalid Username or Password";
                return result;
            }

            _throttle.RecordSuccess(ThrottleScope, clientAddress, username);

            result.Success = true;
            result.Username = user.Username;
            result.Code = _sessions.CreateCode(user);
            return result;
        }

        /// <summary>
        /// End the Session (the browser then deletes the Cookie at /_admin/session/signout)
        /// </summary>
        public async Task Logout()
        {
            _sessions.Revoke(await GetPrincipal());
        }

        public async Task<bool> IsAuthenticated()
        {
            if (!_instance.Configuration.AdminAuthenticationEnabled) return true;

            return _sessions.Validate(await GetPrincipal()) != null;
        }

        /// <summary>
        /// The signed in Admin Account (null if not signed in, or Admin Authentication is disabled)
        /// </summary>
        public async Task<TrakHoundAdminAccount> GetAccount()
        {
            return _sessions.Validate(await GetPrincipal());
        }

        /// <summary>
        /// Whether any Admin Account exists. The first can't be created from the Admin Interface (anyone who reached a new Instance first could
        /// claim it), only from the machine the Instance runs on (see TrakHoundAdminAccountCommand and TrakHoundStartupAdminUser)
        /// </summary>
        public bool AdminUserExists()
        {
            return _sessions.Accounts.Any();
        }


        private async Task<System.Security.Claims.ClaimsPrincipal> GetPrincipal()
        {
            try
            {
                var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
                return state?.User;
            }
            catch (InvalidOperationException)
            {
                return null; // The Authentication State is not available outside of a Circuit
            }
        }
    }
}
