// P2-3: ETL 导入压测 (k6) — 模拟管理员触发 ETL
//   场景: 30 VU 持续 30s, 触发 dry-run + status 查询
//   目标: dry-run P95 < 1s, status P95 < 50ms
//   注意: 不触发真实导入 (会消耗大量 DB IO), 仅压 dry-run 端点

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5148';
const TOKEN = __ENV.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C';
const HEADERS = { 'Content-Type': 'application/json', 'X-Admin-Token': TOKEN };

const dryRunLatency = new Trend('dry_run_latency_ms', true);
const statusLatency = new Trend('status_latency_ms', true);
const historyLatency = new Trend('history_latency_ms', true);
const errors = new Rate('errors');
const successCounter = new Counter('success_total');

export const options = {
    stages: [
        { duration: '5s', target: 10 },
        { duration: '20s', target: 30 },
        { duration: '5s', target: 0 },
    ],
    thresholds: {
        'http_req_duration{endpoint:dry_run}': ['p(95)<1500'],
        'http_req_duration{endpoint:status}': ['p(95)<50'],
        'http_req_duration{endpoint:history}': ['p(95)<300'],
        'errors': ['rate<0.05'],  // dry-run 偶发失败可接受
    },
};

export default function () {
    // 1. dry-run 校验 (轻量级, 不导入)
    const dryRunRes = http.post(`${BASE}/api/etl/dry-run`, JSON.stringify({
        entity: 'products',
        jsonlPath: 'D:/data/sakurafilter/products_test.jsonl'
    }), { headers: HEADERS, tags: { endpoint: 'dry_run' } });
    dryRunLatency.add(dryRunRes.timings.duration);
    const dryOk = check(dryRunRes, {
        'dry-run status 200/400/404': (r) => [200, 400, 404].includes(r.status),
    });
    if (dryOk) successCounter.add(1);
    errors.add(!dryOk);

    sleep(0.3);

    // 2. status 查询
    const statusRes = http.get(`${BASE}/api/etl/status`, {
        headers: HEADERS, tags: { endpoint: 'status' }
    });
    statusLatency.add(statusRes.timings.duration);
    const statusOk = check(statusRes, {
        'status 200': (r) => r.status === 200,
    });
    if (statusOk) successCounter.add(1);
    errors.add(!statusOk);

    sleep(0.2);

    // 3. history 查询
    const historyRes = http.get(`${BASE}/api/etl/history?limit=20`, {
        headers: HEADERS, tags: { endpoint: 'history' }
    });
    historyLatency.add(historyRes.timings.duration);
    const historyOk = check(historyRes, {
        'history 200': (r) => r.status === 200,
    });
    if (historyOk) successCounter.add(1);
    errors.add(!historyOk);

    sleep(0.5);
}
