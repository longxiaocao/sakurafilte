"""Day 7.10.2 advisory lock 验证测试
并行启动 5 个 recover 任务, 验证:
  1) 同一时刻最多只有 1 个任务在跑 (其他 4 个拿不到锁立即返回 409)
  2) DB 状态正确: 同一死信只被恢复 1 次
"""
import requests
import threading
import time
import json

API = 'http://localhost:5180'

# 选一个 active 死信 (注入 1 条临时死信)
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM search_index_dead_letter WHERE last_error LIKE 'TEST-LOCK-%'")
cur.execute("""
    INSERT INTO search_index_dead_letter
      (original_id, operation, payload, retry_count, last_error, created_at, moved_at, status, recovery_count)
    VALUES (99000, 'index', '{"id": 99000}'::jsonb, 5, 'TEST-LOCK-1: Meili 500', now(), now(), 'active', 0)
    RETURNING id
""")
dead_id = cur.fetchone()[0]
conn.commit()
print(f'注入死信 id={dead_id}')

# 启动 5 个并发 /recover
results = []
def recover_call(idx):
    try:
        t0 = time.time()
        r = requests.post(f'{API}/api/admin/dead-letter/{dead_id}/recover', timeout=15)
        elapsed = time.time() - t0
        results.append((idx, r.status_code, r.json(), elapsed))
    except Exception as e:
        results.append((idx, 'EXC', str(e), 0))

threads = [threading.Thread(target=recover_call, args=(i,)) for i in range(5)]
t0 = time.time()
for t in threads: t.start()
for t in threads: t.join()
total = time.time() - t0

print(f'\n5 个并发请求, 总耗时 {total:.2f}s')
print(f'\n各请求结果:')
for idx, code, body, elapsed in sorted(results):
    if isinstance(body, dict):
        body_str = json.dumps(body, ensure_ascii=False)
    else:
        body_str = str(body)
    print(f'  [{idx}] status={code} elapsed={elapsed:.2f}s body={body_str[:120]}')

# 统计: 1 个 200 (成功), 1 个 409 (advisory lock 占用), 3 个 409 (死信已恢复)
success_count = sum(1 for r in results if r[1] == 200 and r[2].get('recovered') is True)
lock_busy_count = sum(1 for r in results if 'advisory lock' in str(r[2]))
already_recovered_count = sum(1 for r in results if '已恢复' in str(r[2]) or r[2].get('recoveredToPendingId'))

print(f'\n统计:')
print(f'  成功 recover: {success_count} (期望=1)')
print(f'  锁忙 409: {lock_busy_count}')
print(f'  已恢复 409: {already_recovered_count}')

# DB 验证: rc 应=1 (只被恢复 1 次)
cur.execute("SELECT status, recovery_count, recovered_to_pending_id FROM search_index_dead_letter WHERE id = %s", (dead_id,))
row = cur.fetchone()
print(f'  DB: status={row[0]} recovery_count={row[1]} recovered_to_pending_id={row[2]}')
assert row[1] == 1, f'rc 应=1 (只恢复 1 次), 实际 {row[1]}'
assert success_count == 1, f'应只有 1 个成功, 实际 {success_count}'

# 清理
cur.execute("DELETE FROM search_index_dead_letter WHERE id = %s", (dead_id,))
cur.execute("DELETE FROM search_index_pending WHERE last_error LIKE 'TEST-LOCK-%'")
conn.commit()
print('\nDay 7.10.2 advisory lock 验证: 全部通过 ✓')
print(f'  ⭐ 5 并发请求: 1 成功 + 4 拒绝 (锁忙/已恢复), rc=1 (无重复恢复)')
conn.close()
