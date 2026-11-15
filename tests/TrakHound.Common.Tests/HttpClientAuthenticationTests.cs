// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Http;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The Authentication Headers that a TrakHoundHttpClient sends with each Request
    /// </summary>
    public class HttpClientAuthenticationTests
    {
        [Fact]
        public void No_Headers_Are_Sent_Without_Credentials()
        {
            Assert.Null(new TrakHoundHttpClientConfiguration().GetHeaders());
            Assert.Null(new TrakHoundHttpRequestSender(new TrakHoundHttpClientConfiguration()).GetHeaders());
        }

        [Fact]
        public void Credentials_Are_Sent_In_Their_Headers()
        {
            var configuration = new TrakHoundHttpClientConfiguration { ApiKey = "thk_a_b", BearerToken = "token", ModuleToken = "thm_c" };

            var headers = configuration.GetHeaders();

            Assert.Equal("thk_a_b", headers["X-API-Key"]);
            Assert.Equal("Bearer token", headers["Authorization"]);
            Assert.Equal("thm_c", headers["X-TrakHound-Module-Token"]);
        }

        [Fact]
        public void Additional_Headers_Override_The_Credentials()
        {
            var configuration = new TrakHoundHttpClientConfiguration { BearerToken = "token" };
            configuration.Headers = new Dictionary<string, string> { ["authorization"] = "Basic abc", ["X-Custom"] = "1" };

            var headers = configuration.GetHeaders();

            Assert.Equal("Basic abc", headers["Authorization"]);
            Assert.Equal("1", headers["X-Custom"]);
        }

        [Fact]
        public void Request_Headers_Override_The_Configuration()
        {
            var configuration = new TrakHoundHttpClientConfiguration { ApiKey = "thk_a_b" };
            var sender = new TrakHoundHttpRequestSender(configuration);

            Assert.Equal("thk_a_b", sender.GetHeaders()["X-API-Key"]);

            var headers = sender.GetHeaders(new Dictionary<string, string> { ["x-api-key"] = "thk_c_d", ["Accept"] = "text/plain" });
            Assert.Equal("thk_c_d", headers["X-API-Key"]);
            Assert.Equal("text/plain", headers["Accept"]);

            // Changes to the Configuration apply to the next Request
            configuration.ApiKey = "thk_e_f";
            Assert.Equal("thk_e_f", sender.GetHeaders()["X-API-Key"]);
        }

        [Theory]
        [InlineData("http://localhost:8472/", "localhost", 8472, "/", false)]
        [InlineData("https://example.com/trakhound/", "example.com", 443, "/trakhound/", true)]
        [InlineData("http://127.0.0.1:9000", "127.0.0.1", 9000, "/", false)]
        public void Configurations_Are_Created_From_Urls(string url, string hostname, int port, string path, bool useSSL)
        {
            var configuration = TrakHoundHttpClientConfiguration.FromUrl(url);

            Assert.Equal(hostname, configuration.Hostname);
            Assert.Equal(port, configuration.Port);
            Assert.Equal(path, configuration.Path);
            Assert.Equal(useSSL, configuration.UseSSL);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("localhost:8472")]
        [InlineData("ftp://localhost/")]
        public void Invalid_Urls_Are_Rejected(string url)
        {
            Assert.Null(TrakHoundHttpClientConfiguration.FromUrl(url));
        }

        [Fact]
        public void A_Module_Token_Connects_As_The_Module()
        {
            var token = new TrakHoundModuleToken { Token = "thm_abc", InstanceUrl = "http://127.0.0.1:8472/", RouterId = "test" };

            var variables = token.GetEnvironmentVariables();
            Assert.Equal("http://127.0.0.1:8472/", variables[TrakHoundHttpConstants.InstanceUrlVariable]);
            Assert.Equal("thm_abc", variables[TrakHoundHttpConstants.ModuleTokenVariable]);
            Assert.Equal("test", variables[TrakHoundHttpConstants.RouterIdVariable]);

            var configuration = token.GetClientConfiguration();
            Assert.Equal("127.0.0.1", configuration.Hostname);
            Assert.Equal("thm_abc", configuration.GetHeaders()["X-TrakHound-Module-Token"]);
        }
    }
}
