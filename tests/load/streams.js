// Long-lived Api Streams : many WebSocket subscriptions to a Function Run's status (the same Stream Endpoint as Server-Sent Events)
// held open at the same time. Measures how quickly Streams are accepted and what each open Stream costs the Instance
// k6 run -e BASE_URL=http://localhost:8495 tests/load/streams.js

import ws from 'k6/ws';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:8495').replace(/^http/, 'ws');
const functionId = __ENV.FUNCTION_ID || 'TrakHound.Example.Function';
const holdSeconds = Number(__ENV.HOLD_SECONDS || 30);
const apiKey = __ENV.API_KEY;

const opened = new Counter('streams_opened');

export const options = {
    scenarios: {
        streams: {
            executor: 'ramping-vus',
            stages: [
                { duration: '15s', target: Number(__ENV.STREAMS || 500) },
                { duration: `${holdSeconds + 15}s`, target: Number(__ENV.STREAMS || 500) },
            ],
            gracefulRampDown: '5s',
        },
    },
    summaryTrendStats: ['avg', 'p(50)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
    const url = `${baseUrl}/functions/${functionId}/runs/loadtest-${__VU}/status`;
    const params = apiKey ? { headers: { 'X-API-Key': apiKey } } : {};

    const response = ws.connect(url, params, (socket) => {
        socket.on('open', () => opened.add(1));
        socket.setTimeout(() => socket.close(), holdSeconds * 1000);
    });

    check(response, { 'stream 101': (r) => r && r.status === 101 });
}
