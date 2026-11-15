// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound.Licenses
{
    // A licensed Module compiles this file into itself (with TRAKHOUND_LICENSE_SOURCES) so a changed TrakHound.Common.dll can't skip its License check.
    // It is internal there, so it doesn't clash with TrakHound.Common's public type
    /// <summary>
    /// Detects a container from what the container runtime leaves in the file system (or the Windows registry), not from environment variables,
    /// which anyone can set on a normal machine
    /// </summary>
#if TRAKHOUND_LICENSE_SOURCES
    internal static class TrakHoundContainerDetection
#else
    public static class TrakHoundContainerDetection
#endif
    {
        // Seen in /proc/1/cgroup (cgroup v1) and /proc/self/mountinfo (the bind mounts of /etc/hostname, /etc/hosts and /etc/resolv.conf)
        private static readonly string[] _markers = new[]
        {
            "/docker/",
            "/docker-",
            "/kubepods",
            "/kubelet/pods/",
            "/containerd/",
            "/libpod",
            "/overlay-containers/",
            "/lxc/"
        };


        /// <summary>
        /// Returns what showed that this is a container (ex. "/.dockerenv"), or null if nothing did
        /// </summary>
        public static string Detect()
        {
            try
            {
                if (OperatingSystem.IsWindows()) return DetectWindows();
                if (OperatingSystem.IsLinux()) return DetectLinux(File.Exists, ReadFile);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Check a Linux file system
        /// </summary>
        /// <param name="fileExists">Whether a file exists</param>
        /// <param name="readFile">Read a file (null if it can't be read)</param>
        public static string DetectLinux(Func<string, bool> fileExists, Func<string, string> readFile)
        {
            // Docker
            if (fileExists("/.dockerenv")) return "/.dockerenv";

            // Podman
            if (fileExists("/run/.containerenv")) return "/run/.containerenv";

            // cgroup v1 (Docker, Kubernetes, containerd, LXC)
            var cgroup = readFile("/proc/1/cgroup");
            if (HasMarker(cgroup)) return "/proc/1/cgroup";

            // cgroup v2 leaves /proc/1/cgroup as "0::/", but the runtime's bind mounts and an overlay root still show
            var mountInfo = readFile("/proc/self/mountinfo");
            if (HasRuntimeMount(mountInfo) || HasOverlayRoot(mountInfo)) return "/proc/self/mountinfo";

            return null;
        }


        private static string DetectWindows()
        {
            // Set in Windows containers (process and Hyper-V isolation)
            if (OperatingSystem.IsWindows())
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
                {
                    if (key?.GetValue("ContainerType") != null) return @"HKLM\SYSTEM\CurrentControlSet\Control\ContainerType";
                }
            }

            return null;
        }

        private static bool HasMarker(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (var marker in _markers)
            {
                if (text.Contains(marker, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        // A line of mountinfo is "id parent major:minor root mountpoint options ... - fstype source superoptions"

        // The runtime bind mounts these from its own folder (ex. /var/lib/docker/containers/[id]/hostname). Only these mount points
        // are checked: a machine that runs containers also sees the runtime's folders in its own mounts
        private static bool HasRuntimeMount(string mountInfo)
        {
            if (string.IsNullOrEmpty(mountInfo)) return false;

            foreach (var line in mountInfo.Split('\n'))
            {
                var fields = line.Split(' ');
                if (fields.Length < 5) continue;

                var mountPoint = fields[4];
                if (mountPoint != "/etc/hostname" && mountPoint != "/etc/hosts" && mountPoint != "/etc/resolv.conf") continue;

                if (HasMarker(fields[3])) return true;
            }

            return false;
        }

        private static bool HasOverlayRoot(string mountInfo)
        {
            if (string.IsNullOrEmpty(mountInfo)) return false;

            foreach (var line in mountInfo.Split('\n'))
            {
                var fields = line.Split(' ');
                if (fields.Length < 5 || fields[4] != "/") continue;

                var separator = Array.IndexOf(fields, "-");
                if (separator > 0 && separator + 1 < fields.Length && fields[separator + 1] == "overlay") return true;
            }

            return false;
        }

        private static string ReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
