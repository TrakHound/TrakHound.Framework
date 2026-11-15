// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using System.Text;

namespace TrakHound.Licenses
{
    // A licensed Module compiles this file into itself (with TRAKHOUND_LICENSE_SOURCES) so a changed TrakHound.Common.dll can't skip its License check.
    // It is internal there, so it doesn't clash with TrakHound.Common's public type
    /// <summary>
    /// Where a Device ID came from
    /// </summary>
#if TRAKHOUND_LICENSE_SOURCES
    internal enum TrakHoundDeviceIdSource
#else
    public enum TrakHoundDeviceIdSource
#endif
    {
        /// <summary>
        /// The machine name
        /// </summary>
        MachineName,

        /// <summary>
        /// The TRAKHOUND_DEVICE_ID environment variable (only in a container)
        /// </summary>
        Variable
    }

    /// <summary>
    /// The Device ID and how it was chosen
    /// </summary>
#if TRAKHOUND_LICENSE_SOURCES
    internal sealed class TrakHoundDeviceIdInformation
#else
    public sealed class TrakHoundDeviceIdInformation
#endif
    {
        public string DeviceId { get; init; }

        public TrakHoundDeviceIdSource Source { get; init; }

        /// <summary>
        /// What showed that this is running in a container (null if it isn't)
        /// </summary>
        public string Container { get; init; }

        /// <summary>
        /// Why TRAKHOUND_DEVICE_ID was ignored (null if it was used or isn't set)
        /// </summary>
        public string Warning { get; init; }
    }

    /// <summary>
    /// The ID that Licenses are issued for: the SHA-256 of the machine name, as 52 characters of lower case Crockford Base32
    /// </summary>
    /// <remarks>
    /// A container's machine name is its hostname, which changes when it is recreated unless one is set, so a container
    /// can set TRAKHOUND_DEVICE_ID to a stable value instead. It is only used when the process is in a container
    /// (DOTNET_RUNNING_IN_CONTAINER and a sign that doesn't come from an environment variable), so it can't give a normal
    /// machine any Device ID it wants. The calculation must never change: every License issued depends on it.
    /// </remarks>
#if TRAKHOUND_LICENSE_SOURCES
    internal static class TrakHoundDeviceId
#else
    public static class TrakHoundDeviceId
#endif
    {
        public const string Variable = "TRAKHOUND_DEVICE_ID";
        public const string ContainerVariable = "DOTNET_RUNNING_IN_CONTAINER";

        private static readonly Lazy<TrakHoundDeviceIdInformation> _information = new Lazy<TrakHoundDeviceIdInformation>(() =>
        {
            return Resolve(
                Environment.MachineName,
                Environment.GetEnvironmentVariable(Variable),
                string.Equals(Environment.GetEnvironmentVariable(ContainerVariable), "true", StringComparison.OrdinalIgnoreCase),
                TrakHoundContainerDetection.Detect());
        });


        /// <summary>
        /// Get the ID of this Device
        /// </summary>
        public static string Get() => _information.Value.DeviceId;

        /// <summary>
        /// Get the ID of this Device and how it was chosen (ex. to show why TRAKHOUND_DEVICE_ID was ignored)
        /// </summary>
        public static TrakHoundDeviceIdInformation GetInformation() => _information.Value;


        /// <summary>
        /// Choose the Device ID
        /// </summary>
        /// <param name="machineName">The machine name</param>
        /// <param name="variable">The value of TRAKHOUND_DEVICE_ID</param>
        /// <param name="dotnetInContainer">Whether DOTNET_RUNNING_IN_CONTAINER is true</param>
        /// <param name="container">What showed that this is a container, other than environment variables (null if nothing did)</param>
        public static TrakHoundDeviceIdInformation Resolve(string machineName, string variable, bool dotnetInContainer, string container)
        {
            var inContainer = dotnetInContainer && container != null;

            if (!string.IsNullOrWhiteSpace(variable))
            {
                if (inContainer)
                {
                    return new TrakHoundDeviceIdInformation
                    {
                        DeviceId = Compute(variable.Trim()),
                        Source = TrakHoundDeviceIdSource.Variable,
                        Container = container
                    };
                }

                var reason = !dotnetInContainer
                    ? $"{ContainerVariable} isn't 'true'"
                    : "no container was detected";

                return new TrakHoundDeviceIdInformation
                {
                    DeviceId = Compute(machineName),
                    Source = TrakHoundDeviceIdSource.MachineName,
                    Container = dotnetInContainer ? container : null,
                    Warning = $"{Variable} is ignored because {reason}. It is only used in a container"
                };
            }

            return new TrakHoundDeviceIdInformation
            {
                DeviceId = Compute(machineName),
                Source = TrakHoundDeviceIdSource.MachineName,
                Container = inContainer ? container : null
            };
        }

        /// <summary>
        /// The Device ID for a value (the machine name or TRAKHOUND_DEVICE_ID)
        /// </summary>
        public static string Compute(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
            return TrakHoundBase32.Encode(hash).ToLowerInvariant();
        }

        /// <summary>
        /// A Device ID as Compute writes it, however it was typed (case, dashes, spaces, I / L for 1 and O for 0). Returns null if it isn't Base32
        /// </summary>
        public static string Normalize(string deviceId)
        {
            return TrakHoundBase32.Normalize(deviceId)?.ToLowerInvariant();
        }
    }
}
