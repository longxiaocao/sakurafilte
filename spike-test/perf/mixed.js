// P2-3: 公开/管理 混合压测 — 模拟真实用户流量分布
//   公开:80%  /  管理:20%
//   用于生产容量评估

import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5148';
const TOKEN = __ENV.ADMIN_TOKEN || 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C';

export const options = {
    stages: [
        { duration: '15s', target: 100 },
        { duration: '60s', target: 300 },
        { duration: '15s', target: 0 },
    ],
    thresholds: {
        'http_req_failed': ['rate<0.01'],  // 整体错误率 < 1%
        'http_req_duration': ['p(95)<500'],
    },
};

export default function () {
    const isPublic = Math.random() < 0.8;
    if (isPublic) {
        // 公开流量
        const action = Math.random();
        if (action < 0.6) {
            // 60% 搜索
            const kw = ['AC', 'OC', 'filter', 'BMW', 'TOYOTA'][Math.floor(Math.random() * 5)];
            http.get(`${BASE}/api/public/search?oemNo3=${kw}&pageSize=20`);
        } else if (action < 0.9) {
            // 30% 详情
            const id = Math.floor(Math.random() * 50000) + 1;
            http.get(`${BASE}/api/public/product/${id}`);
        } else {
            // 10% 对比
            const ids = [
                Math.floor(Math.random() * 50000) + 1,
                Math.floor(Math.random() * 50000) + 1,
                Math.floor(Math.random() * 50000) + 1
            ];
            http.get(`${BASE}/api/public/compare?ids=${ids.join(',')}`);
        }
    } else {
        // 管理流量
        const headers = { 'X-Admin-Token': TOKEN };
        const action = Math.random();
        if (action < 0.5) {
            // 50% 产品列表
            http.get(`${BASE}/api/admin/products?pageSize=20`, { headers });
        } else if (action < 0.8) {
            // 30% 字典查询
            const dict = ['types', 'engines', 'medias'][Math.floor(Math.random() * 3)];
            http.get(`${BASE}/api/admin/${dict}`, { headers });
        } else {
            // 20% ETL 状态
            http.get(`${BASE}/api/etl/status`, { headers });
        }
    }
    sleep(0.1 + Math.random() * 0.3);
}
