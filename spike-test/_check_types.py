import psycopg2
c = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = c.cursor()
cur.execute("""
    SELECT column_name, data_type, character_maximum_length
    FROM information_schema.columns
    WHERE table_name='products' AND column_name IN ('bypass_pressure','bypass_valve_hr','bypass_valve_lr','efficiency_1','efficiency_2')
    ORDER BY column_name
""")
for r in cur.fetchall(): print(r)
