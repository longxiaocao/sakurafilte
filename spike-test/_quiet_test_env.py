import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
# 标记死信 + 重放队列 + ETL 告警状态,避免后端启动后被 IndexReplayWorker / EtlAlertService 拖慢
cur.execute("UPDATE search_index_pending SET retry_count = 999, last_error = 'stale for test' WHERE retry_count < 5")
cur.execute("UPDATE etl_progress_log SET alert_sent = true WHERE alert_sent = false AND status IN ('failed', 'cancelled')")
conn.commit()
print(f"  marked {cur.rowcount} ETL failures as alert_sent=true")
# 验证
cur.execute("SELECT COUNT(*) FROM search_index_pending WHERE retry_count < 5")
print(f"  search_index_pending still-active count: {cur.fetchone()[0]}")
cur.execute("SELECT COUNT(*) FROM etl_progress_log WHERE alert_sent = false AND status IN ('failed', 'cancelled')")
print(f"  etl_progress_log still-pending alert count: {cur.fetchone()[0]}")
conn.close()
