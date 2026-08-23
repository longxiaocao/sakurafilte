"""监控 ETL 进度"""
import requests, time
for i in range(60):
    r = requests.get('http://localhost:5148/api/etl/status')
    data = r.json()
    s = data.get('status', '?')
    read = data.get('read', 0)
    ins = data.get('inserted', 0)
    skip = data.get('skipped', 0)
    err = data.get('errors', 0)
    elap = data.get('elapsedSec', 0)
    print(f't={i*5}s: status={s} read={read:,} inserted={ins:,} skipped={skip:,} errors={err} elapsed={elap}s')
    if s == 'idle' and (ins > 0 or err > 0):
        break
    time.sleep(5)
