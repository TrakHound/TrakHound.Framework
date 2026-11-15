// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TrakHound.Extensions;
using TrakHound.Packages;

namespace TrakHound.Zones
{
    /// <summary>
    /// Something a Package's code can do outside of its TrakHound Client (which a Zone can't restrict)
    /// </summary>
    public class TrakHoundPackageCapability
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The .NET types or members that were found (ex. "System.IO.File")
        /// </summary>
        public IReadOnlyList<string> Evidence { get; set; }
    }

    /// <summary>
    /// Reads a Package's assemblies (without loading them) to find code that reaches outside of the TrakHound Client, such as the file system,
    /// the network or other processes. Zones only govern calls made through the Client, so these are shown as warnings for Zone Members.
    /// </summary>
    /// <remarks>
    /// This is a guide, not a guarantee: code can reach the same things indirectly (ex. through another library or by reflection).
    /// </remarks>
    public class TrakHoundPackageCapabilityScanner
    {
        private class CapabilityDefinition
        {
            public string Id;
            public string Title;
            public string Description;
            public string[] Types;
            public string[] Members; // "Namespace.Type::Member"
        }

        private static readonly CapabilityDefinition[] _definitions = new[]
        {
            new CapabilityDefinition
            {
                Id = "file-system",
                Title = "File System",
                Description = "Reads or writes files directly (not through a Volume)",
                Types = new[] { "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream", "System.IO.DriveInfo", "System.IO.FileSystemWatcher" }
            },
            new CapabilityDefinition
            {
                Id = "network",
                Title = "Network",
                Description = "Makes network connections directly (not through an Api or Service)",
                Types = new[] { "System.Net.Http.HttpClient", "System.Net.Http.HttpClientHandler", "System.Net.Http.SocketsHttpHandler", "System.Net.WebClient", "System.Net.WebRequest", "System.Net.HttpWebRequest", "System.Net.Sockets.Socket", "System.Net.Sockets.TcpClient", "System.Net.Sockets.TcpListener", "System.Net.Sockets.UdpClient", "System.Net.WebSockets.ClientWebSocket", "System.Net.Dns" }
            },
            new CapabilityDefinition
            {
                Id = "processes",
                Title = "Processes",
                Description = "Starts or controls other processes",
                Types = new[] { "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo" }
            },
            new CapabilityDefinition
            {
                Id = "native-code",
                Title = "Native Code",
                Description = "Calls native (unmanaged) code",
                Types = new[] { "System.Runtime.InteropServices.NativeLibrary", "System.Runtime.InteropServices.Marshal" }
            },
            new CapabilityDefinition
            {
                Id = "dynamic-code",
                Title = "Dynamic Code Loading",
                Description = "Loads other assemblies at runtime",
                Types = new[] { "System.Runtime.Loader.AssemblyLoadContext" },
                Members = new[] { "System.Reflection.Assembly::Load", "System.Reflection.Assembly::LoadFrom", "System.Reflection.Assembly::LoadFile", "System.Reflection.Assembly::UnsafeLoadFrom" }
            },
            new CapabilityDefinition
            {
                Id = "environment",
                Title = "Environment",
                Description = "Reads environment variables or exits the process",
                Members = new[] { "System.Environment::GetEnvironmentVariable", "System.Environment::GetEnvironmentVariables", "System.Environment::SetEnvironmentVariable", "System.Environment::Exit", "System.Environment::FailFast" }
            },
            new CapabilityDefinition
            {
                Id = "registry",
                Title = "Registry",
                Description = "Reads or writes the Windows Registry",
                Types = new[] { "Microsoft.Win32.Registry", "Microsoft.Win32.RegistryKey" }
            }
        };

        private const string _nativeCodeId = "native-code";

        private readonly TrakHoundPackageManager _packageManager;
        private readonly ConcurrentDictionary<string, IReadOnlyList<TrakHoundPackageCapability>> _cache = new ConcurrentDictionary<string, IReadOnlyList<TrakHoundPackageCapability>>(); // Package Hash => Capabilities


        public TrakHoundPackageCapabilityScanner(TrakHoundPackageManager packageManager)
        {
            _packageManager = packageManager;
        }


        /// <summary>
        /// Gets the Capabilities found in the Package's assemblies (empty if the Package is not installed)
        /// </summary>
        public IReadOnlyList<TrakHoundPackageCapability> Scan(string packageId, string packageVersion = null)
        {
            var package = ResolvePackage(packageId, packageVersion);
            if (package == null) return Array.Empty<TrakHoundPackageCapability>();

            var key = package.Hash ?? $"{package.Id}:{package.Version}";
            return _cache.GetOrAdd(key, _ =>
            {
                var directory = TrakHoundPackage.GetPackageVersionDistributableDirectory(_packageManager.Path, package.Category, package.Id, package.Version);
                return ScanDirectory(directory);
            });
        }

        // Configurations can use a wildcard Version ("*" = the latest installed Version), the same as when their Module is loaded
        private TrakHoundPackage ResolvePackage(string packageId, string packageVersion)
        {
            if (_packageManager == null || string.IsNullOrEmpty(packageId)) return null;

            return _packageManager.Resolve(packageId, packageVersion);
        }

        /// <summary>
        /// Scans every assembly in the directory (and its subdirectories)
        /// </summary>
        public static IReadOnlyList<TrakHoundPackageCapability> ScanDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return Array.Empty<TrakHoundPackageCapability>();

            var evidence = new Dictionary<string, SortedSet<string>>(); // Capability.Id => Evidence

            foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                // Assemblies the Instance also has (ex. TrakHound.Common) are shared with the Instance, not part of the Package's own code
                var fileName = System.IO.Path.GetFileName(file);
                if (File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, fileName))) continue;

                try
                {
                    ScanAssembly(file, fileName, evidence);
                }
                catch
                {
                    // Not a .NET assembly (ex. a native library), or unreadable
                }
            }

            return _definitions
                .Where(o => evidence.ContainsKey(o.Id))
                .Select(o => new TrakHoundPackageCapability
                {
                    Id = o.Id,
                    Title = o.Title,
                    Description = o.Description,
                    Evidence = evidence[o.Id].ToList()
                })
                .ToList();
        }

        private static void ScanAssembly(string path, string fileName, Dictionary<string, SortedSet<string>> evidence)
        {
            void Add(string capabilityId, string item) => AddEvidence(evidence, capabilityId, $"{item} ({fileName})");

            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return;

            var reader = peReader.GetMetadataReader();

            // Types the assembly refers to
            var referencedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handle in reader.TypeReferences)
            {
                referencedTypes.Add(GetTypeName(reader, reader.GetTypeReference(handle)));
            }

            foreach (var definition in _definitions)
            {
                if (definition.Types == null) continue;

                foreach (var type in definition.Types)
                {
                    if (referencedTypes.Contains(type)) Add(definition.Id, type);
                }
            }

            // Members the assembly calls
            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                if (member.Parent.Kind != HandleKind.TypeReference) continue;

                var memberName = $"{GetTypeName(reader, reader.GetTypeReference((TypeReferenceHandle)member.Parent))}::{reader.GetString(member.Name)}";
                foreach (var definition in _definitions)
                {
                    if (definition.Members != null && definition.Members.Contains(memberName)) Add(definition.Id, memberName);
                }
            }

            // Methods implemented in native code (DllImport / LibraryImport)
            foreach (var handle in reader.MethodDefinitions)
            {
                var method = reader.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    var import = method.GetImport();
                    var moduleName = !import.Module.IsNil ? reader.GetString(reader.GetModuleReference(import.Module).Name) : "native";
                    Add(_nativeCodeId, $"{moduleName}::{reader.GetString(method.Name)}");
                }
            }
        }

        private static string GetTypeName(MetadataReader reader, TypeReference type)
        {
            var name = reader.GetString(type.Name);

            // Nested types are named by their declaring type
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return $"{GetTypeName(reader, reader.GetTypeReference((TypeReferenceHandle)type.ResolutionScope))}+{name}";
            }

            var ns = reader.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        private static void AddEvidence(Dictionary<string, SortedSet<string>> evidence, string capabilityId, string item)
        {
            if (!evidence.TryGetValue(capabilityId, out var items))
            {
                items = new SortedSet<string>(StringComparer.Ordinal);
                evidence.Add(capabilityId, items);
            }

            // Keep the list readable
            if (items.Count < 20) items.Add(item);
        }
    }
}
