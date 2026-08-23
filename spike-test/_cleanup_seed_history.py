"""清理 cursor-test 历史数据 (避免污染产品历史)"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM product_history WHERE changed_by = 'cursor-test'")
print(f"清理 {cur.rowcount} 条 cursor-test 历史数据")
conn.commit()
conn.close()
