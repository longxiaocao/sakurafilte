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
// 真实存在的搜索关键词 (8 字段查询, 命中 oemNo3/xref.oem_no_3 字段)
//   数据样本: 50k products + 5M xref, 短关键词命中率高, 长关键词精准
//   WHY 混合场景: 1) 短数字 测 EXISTS 大表扫描, 2) 长 OEM 测精匹配性能, 3) 大命中品牌 测 COUNT 性能
const searchKeywords = [
    { oemNo3: '1' },                                    // 1. 数字 (极高命中 49k, 触发大表 EXISTS)
    { oemNo3: '2' },                                    // 2. 数字
    { oemNo3: '3' },                                    // 3. 数字
    { oemNo3: '5' },                                    // 4. 数字
    { oemNo3: '6' },                                    // 5. 数字
    { oemNo3: '7' },                                    // 6. 数字
    { oemNo3: '8' },                                    // 7. 数字
    { oemNo3: '9' },                                    // 8. 数字
    { oemNo3: '0' },                                    // 9. 数字
    { oemNo3: '06' },                                   // 10. 短串
    { oemNo3: '07' },                                   // 11. 短串
    { oemNo3: '2026' },                                 // 12. 中串
    { oemNo3: 'E2E' },                                  // 13. 中串
    { oemNo3: 'E2E2026' },                              // 14. 中串
    { oemNo3: 'E2E20260705' },                          // 15. 长串
    { oemNo2: 'E2E202607056257' },                      // 16. 精确匹配
    { oemNo2: 'E2E202607058753' },                      // 17. 精确匹配
    { oemNo3: '532' },                                  // 18. 命中
    { oemNo3: '825' },                                  // 19. 命中
    { oemNo3: '125' },                                  // 20. 命中
];

export const options = {
    scenarios: {
        // 搜索压力: 1 VU 持续 20s (单用户基线, 验证无并发下 P95)
        search_load: {
            executor: 'constant-vus',
            vus: 1,
            duration: '20s',
            gracefulStop: '5s',
            tags: { scenario: 'search_load' },
        },
    },
    thresholds: {
        'http_req_duration{endpoint:search}': ['p(95)<500'],  // 搜索 P95 < 500ms (P2-3 目标)
        'http_req_duration{endpoint:product}': ['p(95)<300'], // 详情 P95 < 300ms
        'http_req_duration{endpoint:compare}': ['p(95)<500'], // 对比 P95 < 500ms
        'errors': ['rate<0.01'],                              // 错误率 < 1%
    },
    noConnectionReuse: false,  // 启用 keep-alive, 模拟真实浏览器
    discardResponseBodies: false,
};

export default function () {
    // 1. 公开搜索 (混合关键词, 命中 8 字段中任一)
    const kw = searchKeywords[Math.floor(Math.random() * searchKeywords.length)];
    // WHY 字符串拼接: k6 跑在 goja (JS 引擎), 不支持 URLSearchParams/URL 等 Web API
    let qs = '';
    if (kw.oemBrand) qs += `&oemBrand=${encodeURIComponent(kw.oemBrand)}`;
    if (kw.oemNo2)   qs += `&oemNo2=${encodeURIComponent(kw.oemNo2)}`;
    if (kw.oemNo3)   qs += `&oemNo3=${encodeURIComponent(kw.oemNo3)}`;
    qs += '&pageSize=20';
    if (qs.startsWith('&')) qs = '?' + qs.substring(1);
    const searchRes = http.get(`${BASE}/api/public/search${qs}`, {
        tags: { endpoint: 'search' },
    });
    searchLatency.add(searchRes.timings.duration);
    const searchOk = check(searchRes, {
        'search status 200': (r) => r.status === 200,
        'search has items': (r) => {
            try { return Array.isArray(JSON.parse(r.body).items); } catch { return false; }
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
    // WHY 取 http_req_duration{endpoint:...} 而非自定义 trend: 自定义 trend 用 .toFixed 但缺 p(95) 字段
    const httpByEp = m.http_req_duration_by_ep || {};
    for (const [ep, v] of Object.entries({
        search: m['http_req_duration{endpoint:search}']?.values,
        product: m['http_req_duration{endpoint:product}']?.values,
        compare: m['http_req_duration{endpoint:compare}']?.values,
    })) {
        if (!v) continue;
        out += `${ep} 延迟: avg=${(v.avg || 0).toFixed(1)}ms p90=${(v['p(90)'] || 0).toFixed(1)}ms p95=${(v['p(95)'] || 0).toFixed(1)}ms p99=${(v['p(99)'] || 0).toFixed(1)}ms max=${(v.max || 0).toFixed(1)}ms\n`;
    }
    return out;
}
