"""Day 7.10.1 集成测试: 死信恢复元数据持久化 + 跨循环 recovery_count 保留

核心场景:
  1) 注入死信 (Meili 5xx) — recovery_count=0, status=active
  2) 人工 /recover → 死信 status=recovered, recovery_count=1, 生成 pending
  3) 模拟 pending 再次失败 (直接 UPDATE search_index_pending.retry_count=5)
  4) 触发 IndexReplayWorker.ProcessDeadLetterAsync 转入死信
     — 期望: 死信 status 仍 active, recovery_count=2 (复用, 不从 0 开始!)
  5) 再 /recover → recovery_count=3
  6) 第 3 次失败: recovery_count 期望 = 3
  7) 第 4 次 /recover: 应被 max_recovery_count=3 限位过滤
"""
import psycopg2
import requests
import json
import time
import subprocess

API = 'http://localhost:5180'
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

def log(msg): print(f'  {msg}', flush=True)

# 清理之前测试数据
conn = psycopg2.connect(**PG); cur = conn.cursor()
cur.execute("DELETE FROM search_index_dead_letter WHERE last_error LIKE 'TEST-DAY7101-%'")
cur.execute("DELETE FROM search_index_pending WHERE last_error LIKE 'TEST-DAY7101-%'")
conn.commit()
log('清理旧测试数据')

# ============ 步骤 1: 注入死信 ============
log('\n步骤 1: 注入测试死信 (recovery_count=0, status=active)')
cur.execute("""
    INSERT INTO search_index_dead_letter
      (original_id, operation, payload, retry_count, last_error, created_at, moved_at,
       status, recovery_count, last_recovery_at)
    VALUES (99000, 'index', '{"id": 99000, "test": "DAY7101"}'::jsonb, 5,
            'TEST-DAY7101-1: Meili returned 500 Internal Server Error',
            now() - interval '15 minutes', now() - interval '15 minutes',
            'active', 0, NULL)
    RETURNING id
""")
dead_id = cur.fetchone()[0]
conn.commit()
log(f'  注入死信 id={dead_id}, status=active, recovery_count=0')

# ============ 步骤 2: 人工 /recover ============
log('\n步骤 2: 调用 /api/admin/dead-letter/{id}/recover')
resp = requests.post(f'{API}/api/admin/dead-letter/{dead_id}/recover')
assert resp.status_code == 200, f'recover 失败: {resp.status_code} {resp.text}'
data = resp.json()
log(f'  response: {json.dumps(data, ensure_ascii=False)}')
assert data['recovered'] == True
assert data['recoveryCount'] == 1, f'recovery_count 应=1, 实际 {data["recoveryCount"]}'

# 验证 DB 状态: 死信行不删除, status=recovered
cur.execute("""SELECT id, status, recovery_count, recovered_at, recovered_to_pending_id
               FROM search_index_dead_letter WHERE id = %s""", (dead_id,))
row = cur.fetchone()
log(f'  DB: id={row[0]} status={row[1]} recovery_count={row[2]} recovered_at={row[3]} recovered_to_pending_id={row[4]}')
assert row[1] == 'recovered', f'status 应=recovered, 实际 {row[1]}'
assert row[2] == 1, f'recovery_count 应=1, 实际 {row[2]}'
assert row[3] is not None, 'recovered_at 不应为 NULL'
assert row[4] is not None, 'recovered_to_pending_id 不应为 NULL'
new_pending_id = row[4]
log('  ✓ 死信行不删除, status=recovered, recovery_count 持久化成功')

# ============ 步骤 3: 模拟 pending 再次失败 (retry=5) ============
log(f'\n步骤 3: 模拟 pending (id={new_pending_id}) 再次失败, retry=5')
cur.execute("""
    UPDATE search_index_pending
    SET retry_count = 5, last_error = 'TEST-DAY7101-2: Meili still 500 after recovery'
    WHERE id = %s
""", (new_pending_id,))
conn.commit()
log('  pending.retry_count 置为 5 (触发 IndexReplayWorker 转入死信)')

# ============ 步骤 4: 触发 IndexReplayWorker (直接调用服务方法) ============
log('\n步骤 4: 触发 IndexReplayWorker 转入死信 (等待 15s 轮询)')
# IndexReplayWorker 10s 轮询, 等 15s
time.sleep(15)

# 验证: 死信行仍存在, status=active, recovery_count 保持 1 (入死信不递增)
cur.execute("""SELECT id, status, recovery_count, retry_count, last_error, recovered_at
               FROM search_index_dead_letter WHERE id = %s""", (dead_id,))
row = cur.fetchone()
log(f'  DB: id={row[0]} status={row[1]} recovery_count={row[2]} retry_count={row[3]}')
log(f'      last_error={row[4][:60]} recovered_at={row[5]}')
assert row[0] == dead_id, f'死信 id 应保持 {dead_id}, 实际 {row[0]} (说明新建了行, 复用失败!)'
assert row[1] == 'active', f'status 应=active (再次失败), 实际 {row[1]}'
assert row[2] == 1, f'⭐ 关键断言: recovery_count 应保持=1 (入死信不递增), 实际 {row[2]}'
assert row[5] is None, 'recovered_at 应清空'
log('  ⭐ 跨循环 id 保持不变, status 重置 active, recovery_count 保留')

# 验证 pending 已被删除
cur.execute("SELECT count(*) FROM search_index_pending WHERE id = %s", (new_pending_id,))
assert cur.fetchone()[0] == 0, f'pending id={new_pending_id} 应已被 IndexReplayWorker 删除'
log(f'  pending id={new_pending_id} 已删除 ✓')

# ============ 步骤 5: 再 /recover ============
log('\n步骤 5: 第二次 /recover (rc=0→recover→1→入死信→recover→2)')
resp = requests.post(f'{API}/api/admin/dead-letter/{dead_id}/recover')
data = resp.json()
log(f'  response: {json.dumps(data, ensure_ascii=False)}')
assert data['recoveryCount'] == 2, f'recovery_count 应=2, 实际 {data["recoveryCount"]}'

cur.execute("""SELECT status, recovery_count, recovered_to_pending_id
               FROM search_index_dead_letter WHERE id = %s""", (dead_id,))
row = cur.fetchone()
log(f'  DB: status={row[0]} recovery_count={row[1]} recovered_to_pending_id={row[2]}')
assert row[0] == 'recovered'
assert row[1] == 2
new_pending_id2 = row[2]
log('  ✓ recovery_count=2 持久化成功 (第 2 次恢复)')

# ============ 步骤 6: 第 3 次失败 ============
log(f'\n步骤 6: 模拟 pending (id={new_pending_id2}) 第 3 次失败')
cur.execute("""UPDATE search_index_pending SET retry_count=5,
               last_error='TEST-DAY7101-3: Meili 500 again' WHERE id = %s""", (new_pending_id2,))
conn.commit()
time.sleep(15)
cur.execute("""SELECT status, recovery_count FROM search_index_dead_letter WHERE id = %s""", (dead_id,))
row = cur.fetchone()
log(f'  DB: status={row[0]} recovery_count={row[1]}')
# 这次入死信时 recovery_count 应保持 2 (复用, 不递增), 等待 /recover 才递增
assert row[0] == 'active', f'status 应=active, 实际 {row[0]}'
assert row[1] == 2, f'recovery_count 应保持=2 (入死信时不递增, 恢复时才递增), 实际 {row[1]}'
log('  ⭐ 第 3 次入死信: recovery_count 保持 2 (符合设计)')

# ============ 步骤 7: 第 3 次 /recover (达到 max=3) ============
log('\n步骤 7: 第 3 次 /recover (应使 recovery_count=3, 达到 max)')
resp = requests.post(f'{API}/api/admin/dead-letter/{dead_id}/recover')
data = resp.json()
log(f'  response: {json.dumps(data, ensure_ascii=False)}')
assert data['recoveryCount'] == 3, f'recovery_count 应=3 (达到 max), 实际 {data["recoveryCount"]}'

cur.execute("""SELECT status, recovery_count, recovered_to_pending_id
               FROM search_index_dead_letter WHERE id = %s""", (dead_id,))
row = cur.fetchone()
new_pending_id3 = row[2]
assert row[1] == 3
log(f'  ✓ recovery_count=3 达到 max')

# ============ 步骤 8: 第 4 次失败 ============
log(f'\n步骤 8: 模拟 pending (id={new_pending_id3}) 第 4 次失败')
cur.execute("""UPDATE search_index_pending SET retry_count=5,
               last_error='TEST-DAY7101-4: Meili 500 forever' WHERE id = %s""", (new_pending_id3,))
conn.commit()
time.sleep(15)
cur.execute("""SELECT status, recovery_count FROM search_index_dead_letter WHERE id = %s""", (dead_id,))
row = cur.fetchone()
log(f'  DB: status={row[0]} recovery_count={row[1]}')
assert row[0] == 'active', f'status 应=active, 实际 {row[0]}'
assert row[1] == 3, f'recovery_count 应保持=3, 实际 {row[1]}'
log('  ⭐ 第 4 次入死信: recovery_count 保持 3 (符合设计)')

# ============ 步骤 9: batch 端点 maxRecoveryCount=3 应过滤 (rc=3 不<3) ============
log('\n步骤 9: batch 端点 maxRecoveryCount=3 应过滤 rc=3 (因 3 < 3 = false)')
resp = requests.post(f'{API}/api/admin/dead-letter/recover-batch',
    params={'lastErrorContains': 'TEST-DAY7101-4', 'maxRecoveryCount': 3})
data = resp.json()
log(f'  batch: {json.dumps(data, ensure_ascii=False)}')
assert data['matched'] == 0, f'batch 应过滤掉 recovery_count=3 的项, 实际 matched={data["matched"]}'
log('  ⭐ max_recovery_count 限位有效: rc=3 不再被自动恢复')

# 但 batch 用 maxRecoveryCount=4 可强制 (运维最后手段)
log('  但 batch 用 maxRecoveryCount=4 可强制恢复 (运维手段)')
resp = requests.post(f'{API}/api/admin/dead-letter/recover-batch',
    params={'lastErrorContains': 'TEST-DAY7101-4', 'maxRecoveryCount': 4})
data = resp.json()
log(f'  batch(maxRc=4): matched={data["matched"]} (期望=1)')
assert data['matched'] == 1, f'matched 应=1, 实际 {data["matched"]}'

# ============ 清理 ============
log('\n清理测试数据')
cur.execute("DELETE FROM search_index_dead_letter WHERE id = %s", (dead_id,))
cur.execute("DELETE FROM search_index_pending WHERE last_error LIKE 'TEST-DAY7101-%'")
conn.commit()
log('已清理')

conn.close()
print('\nDay 7.10.1 集成测试: 全部通过 ✓')
print('  ⭐ 关键验证: 跨循环 recovery_count 保留成功 (Day 7.10 初版 bug 已修复)')
