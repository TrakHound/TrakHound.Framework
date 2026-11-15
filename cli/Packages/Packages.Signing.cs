// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.CLI.Packages
{
    internal static partial class Packages
    {
        /// <summary>
        /// Environment variable holding the signing key for CI: either the path to the key or the key itself
        /// </summary>
        private const string SigningKeyVariable = "TRAKHOUND_SIGNING_KEY";


        [Command("keygen", "Create a key pair for signing Packages")]
        [CommandHelp(null, "th packages keygen contoso --output ~/.trakhound/keys")]
        public static bool KeyGen(
            [CommandParameter("The name of the key files ([name].private.pem and [name].public.pem)")]
            string name = "trakhound-signing",
            [CommandOption("The path to the directory to write the key files to. If not specified, the current working directory is used", "-o")]
            string output = null
            )
        {
            var outputPath = CliPath.Resolve(output);
            var privatePath = Path.Combine(outputPath, $"{name}.private.pem");
            var publicPath = Path.Combine(outputPath, $"{name}.public.pem");

            // Replacing a key can't be undone, and every Package it signed would stop being trusted
            if (File.Exists(privatePath) || File.Exists(publicPath))
            {
                Console.Error.WriteLine($"Error : Key files already exist : ({privatePath})");
                return false;
            }

            Directory.CreateDirectory(outputPath);

            using (var signer = TrakHoundPackageSigner.Generate())
            {
                // Create the private key file with restricted permissions from the start on systems that support them
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

                using (var writer = new StreamWriter(privatePath, options)) writer.Write(signer.ExportPrivateKeyPem());
                File.WriteAllText(publicPath, signer.ExportPublicKeyPem());

                Console.WriteLine($"Key ID : {signer.KeyId}");
            }

            Console.WriteLine($"Private Key : {privatePath}");
            Console.WriteLine($"Public Key : {publicPath}");
            Console.WriteLine("Keep the private key secret, and give the public key to whoever needs to trust your Packages (th trust add).");

            if (GitCommands.IsInRepository(outputPath))
            {
                Console.WriteLine("Warning : The private key is inside a git repository. Move it out so that it can't be committed.");
            }

            return true;
        }


        /// <summary>
        /// Get the check for Packages being installed, from the Instance's packageVerification Setting. Only Require and RequireApproval can refuse a Package.
        /// The Instance checks again when it loads a Package, so this is so that the install reports the problem rather than leaving a Package that won't load
        /// </summary>
        private static IEnumerable<TrakHoundTrustedPublisher> ReadPackageManagerPublishers(string profilePath)
        {
            try
            {
                return TrakHoundPackageManagerPublishers.Read(profilePath).GetPublishers();
            }
            catch
            {
                return null; // The Instance doesn't trust them either until it reads them again
            }
        }

        private static Func<byte[], bool> GetInstallCheck(string path, string profile)
        {
            var profilePath = Trust.Trust.GetProfilePath(path, profile);
            var configuration = TrakHoundInstanceConfiguration.Read(Path.Combine(profilePath, TrakHoundInstanceConfiguration.Filename));

            var mode = configuration?.PackageVerification ?? TrakHoundPackageVerificationMode.Off;
            if (!TrakHoundPackageVerificationPolicy.RequiresTrust(mode)) return packageBytes => true;

            // The Instance's last attestation lists (kept in the Profile), since the CLI doesn't read the Package Managers itself. Synced Publishers are trusted too
            var trustedPublishers = TrakHoundTrustedPublishers.Combine(TrakHoundTrustedPublishers.Read(profilePath), ReadPackageManagerPublishers(profilePath));
            var approvals = mode == TrakHoundPackageVerificationMode.RequireApproval ? TrakHoundPackageManagerApprovalFiles.ReadApprovals(profilePath) : null;

            return packageBytes =>
            {
                var result = TrakHoundPackageVerifier.VerifyArchive(packageBytes, trustedPublishers);
                if (mode == TrakHoundPackageVerificationMode.RequireApproval) TrakHoundPackageVerificationPolicy.CheckApproval(result, approvals, true);
                if (TrakHoundPackageVerificationPolicy.IsAllowed(mode, result)) return true;

                var required = mode == TrakHoundPackageVerificationMode.RequireApproval ? "Packages approved by its Package Manager" : "verified Packages";
                Console.Error.WriteLine($"Error : Package refused because the Instance requires {required} : {TrakHoundPackageVerificationPolicy.Describe(result)}");
                return false;
            };
        }

        /// <summary>
        /// Get the signer from --sign-key or the environment variable. Returns null if neither is set. Throws a CommandException if the key can't be used
        /// </summary>
        /// <param name="signKey">The path to the private key</param>
        /// <param name="sourcePath">The directory being packed</param>
        private static TrakHoundPackageSigner GetSigner(string signKey, string sourcePath)
        {
            string pem;

            var keyPath = !string.IsNullOrEmpty(signKey) ? signKey : Environment.GetEnvironmentVariable(SigningKeyVariable);
            if (string.IsNullOrEmpty(keyPath)) return null;

            if (keyPath.Contains("-----BEGIN"))
            {
                pem = keyPath;
            }
            else
            {
                var fullKeyPath = CliPath.Resolve(keyPath);
                if (!File.Exists(fullKeyPath)) throw new CommandException($"Signing Key not Found : ({fullKeyPath})");

                // A key in the folder being packed could end up in the Package
                var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(File.Exists(sourcePath) ? Path.GetDirectoryName(sourcePath) : sourcePath)) + Path.DirectorySeparatorChar;
                if (fullKeyPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandException($"The Signing Key can't be inside the directory being packed : ({fullKeyPath})");
                }

                pem = File.ReadAllText(fullKeyPath);
            }

            try
            {
                return TrakHoundPackageSigner.FromPem(pem);
            }
            catch (Exception ex)
            {
                // The message is safe to show: the key itself isn't part of it
                throw new CommandException($"The Signing Key can't be read : {ex.Message}");
            }
        }

        /// <summary>
        /// Warn about uncommitted changes, and refuse to sign them. A signature shouldn't vouch for a commit that doesn't contain the code
        /// </summary>
        private static bool CheckWorkingTree(string commit, bool? dirty, TrakHoundPackageSigner signer, bool allowDirty)
        {
            if (dirty == true)
            {
                if (signer != null && !allowDirty)
                {
                    Console.Error.WriteLine($"Error : The working tree has uncommitted changes, so a signed Package wouldn't match commit {commit}. Commit them, or use --allow-dirty to sign anyway");
                    return false;
                }

                Console.WriteLine($"Warning : The working tree has uncommitted changes, so the Package may not match commit {commit}");
            }
            else if (signer != null && string.IsNullOrEmpty(commit))
            {
                Console.WriteLine("Warning : The directory isn't in a git repository, so the signed Package has no commit to trace it to");
            }

            return true;
        }
    }
}
