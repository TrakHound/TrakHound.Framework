// App page loads : GET an App page (the server prerender; HTTP tools can't drive the Blazor circuit that follows)
// k6 run -e BASE_URL=http://localhost:8495 -e APP_PATH=/ tests/load/app.js

import http from 'k6/http';
import { check } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://localhost:8495';
const appPath = __ENV.APP_PATH || '/';

export const options = {
    stages: [
        { duration: '10s', target: 20 },
        { duration: '40s', target: 20 },
        { duration: '5s', target: 0 },
    ],
    summaryTrendStats: ['avg', 'p(50)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
    const page = http.get(`${baseUrl}${appPath}`, { tags: { name: 'app page' } });
    check(page, {
        'page 200': (r) => r.status === 200,
        'page has stylesheets': (r) => typeof r.body === 'string' && r.body.includes('rel="stylesheet"'),
    });
}
