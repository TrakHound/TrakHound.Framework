// Api request throughput : GET requests to the Functions Api (list and get by ID)
// k6 run -e BASE_URL=http://localhost:8495 tests/load/api.js

import http from 'k6/http';
import { check } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://localhost:8495';
const functionId = __ENV.FUNCTION_ID || 'TrakHound.Example.Function';
const apiKey = __ENV.API_KEY; // Sent as X-API-Key when the Apis require Authentication

export const options = {
    stages: [
        { duration: '10s', target: 50 },
        { duration: '40s', target: 50 },
        { duration: '5s', target: 0 },
    ],
    summaryTrendStats: ['avg', 'p(50)', 'p(95)', 'p(99)', 'max'],
};

const params = apiKey ? { headers: { 'X-API-Key': apiKey } } : {};

export default function () {
    const list = http.get(`${baseUrl}/functions/list`, Object.assign({ tags: { name: 'list' } }, params));
    check(list, { 'list 200': (r) => r.status === 200 });

    const get = http.get(`${baseUrl}/functions/${functionId}`, Object.assign({ tags: { name: 'get' } }, params));
    check(get, { 'get 200': (r) => r.status === 200 });
}
