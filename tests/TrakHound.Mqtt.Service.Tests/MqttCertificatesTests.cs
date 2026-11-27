// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TrakHound.Services;

namespace TrakHound.Mqtt.Service.Tests
{
    /// <summary>
    /// Loading the Client and CA Certificates, checking the Broker's certificate against the CA, and the TLS handshake with a local server
    /// </summary>
    public class MqttCertificatesTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "trakhound-mqtt-tests", Guid.NewGuid().ToString());


        public MqttCertificatesTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }


        [Fact]
        public void PemCertificateAndKeyFiles_Load()
        {
            using var ca = CreateCa("Test CA");
            using var client = CreateSigned(ca, "client");

            var certificatePath = Write("client.pem", client.ExportCertificatePem());
            var keyPath = Write("client.key", client.GetRSAPrivateKey().ExportPkcs8PrivateKeyPem());

            using var loaded = MqttCertificates.LoadClientCertificate(certificatePath, keyPath, null);
            Assert.True(loaded.HasPrivateKey);
            Assert.Equal(client.Thumbprint, loaded.Thumbprint);
        }

        [Fact]
        public void PemText_WithTheKey_Loads()
        {
            using var ca = CreateCa("Test CA");
            using var client = CreateSigned(ca, "client");

            var pem = client.ExportCertificatePem() + "\n" + client.GetRSAPrivateKey().ExportPkcs8PrivateKeyPem();

            using var loaded = MqttCertificates.LoadClientCertificate(pem, null, null);
            Assert.True(loaded.HasPrivateKey);
            Assert.Equal(client.Thumbprint, loaded.Thumbprint);
        }

        [Fact]
        public void EncryptedPemKey_LoadsWithThePassword()
        {
            using var ca = CreateCa("Test CA");
            using var client = CreateSigned(ca, "client");

            var parameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);
            var certificatePath = Write("client.pem", client.ExportCertificatePem());
            var keyPath = Write("client.key", client.GetRSAPrivateKey().ExportEncryptedPkcs8PrivateKeyPem("secret", parameters));

            using var loaded = MqttCertificates.LoadClientCertificate(certificatePath, keyPath, "secret");
            Assert.True(loaded.HasPrivateKey);
        }

        [Fact]
        public void Pkcs12File_LoadsWithThePassword()
        {
            using var ca = CreateCa("Test CA");
            using var client = CreateSigned(ca, "client");

            var path = Path.Combine(_directory, "client.pfx");
            File.WriteAllBytes(path, client.Export(X509ContentType.Pkcs12, "secret"));

            using var loaded = MqttCertificates.LoadClientCertificate(path, null, "secret");
            Assert.True(loaded.HasPrivateKey);
            Assert.Equal(client.Thumbprint, loaded.Thumbprint);
        }

        [Fact]
        public void PemCertificate_WithoutAKey_Throws()
        {
            using var ca = CreateCa("Test CA");
            using var client = CreateSigned(ca, "client");

            var certificatePath = Write("client.pem", client.ExportCertificatePem());

            Assert.ThrowsAny<CryptographicException>(() => MqttCertificates.LoadClientCertificate(certificatePath, null, null));
        }

        [Fact]
        public void CaFile_WithSeveralCertificates_LoadsThemAll()
        {
            using var first = CreateCa("First CA");
            using var second = CreateCa("Second CA");

            var path = Write("ca.pem", first.ExportCertificatePem() + "\n" + second.ExportCertificatePem());

            var loaded = MqttCertificates.LoadCaCertificates(path);
            Assert.Equal(2, loaded.Count);
        }

        [Fact]
        public void BrokerCertificate_FromTheCa_IsAccepted()
        {
            using var ca = CreateCa("Test CA");
            using var broker = CreateSigned(ca, "broker");

            // The system doesn't trust the CA, so the policy errors include a chain error that the CA check replaces
            Assert.True(MqttCertificates.ValidateBrokerCertificate(broker, null, SslPolicyErrors.RemoteCertificateChainErrors, Collection(ca)));
        }

        [Fact]
        public void BrokerCertificate_FromAnotherCa_IsRefused()
        {
            using var ca = CreateCa("Test CA");
            using var other = CreateCa("Other CA");
            using var broker = CreateSigned(other, "broker");

            Assert.False(MqttCertificates.ValidateBrokerCertificate(broker, null, SslPolicyErrors.RemoteCertificateChainErrors, Collection(ca)));
        }

        [Fact]
        public void BrokerCertificate_WithTheWrongName_IsRefused()
        {
            using var ca = CreateCa("Test CA");
            using var broker = CreateSigned(ca, "broker");

            Assert.False(MqttCertificates.ValidateBrokerCertificate(broker, null, SslPolicyErrors.RemoteCertificateNameMismatch, Collection(ca)));
        }

        [Fact]
        public void Certificates_TurnOnTls()
        {
            Assert.False(new MqttConnectionConfiguration { Hostname = "localhost" }.IsTls);
            Assert.True(new MqttConnectionConfiguration { Hostname = "localhost", ClientCertificate = "client.pem" }.IsTls);
            Assert.True(new MqttConnectionConfiguration { Hostname = "localhost", CaCertificate = "ca.pem" }.IsTls);
        }

        [Fact]
        public async Task MissingCertificateFile_Returns503()
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "mqtt-certificate", Name = "mqtt-certificate" };
            configuration.SetParameter("hostname", "127.0.0.1");
            configuration.SetParameter("clientCertificate", Path.Combine(_directory, "missing.pem"));
            using var service = new MqttMessageService(configuration);

            using var content = new MemoryStream(new byte[] { 1 });
            var response = await service.Publish("test", content, false, 0);
            Assert.Equal(503, response.StatusCode);

            var availability = await service.CheckAvailability();
            Assert.False(availability.Result.IsAvailable);
            Assert.Contains("Certificate Error", availability.Result.Message);
        }

        [Fact]
        public async Task TlsHandshake_SendsTheClientCertificate_AndChecksTheBrokerAgainstTheCa()
        {
            using var ca = CreateCa("Test CA");
            using var client = CreateSigned(ca, "client");
            using var broker = CreateSigned(ca, "127.0.0.1", IPAddress.Loopback);

            // PEM files, as AWS IoT Core and most Brokers' tools create them
            var certificatePath = Write("client.pem", client.ExportCertificatePem());
            var keyPath = Write("client.key", client.GetRSAPrivateKey().ExportPkcs8PrivateKeyPem());
            var caPath = Write("ca.pem", ca.ExportCertificatePem());

            var (isAvailable, received) = await Connect(broker, certificatePath, keyPath, caPath);
            Assert.True(isAvailable);
            Assert.NotNull(received);
            Assert.Equal(client.Thumbprint, received.Thumbprint);
        }

        [Fact]
        public async Task TlsHandshake_WithABrokerFromAnotherCa_IsRefused()
        {
            using var ca = CreateCa("Test CA");
            using var other = CreateCa("Other CA");
            using var client = CreateSigned(ca, "client");
            using var broker = CreateSigned(other, "127.0.0.1", IPAddress.Loopback);

            var certificatePath = Write("client.pem", client.ExportCertificatePem());
            var keyPath = Write("client.key", client.GetRSAPrivateKey().ExportPkcs8PrivateKeyPem());
            var caPath = Write("ca.pem", ca.ExportCertificatePem());

            // The client refuses the Broker, so it never connects
            var (isAvailable, _) = await Connect(broker, certificatePath, keyPath, caPath);
            Assert.False(isAvailable);
        }


        /// <summary>
        /// Runs a TLS server that accepts one MQTT connection, and checks the Service's availability against it.
        /// Returns whether the Service connected, and the Client Certificate the server received
        /// </summary>
        private static async Task<(bool IsAvailable, X509Certificate2 Received)> Connect(X509Certificate2 brokerCertificate, string certificatePath, string keyPath, string caPath)
        {
            // Windows (SChannel) can't use an ephemeral key for the server either
            using var serverCertificate = X509CertificateLoader.LoadPkcs12(brokerCertificate.Export(X509ContentType.Pkcs12), null);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var server = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync();
                using var ssl = new SslStream(tcp.GetStream());
                X509Certificate2 received = null;
                try
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = serverCertificate,
                        ClientCertificateRequired = true,
                        RemoteCertificateValidationCallback = (s, certificate, chain, errors) =>
                        {
                            if (certificate != null) received = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                            return true;
                        }
                    });

                    // Read the CONNECT packet (its Remaining Length is a variable byte integer) and accept it with a CONNACK
                    var header = new byte[1];
                    await ssl.ReadExactlyAsync(header);
                    int length = 0, multiplier = 1, next;
                    do
                    {
                        var b = new byte[1];
                        await ssl.ReadExactlyAsync(b);
                        next = b[0];
                        length += (next & 127) * multiplier;
                        multiplier *= 128;
                    } while ((next & 128) != 0);
                    await ssl.ReadExactlyAsync(new byte[length]);
                    await ssl.WriteAsync(new byte[] { 0x20, 0x02, 0x00, 0x00 });
                    await ssl.FlushAsync();

                    // Keep the connection until the client disconnects
                    try { while (await ssl.ReadAsync(new byte[256]) > 0) { } } catch { }
                }
                catch { }
                return received;
            });

            try
            {
                var configuration = new TrakHoundServiceConfiguration { Id = "mqtt-tls", Name = "mqtt-tls" };
                configuration.SetParameter("hostname", "127.0.0.1");
                configuration.SetParameter("port", port.ToString());
                configuration.SetParameter("timeout", "3000");
                configuration.SetParameter("clientCertificate", certificatePath);
                configuration.SetParameter("clientKey", keyPath);
                configuration.SetParameter("caCertificate", caPath);
                bool isAvailable;
                using (var service = new MqttMessageService(configuration))
                {
                    isAvailable = (await service.CheckAvailability()).Result.IsAvailable;
                }

                return (isAvailable, await server.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                listener.Stop();
            }
        }

        private string Write(string name, string content)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static X509Certificate2Collection Collection(X509Certificate2 certificate) => new X509Certificate2Collection(certificate);

        private static X509Certificate2 CreateCa(string name)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        }

        private static X509Certificate2 CreateSigned(X509Certificate2 ca, string name, IPAddress address = null)
        {
            var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") }, false));

            if (address != null)
            {
                var san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(address);
                request.CertificateExtensions.Add(san.Build());
            }

            var serial = RandomNumberGenerator.GetBytes(16);
            using var certificate = request.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29), serial);
            return certificate.CopyWithPrivateKey(key);
        }
    }
}
