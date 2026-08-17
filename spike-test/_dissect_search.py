import json, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

MEILI = 'http://localhost:57700'
BASE = 'http://localhost:55148'
KEY = open('.env.perf', encoding='utf-8').read().split('MEILI_MASTER_KEY=')[1].split('\n')[0].strip().strip('"').strip("'")
FILTER = 'is_published = true AND is_discontinued = false'
MARK_OPEN, MARK_CLOSE = '\uE000', '\uE001'
qs = json.load(open('spike-test/bench_queries_public.json', encoding='utf-8'))['queries']

def build_body(item):
    q_type, q_text, type_val, m_brand, m_model, h1, tol = item
    body = {"page": 1, "pageSize": 20, "includeDiscontinued": False}
    if q_text is not None: body["q"] = q_text
    if type_val is not None: body["type"] = type_val
    if h1 is not None: body["h1"] = h1
    body["tolerance"] = tol or 5
    return body

def meili_body(item):
    q_type, q_text, type_val, m_brand, m_model, h1, tol = item
    f = FILTER
    if type_val: f += f' AND type = "{type_val}"'
    if h1 is not None:
        lo, hi = h1 - (tol or 5), h1 + (tol or 5)
        f += f' AND h1_mm >= {lo} AND h1_mm <= {hi}'
    return {"q": q_text or "", "limit": 20, "offset": 0, "filter": f,
            "attributesToHighlight": ["product_name_1", "product_name_2", "oem_2", "remark", "type", "media"],
            "highlightPreTag": MARK_OPEN, "highlightPostTag": MARK_CLOSE,
            "showRankingScore": True, "sort": ["brand_sort_order_min:asc", "oem_list_sort_order_min:asc"]}

def req(url, body, headers):
    t = time.perf_counter()
    try:
        urllib.request.urlopen(urllib.request.Request(url, data=json.dumps(body).encode(), headers=headers), timeout=10).read()
        return (time.perf_counter() - t) * 1000, 200
    except Exception:
        return (time.perf_counter() - t) * 1000, 0

def bench(name, url_fn, body_fn, headers, cc, rounds=10):
    tasks = qs * rounds
    t0 = time.perf_counter()
    with ThreadPoolExecutor(max_workers=cc) as ex:
        res = list(ex.map(lambda it: req(url_fn(it), body_fn(it), headers), tasks))
    lats = sorted(r[0] for r in res); n = len(lats)
    errs = sum(1 for r in res if r[1] != 200)
    p50, p95, p99 = lats[int(n*0.5)], lats[min(n-1, int(n*0.95))], lats[min(n-1, int(n*0.99))]
    print(f'{name} cc={cc}: n={n} P50={p50:.1f} P95={p95:.1f} P99={p99:.1f} errs={errs} wall={(time.perf_counter()-t0)*1000:.0f}ms')

if __name__ == '__main__':
    bench('backend-search', lambda it: BASE + '/api/search', build_body, {'Content-Type': 'application/json'}, 50)
    bench('meili-direct  ', lambda it: MEILI + '/indexes/products/search', meili_body, {'Content-Type': 'application/json', 'Authorization': 'Bearer ' + KEY}, 50)
    bench('backend-search', lambda it: BASE + '/api/search', build_body, {'Content-Type': 'application/json'}, 100)
    bench('meili-direct  ', lambda it: MEILI + '/indexes/products/search', meili_body, {'Content-Type': 'application/json', 'Authorization': 'Bearer ' + KEY}, 100)
