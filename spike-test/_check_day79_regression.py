"""Day 7.9 回归:Day 7.7 entity_type + 验证 webhook 接收"""
import psycopg2, json, re, subprocess
from pathlib import Path

# 1) entity_type 回归
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT DISTINCT entity_type FROM etl_progress_log ORDER BY entity_type")
print('etl_progress_log 中现有 entity_type:')
for r in cur.fetchall(): print(f'  {r[0]}')

# 2) alert_sent 状态
cur.execute("SELECT count(*) FROM etl_progress_log WHERE status='failed'")
print(f'\nfailed 总数: {cur.fetchone()[0]}')
cur.execute("SELECT count(*) FROM etl_progress_log WHERE status='failed' AND alert_sent=true")
print(f'  其中已告警: {cur.fetchone()[0]}')

# 3) 看 webhook server stdout (看接收的 payload)
wh = Path(r'd:\projects\sakurafilter\spike-test\webhook_stdout.log')
if wh.exists():
    text = wh.read_text(encoding='utf-8', errors='ignore')
    print(f'\nwebhook server log 行数: {len(text.splitlines())}')
    # 提取 Listen info
    listens = re.findall(r'listening on.*', text)
    for l in listens: print(f'  {l}')

conn.close()
