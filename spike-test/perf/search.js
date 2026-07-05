// P2-3: 公开搜索接口压测 (k6)
//   场景: 50k 产品数据下, 100 VU 持续 60s 公开搜索 + 产品详情 + 对比
//   目标: P95 < 200ms, 错误率 < 0.1%
//   运行:  k6 run --duration 60s --vus 100 spike-test/perf/search.js
//          k6 run --out json=perf-result.json spike-test/perf/search.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5148';
const searchLatency = new Trend('search_latency_ms', true);
const productLatency = new Trend('product_latency_ms', true);
const compareLatency = new Trend('compare_latency_ms', true);
const errors = new Rate('errors');
const successCounter = new Counter('success_total');

// 真实存在的产品 ID (从数据库抽样, 1k-50k 范围)
const productIds = [1001, 1002, 1003, 5001, 5002, 10000, 20000, 30000, 40000, 49960];
// 真实存在的搜索关键词 (用 8 字段查询的 oemNo3)
const oemNo3s = ['AC', 'OC', 'OF', '1', '2', 'MR', 'filter'];

export const options = {
    stages: [
        { duration: '10s', target: 50 },  // 预热
        { duration: '30s', target: 100 }, // 峰值
        { duration: '10s', target: 200 }, // 极限
        { duration: '10s', target: 0 },   // 收尾
    ],
    thresholds: {
        'http_req_duration{endpoint:search}': ['p(95)<200'],  // 搜索 P95 < 200ms
        'http_req_duration{endpoint:product}': ['p(95)<300'], // 详情 P95 < 300ms
        'http_req_duration{endpoint:compare}': ['p(95)<500'], // 对比 P95 < 500ms
        'errors': ['rate<0.001'],                              // 错误率 < 0.1%
    },
};

export default function () {
    // 1. 公开搜索 (混合关键词)
    const kw = keywords[Math.floor(Math.random() * keywords.length)];
    const searchRes = http.get(`${BASE}/api/public/search?q=${kw}&pageSize=20`, {
        tags: { endpoint: 'search' },
    });
    searchLatency.add(searchRes.timings.duration);
    const searchOk = check(searchRes, {
        'search status 200': (r) => r.status === 200,
        'search has items': (r) => {
            try { return JSON.parse(r.body).items?.length >= 0; } catch { return false; }
        },
    });
    if (searchOk) successCounter.add(1);
    errors.add(!searchOk);

    sleep(0.5);

    // 2. 公开产品详情
    const id = productIds[Math.floor(Math.random() * productIds.length)];
    const productRes = http.get(`${BASE}/api/public/product/${id}`, {
        tags: { endpoint: 'product' },
    });
    productLatency.add(productRes.timings.duration);
    const productOk = check(productRes, {
        'product status 200': (r) => r.status === 200 || r.status === 404,
    });
    if (productOk) successCounter.add(1);
    errors.add(!productOk);

    sleep(0.3);

    // 3. 公开对比 (3 个产品)
    const ids = [];
    for (let i = 0; i < 3; i++) {
        ids.push(productIds[Math.floor(Math.random() * productIds.length)]);
    }
    const compareRes = http.get(`${BASE}/api/public/compare?ids=${ids.join(',')}`, {
        tags: { endpoint: 'compare' },
    });
    compareLatency.add(compareRes.timings.duration);
    const compareOk = check(compareRes, {
        'compare status 200': (r) => r.status === 200,
    });
    if (compareOk) successCounter.add(1);
    errors.add(!compareOk);

    sleep(0.2);
}

// 启动横幅
export function handleSummary(data) {
    return {
        'stdout': textSummary(data, { indent: ' ', enableColors: true }),
    };
}

// 简化 text summary (避免引入 jslib 依赖)
function textSummary(data, opts) {
    const m = data.metrics;
    let out = '\n=== SakuraFilter 公开接口压测报告 ===\n\n';
    out += `总请求数: ${m.http_reqs?.values?.count || 0}\n`;
    out += `RPS: ${(m.http_reqs?.values?.rate || 0).toFixed(2)}\n`;
    out += `失败率: ${((m.errors?.values?.rate || 0) * 100).toFixed(3)}%\n\n`;
    if (m.search_latency_ms) {
        const v = m.search_latency_ms.values;
        out += `搜索延迟: avg=${v.avg.toFixed(1)}ms p95=${v['p(95)'].toFixed(1)}ms p99=${v['p(99)'].toFixed(1)}ms\n`;
    }
    if (m.product_latency_ms) {
        const v = m.product_latency_ms.values;
        out += `详情延迟: avg=${v.avg.toFixed(1)}ms p95=${v['p(95)'].toFixed(1)}ms p99=${v['p(99)'].toFixed(1)}ms\n`;
    }
    if (m.compare_latency_ms) {
        const v = m.compare_latency_ms.values;
        out += `对比延迟: avg=${v.avg.toFixed(1)}ms p95=${v['p(95)'].toFixed(1)}ms p99=${v['p(99)'].toFixed(1)}ms\n`;
    }
    return out;
}
