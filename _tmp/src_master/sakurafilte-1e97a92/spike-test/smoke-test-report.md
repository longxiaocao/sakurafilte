# SakuraFilter E2E Smoke Test Report
- Time: 2026-07-06 06:42:03
- Backend: dotnet run (refactored), listening on http://localhost:5148

### Root
- GET / -> HTTP **200**
- body: {"name":"SakuraFilter API","version":"0.3.0","status":"running"}

### Liveness
- GET /health/live -> HTTP **200**
- body: {"status":"alive"}

### Readiness
- GET /health/ready -> HTTP **200**
- body: {"status":"degraded","checks":[{"name":"postgres","healthy":true},{"name":"meili","healthy":false},{"name":"fallback","healthy":true},{"name":"backgroundServices","healthy":true,"stale":[]}]}

### Metrics
- GET /metrics -> HTTP **200**
- body: # HELP sakura_etl_records_processed_total ETL 澶勭悊鐨勮褰曟€绘暟
# TYPE sakura_etl_records_processed_total counter
# HELP sakura_etl_failures_total ETL 澶辫触鍘熷洜璁℃暟 (reason_code 瑙?EtlReasonCode 鏋氫妇)
# TYPE sakura_etl_failures_total counter
# HELP sakura_etl_task_duration_seconds ETL 浠诲姟鎸佺画鏃堕棿 (绉?
# TYPE sakur...

### Search health
- GET /api/search/health -> HTTP **200**
- body: {"provider":"resilient(meili鈫抪g)","healthy":true}

### Public search
- POST /api/search -> HTTP **200**
- body: {"provider":"resilient(meili鈫抪g)","result":{"total":49938,"page":1,"pageSize":3,"totalPages":16646,"elapsedMs":14,"items":[{"id":1,"oemNoDisplay":"AC 010323","remark":"HiFi Filter AC 010323 Oil Filter","type":"OIL FILTER","d1Mm":69.80,"d2Mm":292.20,"h1Mm":374.80,"imageKey":null,"isDiscontinued":fals...

### Perf ingest (empty, expect 400)
- POST /api/perf/ingest -> HTTP **400**
- body: {"error":"samples 涓嶈兘涓虹┖"}

### Perf ingest batch
- POST /api/perf/ingest -> HTTP **200**
- body: {"received":1}

### Login admin
- POST /api/auth/login -> HTTP **200**
- body: {"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiZW1haWwiOiJhZG1pbkBzYWt1cmFmaWx0ZXIubG9jYWwiLCJ1bmlxdWVfbmFtZSI6ImFkbWluIiwiaWF0IjoxNzgzMjkxMzI3LCJyb2xlIjoiYWRtaW4iLCJuYmYiOjE3ODMyOTEzMjcsImV4cCI6MTc4MzI5MzEyNywiaXNzIjoiU2FrdXJhRmlsdGVyIiwiYXVkIjoiU2FrdXJhRmlsdGVyLldlYiJ9.3Dgyej...

JWT token length: 321

### No token admin (expect 401)
- GET /api/admin/products/ -> HTTP **401**
- body: {"type":"https://tools.ietf.org/html/rfc7235#section-3.1","title":"Unauthorized","status":401,"detail":"缂哄皯鎴栭潪娉曠殑 X-Admin-Token header","instance":"/api/admin/products/"}

### JWT products
- GET /api/admin/products/?page=1&pageSize=3 -> HTTP **200**
- body: {"total":49938,"page":1,"pageSize":3,"items":[{"id":50009,"oemNoDisplay":"E2E202607051267","oem2":"E2E202607051267","mr1":null,"productName1":"E2E娴嬭瘯浜у搧_E2E202607051267","productName2":null,"type":"filter","isPublished":true,"isDiscontinued":false,"imageKey":null,"imageUrl":null,"updatedAt":"2026-07...

### X-Admin-Token auth status
- GET /api/admin/auth/status -> HTTP **200**
- body: {"currentLen":56,"currentPrefix":"dev-","previousLen":43,"previousPrefix":"e2e-","lastRotatedAt":"2026-07-04T08:14:22.546386+08:00","lastRotatedBy":"e2e_test_p71_restore","loadedFromDb":true,"hasPrevious":true}

### Dict schema contract
- GET /api/admin/dict/_schema -> HTTP **200**
- body: {"generatedAt":"2026-07-05T22:42:07.6749062Z","count":8,"dictionaries":[{"entity":"XrefOemBrand","table":"xref_oem_brand","fields":[{"name":"Id","cSharpType":"long","nullable":false,"hasColumn":false},{"name":"Brand","cSharpType":"string","nullable":true,"hasColumn":true},{"name":"SortOrder","cSharp...

### Dict oem-brands
- GET /api/admin/dict/oem-brands/?limit=3 -> HTTP **200**
- body: {"count":3,"items":[{"id":24,"brand":"Donaldson","sortOrder":10,"createdAt":"2026-07-02T15:09:20.216516","updatedAt":"2026-07-02T15:09:20.216516","deletedAt":null,"xrefCount":34626},{"id":23,"brand":"Bosch","sortOrder":20,"createdAt":"2026-07-02T15:09:20.216516","updatedAt":"2026-07-02T15:09:20.2165...

### Dict product-name1s
- GET /api/admin/dict/product-name1s/?limit=3 -> HTTP **200**
- body: {"count":3,"items":[{"id":13,"productName1":"ACTIVATED CARBON FILTER","sortOrder":0,"createdAt":"2026-07-03T05:23:40.489547","updatedAt":"2026-07-03T05:23:40.489547","deletedAt":null,"xrefCount":0},{"id":14,"productName1":"ACTIVATED CARBON FILTER Compact","sortOrder":0,"createdAt":"2026-07-03T05:23:...

### Dict types
- GET /api/admin/dict/types/?limit=3 -> HTTP **200**
- body: {"count":3,"items":[{"id":1,"type":"oil","sortOrder":1,"createdAt":"2026-07-02T20:16:28.048436","updatedAt":"2026-07-03T14:23:00.859606","deletedAt":null,"xrefCount":0},{"id":2,"type":"fuel","sortOrder":2,"createdAt":"2026-07-02T20:16:28.048436","updatedAt":"2026-07-03T14:23:00.859606","deletedAt":n...

### Dict machines
- GET /api/admin/dict/machines/?limit=3 -> HTTP **200**
- body: {"count":3,"items":[{"id":1033,"machineBrand":"Case","machineModel":null,"machineName":null,"machineCategory":"others","sortOrder":0,"createdAt":"2026-07-03T05:55:44.684657","updatedAt":"2026-07-03T05:55:44.684657","deletedAt":null,"xrefCount":0},{"id":1031,"machineBrand":"Doosan","machineModel":nul...

### Dead letter
- GET /api/admin/dead-letter/?limit=3 -> HTTP **200**
- body: {"total":1876119,"totalInRange":1876119,"returned":3,"limit":3,"since":null,"minRecoveryCount":null,"maxRecoveryCount":null,"cursor":null,"nextCursor":"2026-07-04T00:23:55.883Z|1877693","hasMore":true,"items":[{"id":1877695,"originalId":2806026,"operation":"index","retryCount":999,"lastError":"stale...

### Admin products list
- GET /api/admin/products/?page=1&pageSize=3 -> HTTP **200**
- body: {"total":49938,"page":1,"pageSize":3,"items":[{"id":50009,"oemNoDisplay":"E2E202607051267","oem2":"E2E202607051267","mr1":null,"productName1":"E2E娴嬭瘯浜у搧_E2E202607051267","productName2":null,"type":"filter","isPublished":true,"isDiscontinued":false,"imageKey":null,"imageUrl":null,"updatedAt":"2026-07...

### Admin products search
- GET /api/admin/products/search?page=1&pageSize=3 -> HTTP **200**
- body: {"total":49938,"countMode":"exact","countModeUsed":"exact","pagingMode":"offset","hasMore":true,"nextCursor":null,"page":1,"pageSize":3,"sizeTolerance":5,"items":[{"id":50009,"oemNoDisplay":"E2E202607051267","oem2":"E2E202607051267","mr1":null,"productName1":"E2E娴嬭瘯浜у搧_E2E202607051267","productName2...

### ETL status
- GET /api/etl/status -> HTTP **200**
- body: {"status":"idle","stage":"idle","rowsTotal":0,"currentFile":null,"read":0,"inserted":0,"updated":0,"skipped":0,"skippedMissingOem":0,"skippedNullField":0,"skippedDuplicate":0,"errors":0,"typeMismatches":0,"indexed":0,"indexPending":0,"elapsedSec":null,"startedAt":null,"finishedAt":null,"lastError":n...

### ETL dry-run (no file, expect 404)
- POST /api/admin/etl/trigger -> HTTP **404**
- body: {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"File Not Found","status":404,"detail":"鏂囦欢涓嶅瓨鍦? D:/nonexistent.jsonl"}

### ETL history
- GET /api/admin/etl/history?limit=3 -> HTTP **200**
- body: {"count":3,"items":[{"id":207,"entityType":"products","mode":"full-load","status":"completed","reasonCode":null,"cancelReason":null,"cancelledAt":null,"readCount":50000,"insertedCount":49913,"updatedCount":0,"skippedCount":87,"skippedMissingOem":0,"skippedNullField":0,"skippedDuplicate":0,"errorCoun...

### ETL history aggregate
- GET /api/admin/etl/history/aggregate -> HTTP **200**
- body: {"total":23,"breakdown":[{"code":"USER_REQUEST","count":14,"pct":60.9},{"code":"TIMEOUT","count":5,"pct":21.7},{"code":"LEGACY","count":4,"pct":17.4}]}

### Perf snapshot
- GET /api/perf -> HTTP **200**
- body: {"sampleCount":75,"totalRequests":75,"errorRequests":2,"errorRate":2.67,"p50Ms":2.8,"p95Ms":1072.9,"p99Ms":1176.7,"maxMs":1176.7,"generatedAt":"2026-07-05T22:42:08.4967388Z"}

