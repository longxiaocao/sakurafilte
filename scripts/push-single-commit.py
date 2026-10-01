import json, subprocess, urllib.request, urllib.error, base64, os, sys

# 单 commit 推送: 用 Git Data API (blob→tree→commit→ref) 一次提交所有文件
# 替代 Contents API 逐文件 PUT (每个文件一个 commit → 中间态编译失败 → CI/E2E 失败邮件)
# 用法: python push_single_commit.py

def sh(cmd):
    r = subprocess.run(cmd, shell=True, capture_output=True, text=True,
                       encoding="gbk", errors="replace")
    return r.stdout.strip()

def api(url, method="GET", data=None, token=None):
    req = urllib.request.Request(url, method=method,
                                 headers={"Authorization": f"Bearer {token}",
                                          "Accept": "application/vnd.github+json",
                                          "Content-Type": "application/json"})
    body = json.dumps(data).encode() if data is not None else None
    with urllib.request.urlopen(req, body, timeout=60) as r:
        return json.loads(r.read())

repo = r"F:\sakurafilter-real"
with open(r"F:\sakurafilter-perf\_tmp\gh_token.txt") as f:
    token = f.read().strip()

commit = sh(f'cd /d {repo} && git rev-parse HEAD')
files = [f for f in sh(f'cd /d {repo} && git diff-tree --no-commit-id --name-only -r {commit}').splitlines() if f.strip()]
msg = sh(f'cd /d {repo} && git log -1 --format=%B {commit}')
print(f"提交 {commit[:8]}, {len(files)} 个文件, 单 commit 推送中...")

# 1. 当前 master 最新 commit (parent)
ref = api("https://api.github.com/repos/longxiaocao/sakurafilte/git/ref/heads/master", token=token)
parent = ref["object"]["sha"]

# 2. 每个文件创建 blob
tree_items = []
for fp in files:
    content = open(os.path.join(repo, fp), "rb").read()
    blob = api("https://api.github.com/repos/longxiaocao/sakurafilte/git/blobs",
               "POST", {"content": base64.b64encode(content).decode(), "encoding": "base64"}, token)
    tree_items.append({"path": fp, "mode": "100644", "type": "blob", "sha": blob["sha"]})
    print(f"  blob: {fp}")

# 3. 创建 tree (base = parent 的 tree)
parent_tree = api(f"https://api.github.com/repos/longxiaocao/sakurafilte/git/commits/{parent}", token=token)["tree"]["sha"]
tree = api("https://api.github.com/repos/longxiaocao/sakurafilte/git/trees",
           "POST", {"base_tree": parent_tree, "tree": tree_items}, token)

# 4. 创建 commit
new_commit = api("https://api.github.com/repos/longxiaocao/sakurafilte/git/commits",
                 "POST", {"message": msg.strip(), "tree": tree["sha"], "parents": [parent]}, token)

# 5. 更新 ref
api("https://api.github.com/repos/longxiaocao/sakurafilte/git/refs/heads/master",
    "PATCH", {"sha": new_commit["sha"], "force": False}, token)
print(f"✅ 推送成功 (单 commit): {new_commit['sha'][:8]} — CI 将只对完整代码跑一次")
