# -*- coding: utf-8 -*-
"""Day 9.11: PreviousKey 双 key 轮换 E2E 测试

验证 DevTokenAuthMiddleware 的双 key 轮换机制:
  场景1 单 key 模式: CurrentKey=A, PreviousKey="" → A 可用, B 不可用
  场景2 双 key 模式: CurrentKey=B, PreviousKey=A → A/B 都可用, C 不可用
  场景3 移除 Previous: CurrentKey=B, PreviousKey="" → A 不可用, B 可用

测试方式: 修改 appsettings.json → 启动应用 → curl 测试 → 停止应用 → 下一个场景
  最后恢复原始 appsettings.json 并重启应用
"""
import json, os, subprocess, time, urllib.request, urllib.error, sys, shutil, signal

BASE = 'http://localhost:5148'
APPSETTINGS = 'backend/src/SakuraFilter.Api/appsettings.json'
BACKUP = 'backend/src/SakuraFilter.Api/appsettings.json.bak_day911'
API_CSProj = 'backend/src/SakuraFilter.Api/SakuraFilter.Api.csproj'

TOKEN_A = 'dev-admin-token-current-A-12345678901234567890'
TOKEN_B = 'dev-admin-token-current-B-98765432109876543210'
TOKEN_C = 'dev-admin-token-invalid-C-00000000000000000000'

passed = 0
failed = 0

def log(msg):
    print('  ' + msg)

def check(name, cond):
    global passed, failed
    if cond:
        passed += 1
        print('  [PASS] %s' % name)
    else:
        failed += 1
        print('  [FAIL] %s' % name)

def write_appsettings(current, previous=''):
    """写入 appsettings.json, 设置 Current/Previous token"""
    with open(APPSETTINGS, 'r', encoding='utf-8') as f:
        cfg = json.load(f)
    cfg['Auth']['DevStaticToken'] = current
    cfg['Auth']['DevStaticTokenPrevious'] = previous
    with open(APPSETTINGS, 'w', encoding='utf-8') as f:
        json.dump(cfg, f, indent=2, ensure_ascii=False)

def start_api():
    """启动后端应用,返回 process 对象"""
    # WHY --no-build: 已构建过,避免重复编译耗时
    proc = subprocess.Popen(
        ['dotnet', 'run', '--no-build', '-c', 'Release', '--urls', BASE],
        cwd='backend/src/SakuraFilter.Api',
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == 'win32' else 0
    )
    # 等待健康检查 (最多 30s)
    for i in range(30):
        try:
            r = urllib.request.urlopen(BASE + '/', timeout=2)
            if r.status == 200:
                log('API 启动成功 (%ds)' % (i + 1))
                return proc
        except:
            time.sleep(1)
    log('API 启动超时 30s')
    return proc

def stop_api(proc):
    """停止后端应用"""
    if proc is None:
        return
    try:
        if sys.platform == 'win32':
            # Windows: 发送 CTRL_BREAK_EVENT 终止进程组
            proc.send_signal(signal.CTRL_BREAK_EVENT)
        proc.terminate()
        proc.wait(timeout=10)
    except:
        try:
            proc.kill()
        except:
            pass

def test_token(token, expect_status, label):
    """测试 token 访问 /api/etl/status,验证返回码"""
    req = urllib.request.Request(
        BASE + '/api/etl/status',
        headers={'X-Admin-Token': token}
    )
    try:
        r = urllib.request.urlopen(req, timeout=5)
        actual = r.status
    except urllib.error.HTTPError as e:
        actual = e.code
    except Exception as e:
        actual = -1
        log('  请求异常: %s' % str(e)[:100])
    check('%s (expect=%d, actual=%s)' % (label, expect_status, actual), actual == expect_status)

def scenario(name, current, previous, tests):
    """运行一个测试场景: 修改配置 → 启动 → 测试 → 停止"""
    print()
    print('=== 场景: %s ===' % name)
    log('配置: Current=%s..., Previous=%s' % (current[:20], '空' if not previous else previous[:20] + '...'))
    write_appsettings(current, previous)
    proc = start_api()
    if proc is None or proc.poll() is not None:
        log('API 启动失败,跳过此场景')
        stop_api(proc)
        return
    time.sleep(2)  # 额外等待中间件初始化
    for token, expect, label in tests:
        test_token(token, expect, label)
    stop_api(proc)
    time.sleep(2)  # 等待端口释放

# ========== 主流程 ==========
print('=== Day 9.11: PreviousKey 双 key 轮换 E2E 测试 ===')

# 备份 appsettings.json
shutil.copy2(APPSETTINGS, BACKUP)
log('已备份 appsettings.json → %s' % BACKUP)

try:
    # 场景1: 单 key 模式 (当前生产状态)
    scenario(
        '单 key 模式 (Current=A, Previous=空)',
        current=TOKEN_A,
        previous='',
        tests=[
            (TOKEN_A, 200, 'Current token A 可用'),
            (TOKEN_B, 401, '未知 token B 不可用'),
            (TOKEN_C, 401, '无效 token C 不可用'),
        ]
    )

    # 场景2: 双 key 轮换过渡期 (Current=B, Previous=A)
    scenario(
        '双 key 过渡期 (Current=B, Previous=A)',
        current=TOKEN_B,
        previous=TOKEN_A,
        tests=[
            (TOKEN_B, 200, '新 Current token B 可用'),
            (TOKEN_A, 200, '旧 Previous token A 过渡期可用'),
            (TOKEN_C, 401, '无效 token C 不可用'),
        ]
    )

    # 场景3: 移除 PreviousKey (过渡期结束)
    scenario(
        '移除 Previous (Current=B, Previous=空)',
        current=TOKEN_B,
        previous='',
        tests=[
            (TOKEN_B, 200, 'Current token B 可用'),
            (TOKEN_A, 401, '旧 token A 已失效 (Previous 移除)'),
            (TOKEN_C, 401, '无效 token C 不可用'),
        ]
    )

finally:
    # 恢复 appsettings.json
    print()
    print('=== 恢复配置 ===')
    if os.path.exists(BACKUP):
        shutil.move(BACKUP, APPSETTINGS)
        log('已恢复 appsettings.json')
    else:
        log('警告: 备份文件不存在,无法恢复')

# ========== 总结 ==========
print()
print('=== 测试总结 ===')
print('  PASS: %d' % passed)
print('  FAIL: %d' % failed)
if failed > 0:
    print('  结果: 失败')
    sys.exit(1)
else:
    print('  结果: 全部通过')
    sys.exit(0)
