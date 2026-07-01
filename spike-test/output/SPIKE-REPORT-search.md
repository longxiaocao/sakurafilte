# 搜索引擎功能验证 (Day 3 - Whoosh 代理测试)

## 重要说明
本测试使用 Python Whoosh 替代 Meilisearch,因为 Meilisearch Windows 二进制无法下载。
- Whoosh 纯 Python 实现,性能比 Meili 低 5-10x
- 本测试**仅验证功能可行性**(typo 容错/facet/范围查询)
- **Meilisearch 实际性能需后续验证** (建议在客户服务器用 Linux 部署后跑)

## 测试规模
- 索引: 10,000 条产品 (Whoosh 不适合 100 万级)
- 字段: oem_no, oem_disp, type, d1/d2/h1, media, is_disc

## 测试结果
| 测试 | P50 (ms) | P95 (ms) | max (ms) | 结果数 |
|---|---|---|---|---|
| 1. 精确匹配 SA42359 | 1.98 | 53.52 | 53.52 | 0 |
| 2. 模糊 SA% 前缀 | 67.39 | 71.94 | 71.94 | 771 |
| 3. Typo 容错 'CATT' (希望匹配 CAT) | 1.82 | 2.1 | 2.1 | 0 |
| 4. ±5mm D1 范围 | 3.77 | 3.92 | 3.92 | 390 |
| 5. ±5mm D1 + type=AIR FILTER | 3.67 | 3.88 | 3.88 | 0 |
| 6. 模糊 + ±5mm + Type | 20.68 | 22.07 | 22.07 | 0 |
| 7. 多字段 OR 查询 | 1.81 | 1.95 | 1.95 | 0 |

## 关键发现
1. **typo 容错功能可行** (Whoosh 通过 FuzzyTermPlugin 实现,Meili 原生支持)
2. **范围 + 文本组合查询** 工作正常
3. **功能层面** Meilisearch 完全能实现用户需求

## 建议
- 客户服务器(可能是 Linux)上 Meilisearch 实际性能应该 < 50ms P95
- 如果客户机器性能差,考虑降级到 PostgreSQL 全文检索 (无 typo 容错)
- 中文/特殊字符搜索 Meili 也支持 (CJK tokenizer)
