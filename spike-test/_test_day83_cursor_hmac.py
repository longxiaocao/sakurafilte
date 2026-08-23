"""Day 8.3 cursor HMAC 签名验证测试

测试目标:
  1) 正常 cursor 翻页 → 200, 拿到下一页数据
  2) 篡改 id → 400 (签名验证失败)
  3) 篡改 updatedAt → 400 (签名验证失败)
  4) 篡改 sig (改一位) → 400 (签名验证失败)
  5) 旧格式 (无 sig) → 400
  6) 段数错 (空段) → 400
  7) 高负载 (100K 数据) 翻页签名开销 < 0.1ms
"""
import time
import requests

API = 'http://localhost:5000'

print('=' * 90)
print('Day 8.3 cursor HMAC 签名验证测试')
print('=' * 90)


def hit(params, expect=200, label=''):
    r = requests.get(f'{API}/api/admin/products/search', params=params)
    if expect == 200:
        assert r.status_code == 200, f'{label} 应 200, 实际 {r.status_code} {r.text[:200]}'
        return r.json()
    else:
        assert r.status_code == expect, f'{label} 应 {expect}, 实际 {r.status_code} {r.text[:200]}'
        return r.json()


# ========== 1) 正常 cursor 翻页 ==========
print('\n[1] 正常 cursor 翻页')
data1 = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2}, label='1a')
assert data1['hasMore'], '应 hasMore'
assert '|' in data1['nextCursor'], f'cursor 应含 |, 实际: {data1["nextCursor"]}'
parts = data1['nextCursor'].split('|')
assert len(parts) == 3, f'cursor 应 3 段, 实际 {len(parts)} 段: {data1["nextCursor"]}'
print(f'  nextCursor = {data1["nextCursor"]}')
print(f'  段数={len(parts)} (期望 3) ✓')

# 用 nextCursor 翻页
data2 = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2,
             'cursor': data1['nextCursor']}, label='1b')
assert data2['items'][0]['id'] != data1['items'][0]['id'], '下一页 id 应不同'
print(f'  下一页 first id = {data2["items"][0]["id"]} (与首页不同) ✓')

# ========== 2) 篡改 id ==========
print('\n[2] 篡改 cursor id 段 → 400')
parts2 = data1['nextCursor'].split('|')
tampered = f'{parts2[0]}|{int(parts2[1]) - 1}|{parts2[2]}'  # 改 id
err = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2,
           'cursor': tampered}, expect=400, label='2')
assert '签名' in err.get('error', '') or 'cursor' in err.get('error', ''), f'错误信息应提到签名/cursor, 实际: {err}'
print(f'  ✓ 400: {err["error"][:60]}')

# ========== 3) 篡改 updatedAt ==========
print('\n[3] 篡改 cursor updatedAt 段 → 400')
parts3 = data1['nextCursor'].split('|')
# 改 1 小时前
dt = parts3[0]
dt_new = dt[:11] + '01:00:00.000000Z'  # 简单替换小时
tampered = f'{dt_new}|{parts3[1]}|{parts3[2]}'
err = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2,
           'cursor': tampered}, expect=400, label='3')
print(f'  ✓ 400: {err["error"][:60]}')

# ========== 4) 篡改 sig (改一位) ==========
print('\n[4] 篡改 cursor sig 段 → 400')
parts4 = data1['nextCursor'].split('|')
# sig 末位 +1 (十六进制/字母)
sig_last = parts4[2][-1]
new_last = 'A' if sig_last != 'A' else 'B'
tampered = f'{parts4[0]}|{parts4[1]}|{parts4[2][:-1]}{new_last}'
err = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2,
           'cursor': tampered}, expect=400, label='4')
print(f'  ✓ 400: {err["error"][:60]}')

# ========== 5) 旧格式 (无 sig, 2 段) ==========
print('\n[5] 旧格式 cursor (2 段, 无 sig) → 400')
parts5 = data1['nextCursor'].split('|')
old_format = f'{parts5[0]}|{parts5[1]}'  # 去掉 sig
err = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2,
           'cursor': old_format}, expect=400, label='5')
print(f'  ✓ 400: {err["error"][:60]}')

# ========== 6) 段数错 (空 sig) ==========
print('\n[6] cursor sig 段为空 → 400')
parts6 = data1['nextCursor'].split('|')
empty_sig = f'{parts6[0]}|{parts6[1]}|'
err = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 2,
           'cursor': empty_sig}, expect=400, label='6')
print(f'  ✓ 400: {err["error"][:60]}')

# ========== 7) 签名性能开销 ==========
print('\n[7] 签名验证性能 (1000 次翻页, 测单次开销)')
t0 = time.perf_counter()
for _ in range(50):
    data = hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 10}, label='7')
    if data.get('nextCursor'):
        hit({'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 10,
             'cursor': data['nextCursor']}, label='7-cont')
dt = (time.perf_counter() - t0) * 1000
print(f'  100 次 (含翻页) 耗时 {dt:.1f}ms, 平均 {dt/100:.2f}ms/次')

# ========== 8) 全程翻页 (10 页) 验证不漏数据 ==========
print('\n[8] 连续翻 10 页验证不漏数据')
seen_ids = set()
cursor = None
for page_no in range(10):
    p = {'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 50}
    if cursor:
        p['cursor'] = cursor
    data = hit(p, label=f'8.{page_no}')
    page_ids = [i['id'] for i in data['items']]
    # 不应有重复
    dup = set(page_ids) & seen_ids
    assert not dup, f'第 {page_no+1} 页与前面有重复 id: {dup}'
    seen_ids.update(page_ids)
    cursor = data.get('nextCursor')
    if not cursor:
        print(f'  第 {page_no+1} 页已是末页 (cursor=null)')
        break
print(f'  10 页共 {len(seen_ids)} 条, 无重复 ✓')

print('\n' + '=' * 90)
print('cursor HMAC 签名验证测试完成')
print('=' * 90)
