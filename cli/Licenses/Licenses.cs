// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using TrakHound.Licenses;

namespace TrakHound.CLI.Licenses
{
    [CommandGroup("licenses", "Create License signing keys, issue License Codes and read them")]
    internal static class Licenses
    {
        /// <summary>
        /// Environment variable holding the License signing key: either the path to the key or the key itself
        /// </summary>
        private const string SigningKeyVariable = "TRAKHOUND_LICENSE_KEY";


        [Command("keygen", "Create a key pair for issuing Licenses")]
        [CommandHelp(null, "th licenses keygen contoso --output ~/.trakhound/keys")]
        public static bool KeyGen(
            [CommandParameter("The name of the key files ([name].license.private.pem and [name].license.public.pem)")]
            string name = "trakhound",
            [CommandOption("The path to the directory to write the key files to. If not specified, the current working directory is used", "-o")]
            string output = null
            )
        {
            var outputPath = CliPath.Resolve(output);
            var privatePath = Path.Combine(outputPath, $"{name}.license.private.pem");
            var publicPath = Path.Combine(outputPath, $"{name}.license.public.pem");

            // Replacing a key can't be undone, and every License it issued would stop being valid
            if (File.Exists(privatePath) || File.Exists(publicPath))
            {
                Console.Error.WriteLine($"Error : Key files already exist : ({privatePath})");
                return false;
            }

            // A key next to a project could be packed or published with it
            if (Directory.Exists(outputPath) && Directory.EnumerateFiles(outputPath, "*.csproj").Any())
            {
                Console.Error.WriteLine($"Error : The directory contains a project, so the private key could be published with it. Choose a directory outside of it : ({outputPath})");
                return false;
            }

            Directory.CreateDirectory(outputPath);

            using (var signer = TrakHoundLicenseSigner.Generate())
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
            Console.WriteLine("Keep the private key secret: anyone with it can issue Licenses for your Products. Embed the public key in your Modules' code.");

            if (GitCommands.IsInRepository(outputPath))
            {
                Console.WriteLine("Warning : The private key is inside a git repository. Move it out so that it can't be committed.");
            }

            return true;
        }

        [Command("sign", "Issue a License Code")]
        [CommandHelp(null, "th licenses sign --key ./contoso.license.private.pem --product contoso.widget --device 7f3k... --limit 5 --expires 2027-12-31")]
        public static bool Sign(
            [CommandOption("The path to the private key. If not specified, the TRAKHOUND_LICENSE_KEY environment variable is used", "-k")]
            string key = null,
            [CommandOption("The Product ID")]
            string product = null,
            [CommandOption("The Device ID shown on the Instance's Licenses page")]
            string device = null,
            [CommandOption("Issue a License that works on any Device. The limit then applies to each installation")]
            bool anyDevice = false,
            [CommandOption("How many Assignments can use the License (1 to 65535)")]
            int limit = 1,
            [CommandOption("The last day the License can be used (yyyy-MM-dd, UTC). It expires at the end of that day")]
            string expires = null,
            [CommandOption("The number of days the License can be used, from now")]
            int? days = null,
            [CommandOption("Issue a License that doesn't expire")]
            bool permanent = false
            )
        {
            if (string.IsNullOrWhiteSpace(product)) throw new CommandException("--product is required");
            if (anyDevice == !string.IsNullOrWhiteSpace(device)) throw new CommandException("Specify either --device or --any-device");

            // The expiration must be chosen on purpose, so a forgotten option can't issue a License that never expires
            var choices = (expires != null ? 1 : 0) + (days.HasValue ? 1 : 0) + (permanent ? 1 : 0);
            if (choices != 1) throw new CommandException("Specify one of --expires, --days or --permanent");

            DateTime? expiration = null;
            if (expires != null)
            {
                if (!DateTime.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
                {
                    throw new CommandException($"Invalid value for --expires : '{expires}' (expected yyyy-MM-dd)");
                }

                expiration = date.AddDays(1);
            }
            else if (days.HasValue)
            {
                if (days.Value < 1) throw new CommandException("--days must be at least 1");
                expiration = DateTime.UtcNow.AddDays(days.Value);
            }

            using (var signer = GetSigner(key))
            {
                string licenseCode;
                try
                {
                    licenseCode = anyDevice
                        ? signer.SignForAnyDevice(product, limit, expiration)
                        : signer.Sign(product, device, limit, expiration);
                }
                catch (ArgumentException ex)
                {
                    throw new CommandException(ex.Message);
                }

                Console.WriteLine(licenseCode);
            }

            return true;
        }

        [Command("inspect", "Show what a License Code contains, and check it against a public key, Product and Device")]
        [CommandHelp(null, "th licenses inspect XXXXX-XXXXX-... --public-key ./contoso.license.public.pem --product contoso.widget")]
        public static bool Inspect(
            [CommandParameter("The License Code")]
            string licenseCode,
            [CommandOption("The path to the Publisher's public key, to check the signature")]
            string publicKey = null,
            [CommandOption("The Product ID to check")]
            string product = null,
            [CommandOption("The Device ID to check. If not specified, this machine's Device ID is used")]
            string device = null
            )
        {
            var code = TrakHoundLicenseCode.Parse(licenseCode);
            if (code == null)
            {
                Console.Error.WriteLine("Error : This isn't a License Code");
                return false;
            }

            var deviceId = !string.IsNullOrWhiteSpace(device) ? device : TrakHoundDeviceId.Get();

            Console.WriteLine($"Format Version : {code.Version}");
            Console.WriteLine($"Key ID : {code.KeyId}");
            Console.WriteLine($"Assignment Limit : {code.AssignmentLimit}");
            Console.WriteLine($"Expires : {(code.IsPermanent ? "Never" : code.ExpirationDate.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"))}");
            Console.WriteLine($"Device : {(code.IsAnyDevice ? "Any" : code.IsForDevice(deviceId) ? $"{deviceId} (matches)" : $"not {deviceId}")}");
            if (!string.IsNullOrWhiteSpace(product)) Console.WriteLine($"Product : {(code.IsForProduct(product) ? $"{product} (matches)" : $"not {product}")}");

            if (!string.IsNullOrEmpty(publicKey))
            {
                var keyPath = CliPath.Resolve(publicKey);
                if (!File.Exists(keyPath)) throw new CommandException($"Public Key not Found : ({keyPath})");

                var signed = code.IsSignedBy(ReadPublicKey(File.ReadAllText(keyPath)));
                Console.WriteLine($"Signature : {(signed ? "Valid" : "Not signed by this key")}");
                return signed;
            }

            return true;
        }

        [Command("device-id", "Show this machine's Device ID")]
        [CommandHelp(null, "th licenses device-id")]
        public static bool DeviceId()
        {
            var information = TrakHoundDeviceId.GetInformation();

            Console.WriteLine(information.DeviceId);
            if (information.Warning != null) Console.WriteLine($"Warning : {information.Warning}");

            return true;
        }


        /// <summary>
        /// Get the signer from --key or the environment variable. Throws a CommandException if there isn't one or it can't be used
        /// </summary>
        private static TrakHoundLicenseSigner GetSigner(string key)
        {
            var keyPath = !string.IsNullOrEmpty(key) ? key : Environment.GetEnvironmentVariable(SigningKeyVariable);
            if (string.IsNullOrEmpty(keyPath)) throw new CommandException($"Specify the private key with --key or the {SigningKeyVariable} environment variable");

            string pem;
            if (keyPath.Contains("-----BEGIN"))
            {
                pem = keyPath;
            }
            else
            {
                var fullKeyPath = CliPath.Resolve(keyPath);
                if (!File.Exists(fullKeyPath)) throw new CommandException($"Signing Key not Found : ({fullKeyPath})");

                pem = File.ReadAllText(fullKeyPath);
            }

            try
            {
                return TrakHoundLicenseSigner.FromPem(pem);
            }
            catch (Exception ex)
            {
                // The message is safe to show: the key itself isn't part of it
                throw new CommandException($"The Signing Key can't be read : {ex.Message}");
            }
        }

        private static byte[] ReadPublicKey(string pem)
        {
            try
            {
                using (var key = System.Security.Cryptography.ECDsa.Create())
                {
                    key.ImportFromPem(pem);
                    return key.ExportSubjectPublicKeyInfo();
                }
            }
            catch (Exception ex)
            {
                throw new CommandException($"The Public Key can't be read : {ex.Message}");
            }
        }
    }
}
