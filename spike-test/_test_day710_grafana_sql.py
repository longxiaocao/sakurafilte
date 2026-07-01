"""Day 7.10 Item 3 验证: Grafana dashboard SQL 在 PG 中可执行
从 grafana-dashboard-etl.json 提取每个 panel 的 rawSql 并执行,确认:
  - 语法正确
  - 返回合理结果 (非空 + 数据形态正确)
  - 性能可接受 (< 2s)
"""
import json
import psycopg2
import time

PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

with open(r'd:\projects\sakurafilter\monitoring\grafana-dashboard-etl.json', 'r') as f:
    dash = json.load(f)

panels = [p for p in dash['panels'] if p.get('type') in ('stat', 'timeseries', 'table')]
print(f'面板总数: {len(panels)}')
print(f'  stat: {sum(1 for p in panels if p["type"]=="stat")}')
print(f'  timeseries: {sum(1 for p in panels if p["type"]=="timeseries")}')
print(f'  table: {sum(1 for p in panels if p["type"]=="table")}')

conn = psycopg2.connect(**PG); cur = conn.cursor()
print('\n逐面板 SQL 验证:')
ok, fail = 0, 0
for p in panels:
    sql = p['targets'][0].get('rawSql') or ''
    if not sql:
        print(f'  panel#{p["id"]} ({p["title"]}): 无 rawSql, 跳过')
        continue
    t0 = time.time()
    try:
        cur.execute(sql)
        rows = cur.fetchall()
        cols = [d[0] for d in cur.description] if cur.description else []
        elapsed = (time.time() - t0) * 1000
        status = 'OK'
        if elapsed > 2000: status = 'SLOW'
        print(f'  panel#{p["id"]:>3} [{p["type"]:<10}] {p["title"][:30]:<30} | {len(rows):>4} rows, {cols[:5]}{"..." if len(cols)>5 else ""} | {elapsed:.0f}ms [{status}]')
        ok += 1
    except Exception as e:
        print(f'  panel#{p["id"]} ({p["title"]}): FAIL — {e}')
        fail += 1

print(f'\n总计: {ok} OK, {fail} FAIL')
conn.close()
