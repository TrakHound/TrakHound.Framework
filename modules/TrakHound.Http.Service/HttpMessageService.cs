// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Http.Service
{
    public class HttpMessageService : HttpService, ITrakHoundMessageService
    {
        protected override ITrakHoundServiceClient ServiceClient => Client?.Services.Messages;


        public HttpMessageService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public async Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string clientId, IEnumerable<string> topics, int qos, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<ITrakHoundStream<TrakHoundMessageResponse>>(NotConfiguredMessage);

            // Stream is a WebSocket connection to the remote Instance
            return Forward(await Client.Services.Messages.Subscribe(topics, clientId, qos, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain, int qos, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            return Forward(await Client.Services.Messages.Publish(topic, content, retain, qos, cancellationToken: cancellationToken), cancellationToken);
        }
    }
}
