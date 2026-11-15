// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Security
{
    /// <summary>
    /// The Status of an Identity Provider operation
    /// </summary>
    public enum TrakHoundIdentityResultStatus
    {
        /// <summary>
        /// The Request did not contain anything for the Identity Provider to verify (ex. no Token was sent)
        /// </summary>
        NoResult,

        /// <summary>
        /// The Identity was verified
        /// </summary>
        Success,

        /// <summary>
        /// The credentials were present but not valid (ex. wrong password or an expired Token)
        /// </summary>
        Fail,

        /// <summary>
        /// The Identity Provider could not complete the operation (ex. the external Identity server is unavailable)
        /// </summary>
        Error
    }

    /// <summary>
    /// The result of an Identity Provider verifying an Identity
    /// </summary>
    public readonly struct TrakHoundIdentityResult
    {
        /// <summary>
        /// The Status of the operation
        /// </summary>
        public TrakHoundIdentityResultStatus Status { get; }

        /// <summary>
        /// The verified Identity (only set when the Status is Success)
        /// </summary>
        public TrakHoundIdentity Identity { get; }

        /// <summary>
        /// A message describing the result (ex. why it failed). Shown in logs, never to the client
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// For a Callback, the State value that was given to the Challenge (used to return to the originally requested URL)
        /// </summary>
        public string State { get; }


        /// <summary>
        /// True if the Identity was verified
        /// </summary>
        public bool IsSuccess => Status == TrakHoundIdentityResultStatus.Success && Identity != null;


        private TrakHoundIdentityResult(TrakHoundIdentityResultStatus status, TrakHoundIdentity identity, string message, string state)
        {
            Status = status;
            Identity = identity;
            Message = message;
            State = state;
        }


        /// <summary>
        /// The Identity was verified
        /// </summary>
        public static TrakHoundIdentityResult Success(TrakHoundIdentity identity, string state = null) => new TrakHoundIdentityResult(TrakHoundIdentityResultStatus.Success, identity, null, state);

        /// <summary>
        /// The Request did not contain anything for the Identity Provider to verify
        /// </summary>
        public static TrakHoundIdentityResult NoResult() => new TrakHoundIdentityResult(TrakHoundIdentityResultStatus.NoResult, null, null, null);

        /// <summary>
        /// The credentials were present but not valid
        /// </summary>
        public static TrakHoundIdentityResult Fail(string message = null, string state = null) => new TrakHoundIdentityResult(TrakHoundIdentityResultStatus.Fail, null, message, state);

        /// <summary>
        /// The Identity Provider could not complete the operation
        /// </summary>
        public static TrakHoundIdentityResult Error(string message = null, string state = null) => new TrakHoundIdentityResult(TrakHoundIdentityResultStatus.Error, null, message, state);
    }

    /// <summary>
    /// A Redirect returned by an Identity Provider (ex. to an external sign in or sign out page)
    /// </summary>
    public readonly struct TrakHoundIdentityRedirect
    {
        /// <summary>
        /// The URL to redirect to. Null if the Identity Provider does not need a Redirect
        /// </summary>
        public string Location { get; }

        /// <summary>
        /// A message describing the result (ex. why a Redirect could not be created). Shown in logs, never to the client
        /// </summary>
        public string Message { get; }


        /// <summary>
        /// True if there is a Location to redirect to
        /// </summary>
        public bool IsRedirect => !string.IsNullOrEmpty(Location);


        private TrakHoundIdentityRedirect(string location, string message)
        {
            Location = location;
            Message = message;
        }


        /// <summary>
        /// Redirect to the specified Location
        /// </summary>
        public static TrakHoundIdentityRedirect To(string location) => new TrakHoundIdentityRedirect(location, null);

        /// <summary>
        /// No Redirect is needed (or the Identity Provider doesn't support it)
        /// </summary>
        public static TrakHoundIdentityRedirect None(string message = null) => new TrakHoundIdentityRedirect(null, message);
    }
}
