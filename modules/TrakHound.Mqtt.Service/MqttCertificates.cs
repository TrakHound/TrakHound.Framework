// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TrakHound.Mqtt.Service
{
    /// <summary>
    /// The certificates of a connection: the Client Certificate it authenticates with and the CA Certificates its Broker's certificate must chain to.
    /// They are loaded once per Service, so every connection (and reconnection) uses the same certificate objects.
    /// </summary>
    internal class MqttCertificates
    {
        private const string PemHeader = "-----BEGIN";


        public X509Certificate2 ClientCertificate { get; init; }

        public X509Certificate2Collection CaCertificates { get; init; }


        /// <summary>
        /// Loads the certificates set in <paramref name="configuration"/>. Returns null when none are set
        /// </summary>
        public static MqttCertificates Load(MqttConnectionConfiguration configuration)
        {
            if (configuration == null) return null;
            if (string.IsNullOrEmpty(configuration.ClientCertificate) && string.IsNullOrEmpty(configuration.CaCertificate)) return null;

            return new MqttCertificates
            {
                ClientCertificate = !string.IsNullOrEmpty(configuration.ClientCertificate)
                    ? LoadClientCertificate(configuration.ClientCertificate, configuration.ClientKey, configuration.ClientCertificatePassword)
                    : null,
                CaCertificates = !string.IsNullOrEmpty(configuration.CaCertificate)
                    ? LoadCaCertificates(configuration.CaCertificate)
                    : null
            };
        }

        /// <summary>
        /// Loads a Client Certificate with its private key from a PKCS#12 file (.pfx or .p12), or from PEM (a file path or the PEM text).
        /// For PEM, the key is read from <paramref name="key"/>, or from the certificate's PEM when no key is set
        /// </summary>
        public static X509Certificate2 LoadClientCertificate(string certificate, string key, string password)
        {
            if (!IsPem(certificate) && IsPkcs12File(certificate))
            {
                var pkcs12 = X509CertificateLoader.LoadPkcs12FromFile(GetPath(certificate), password);
                if (!pkcs12.HasPrivateKey) throw new InvalidOperationException("The Client Certificate has no private key");
                return pkcs12;
            }

            var certificatePem = ReadPem(certificate);
            var keyPem = !string.IsNullOrEmpty(key) ? ReadPem(key) : certificatePem;

            var loaded = keyPem.Contains("ENCRYPTED PRIVATE KEY")
                ? X509Certificate2.CreateFromEncryptedPem(certificatePem, keyPem, password)
                : X509Certificate2.CreateFromPem(certificatePem, keyPem);

            if (OperatingSystem.IsWindows())
            {
                // Windows (SChannel) can't use the ephemeral key of a certificate created from PEM for TLS, so it's reloaded through PKCS#12
                using (loaded)
                {
                    return X509CertificateLoader.LoadPkcs12(loaded.Export(X509ContentType.Pkcs12), null);
                }
            }

            return loaded;
        }

        /// <summary>
        /// Loads the CA Certificates from PEM (a file path or the PEM text, with one or more certificates) or from a DER file
        /// </summary>
        public static X509Certificate2Collection LoadCaCertificates(string certificate)
        {
            var collection = new X509Certificate2Collection();

            if (IsPem(certificate))
            {
                collection.ImportFromPem(certificate);
            }
            else
            {
                var path = GetPath(certificate);
                var content = File.ReadAllText(path);
                if (content.Contains(PemHeader)) collection.ImportFromPem(content);
                else collection.Add(X509CertificateLoader.LoadCertificateFromFile(path));
            }

            if (collection.Count < 1) throw new InvalidOperationException("The CA Certificate file has no certificates");
            return collection;
        }

        /// <summary>
        /// Returns true if the Broker's certificate is valid for its hostname and chains to one of <paramref name="caCertificates"/>.
        /// The certificates the system trusts are not used, so only a Broker with a certificate from these CAs is accepted
        /// </summary>
        public static bool ValidateBrokerCertificate(X509Certificate certificate, X509Chain brokerChain, SslPolicyErrors errors, X509Certificate2Collection caCertificates)
        {
            if (certificate == null || caCertificates == null || caCertificates.Count < 1) return false;

            // Only the chain is checked against the CAs: a missing certificate or a hostname that doesn't match is never accepted
            if ((errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0) return false;

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

            // Private CAs usually don't publish revocation lists, so revocation isn't checked (as with mosquitto's --cafile)
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            foreach (var caCertificate in caCertificates)
            {
                // Only self-signed certificates can be roots. Intermediates in the file help build the chain
                if (caCertificate.SubjectName.RawData.AsSpan().SequenceEqual(caCertificate.IssuerName.RawData)) chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
                else chain.ChainPolicy.ExtraStore.Add(caCertificate);
            }

            // Intermediates the Broker sent
            if (brokerChain != null)
            {
                foreach (var element in brokerChain.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }

            if (certificate is X509Certificate2 certificate2) return chain.Build(certificate2);

            using var converted = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            return chain.Build(converted);
        }


        private static bool IsPem(string value) => value.TrimStart().StartsWith(PemHeader, StringComparison.Ordinal);

        private static bool IsPkcs12File(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".p12", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadPem(string value) => IsPem(value) ? value : File.ReadAllText(GetPath(value));

        private static string GetPath(string path) => Path.GetFullPath(path);
    }
}
