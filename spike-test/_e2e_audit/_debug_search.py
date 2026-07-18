import urllib.request
import json

BASE = "http://localhost:5148"
body = json.dumps({"q": "oil", "page": 1, "pageSize": 5}).encode('utf-8')
req = urllib.request.Request(f"{BASE}/api/search", data=body, method="POST",
    headers={"Content-Type": "application/json"})
try:
    with urllib.request.urlopen(req, timeout=15) as resp:
        print(f"OK {resp.status}")
        print(resp.read().decode('utf-8')[:500])
except urllib.error.HTTPError as e:
    print(f"ERR {e.code}")
    print(e.read().decode('utf-8', errors='replace')[:2000])
