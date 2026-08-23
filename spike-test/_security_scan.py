"""
SakuraFilter 安全扫描脚本
=========================
覆盖三大维度:
  1. 依赖审计: dotnet list package --vulnerable (调用 dotnet CLI 解析输出)
  2. 配置检查: appsettings.json 风险项 (硬编码密钥/弱密钥/危险 CORS/debug 残留)
  3. 源码扫描: .cs 文件中硬编码的密码/Token/连接串/私钥

WHY Python 而非 .NET: 跨平台轻量, 不需要构建, 30 秒内可完成全量扫描
退出码:
  0 = 无问题
  1 = 仅 WARN
  2 = 有 FAIL (高危)

用法:
  python _security_scan.py                       # 扫描默认范围
  python _security_scan.py --skip-vuln           # 跳过依赖审计 (慢)
  python _security_scan.py --json                # 输出 JSON 报告
"""
import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import List, Dict, Any

# === 路径常量 ===
ROOT = Path(__file__).resolve().parent.parent  # spike-test/ 上级 (项目根)
BACKEND = ROOT / "backend" / "src"
APPSETTINGS_FILES = [
    BACKEND / "SakuraFilter.Api" / "appsettings.json",
    BACKEND / "SakuraFilter.Api" / "appsettings.Production.json",
    BACKEND / "SakuraFilter.Api" / "appsettings.Development.json",
]
SRC_CS = BACKEND / "SakuraFilter.Api"

# === 严重度图标 ===
SEV_FAIL = "FAIL"
SEV_WARN = "WARN"
SEV_OK = "OK"


# ===== 1. 依赖审计 =====
def audit_dependencies() -> List[Dict[str, Any]]:
    """
    通过 dotnet CLI 扫描所有 csproj 的已知漏洞
    WHY 用 CLI 而非自己解析: dotnet list package --vulnerable 会查 GitHub Advisory Database,
      准确率比自己维护 CVE 库高得多, 且零维护成本
    """
    findings: List[Dict[str, Any]] = []
    if not BACKEND.exists():
        findings.append({
            "severity": SEV_WARN, "category": "deps",
            "message": f"后端目录不存在: {BACKEND}, 跳过依赖审计"
        })
        return findings

    # 找所有 csproj
    csproj_files = list((BACKEND.parent).rglob("*.csproj"))
    if not csproj_files:
        findings.append({
            "severity": SEV_WARN, "category": "deps",
            "message": "未找到 .csproj 文件"
        })
        return findings

    try:
        result = subprocess.run(
            ["dotnet", "list", "package", "--vulnerable", "--include-transitive"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            timeout=300,
            encoding="utf-8",
            errors="replace",
        )
        output = result.stdout + "\n" + result.stderr
    except FileNotFoundError:
        findings.append({
            "severity": SEV_WARN, "category": "deps",
            "message": "dotnet CLI 未安装或不在 PATH, 跳过依赖审计"
        })
        return findings
    except subprocess.TimeoutExpired:
        findings.append({
            "severity": SEV_WARN, "category": "deps",
            "message": "dotnet list 超过 5 分钟未完成, 跳过依赖审计"
        })
        return findings

    # 解析输出: 匹配 "PackageName" 行 + "-> [version]" 行 + "Severity:" 行
    #   简化版: 查找 Severity 段, 然后向上找 Package 名
    lines = output.splitlines()
    i = 0
    vuln_count = 0
    while i < len(lines):
        line = lines[i]
        if ">" in line and "has the following vulnerable packages" in line.lower():
            i += 1
            continue
        # 检测漏洞段: 形如 "  > Package.Name"
        m = re.match(r"\s*>\s*([A-Za-z0-9._\-]+)\s+([0-9.]+)\s*$", line)
        if m:
            pkg, ver = m.group(1), m.group(2)
            # 后续几行找 Severity / Advisory
            block = []
            j = i + 1
            while j < len(lines) and j < i + 30:
                if re.match(r"\s*>\s*[A-Za-z0-9._\-]+\s+[0-9.]+\s*$", lines[j]):
                    break
                block.append(lines[j])
                j += 1
            block_text = "\n".join(block)
            severity_match = re.search(r"Severity:\s*(\w+)", block_text)
            severity = severity_match.group(1) if severity_match else "Unknown"
            advisory_match = re.search(r"Advisory URL:\s*(\S+)", block_text)
            advisory = advisory_match.group(1) if advisory_match else ""
            # 映射严重度
            sev_norm = SEV_FAIL if severity.lower() in ("critical", "high") else SEV_WARN
            findings.append({
                "severity": sev_norm, "category": "deps",
                "package": pkg, "version": ver, "advisory_severity": severity,
                "advisory_url": advisory,
                "message": f"漏洞包 {pkg}@{ver}: {severity}",
            })
            vuln_count += 1
            i = j
            continue
        i += 1

    if vuln_count == 0:
        findings.append({
            "severity": SEV_OK, "category": "deps",
            "message": f"依赖审计: 未发现已知漏洞 (扫描 {len(csproj_files)} 个 csproj)"
        })
    return findings


# ===== 2. 配置检查 =====
def check_configs() -> List[Dict[str, Any]]:
    """扫描 appsettings.json 中的安全风险项"""
    findings: List[Dict[str, Any]] = []

    for path in APPSETTINGS_FILES:
        if not path.exists():
            continue
        is_prod = "Production" in path.name
        is_dev = "Development" in path.name
        try:
            cfg = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            findings.append({
                "severity": SEV_WARN, "category": "config",
                "file": str(path.relative_to(ROOT)),
                "message": f"JSON 解析失败: {e}"
            })
            continue

        # 2.1 硬编码的 dev key 出现在生产配置
        if is_prod:
            search_key = cfg.get("Search", {}).get("CursorHmacKey", "")
            if search_key and "dev" in search_key.lower():
                findings.append({
                    "severity": SEV_FAIL, "category": "config",
                    "file": str(path.relative_to(ROOT)),
                    "message": f"生产配置含 dev 标记的 Search:CursorHmacKey: {search_key[:20]}..."
                })
            jwt_key = cfg.get("Jwt", {}).get("SigningKey", "")
            if jwt_key and len(jwt_key) < 32:
                findings.append({
                    "severity": SEV_FAIL, "category": "config",
                    "file": str(path.relative_to(ROOT)),
                    "message": f"JWT SigningKey 长度不足 32 字符: {len(jwt_key)}"
                })
            elif jwt_key and "dev" in jwt_key.lower():
                findings.append({
                    "severity": SEV_FAIL, "category": "config",
                    "file": str(path.relative_to(ROOT)),
                    "message": f"生产配置含 dev 标记的 Jwt:SigningKey"
                })

        # 2.2 dev 凭证不应出现在 Production 文件中
        if is_prod and cfg.get("Auth", {}).get("DevStaticToken"):
            findings.append({
                "severity": SEV_FAIL, "category": "config",
                "file": str(path.relative_to(ROOT)),
                "message": "生产配置含 DevStaticToken (应仅在开发环境配置)"
            })

        # 2.3 CORS 允许任意 origin
        origins = cfg.get("Cors", {}).get("AllowedOrigins", [])
        if "*" in origins:
            findings.append({
                "severity": SEV_FAIL, "category": "config",
                "file": str(path.relative_to(ROOT)),
                "message": "CORS AllowedOrigins 含 '*' (允许任意 origin 跨域)"
            })
        # 检查是否含 http:// (生产应为 https)
        if is_prod and any(o.startswith("http://") for o in origins):
            findings.append({
                "severity": SEV_FAIL, "category": "config",
                "file": str(path.relative_to(ROOT)),
                "message": f"生产 CORS 含非 HTTPS origin: {[o for o in origins if o.startswith('http://')]}"
            })

        # 2.4 Connection string 不应硬编码密码
        conn = cfg.get("ConnectionStrings", {}).get("Postgres", "")
        if conn and ("Password=" in conn or "password=" in conn):
            findings.append({
                "severity": SEV_WARN, "category": "config",
                "file": str(path.relative_to(ROOT)),
                "message": "Postgres 连接串含 Password 字段 (建议改用环境变量)"
            })

        # 2.5 Storage 密钥不应留空 (应强制设置)
        minio_secret = cfg.get("Minio", {}).get("SecretKey", "")
        aliyun_secret = cfg.get("Aliyun", {}).get("AccessKeySecret", "")
        meili_key = cfg.get("MeiliSearch", {}).get("ApiKey", "")
        if is_prod:
            if not minio_secret:
                findings.append({
                    "severity": SEV_WARN, "category": "config",
                    "file": str(path.relative_to(ROOT)),
                    "message": "生产配置 Minio.SecretKey 为空 (依赖环境变量, 仅警告)"
                })
            if not aliyun_secret:
                findings.append({
                    "severity": SEV_WARN, "category": "config",
                    "file": str(path.relative_to(ROOT)),
                    "message": "生产配置 Aliyun.AccessKeySecret 为空 (依赖环境变量, 仅警告)"
                })

    if not findings:
        findings.append({
            "severity": SEV_OK, "category": "config",
            "message": f"配置检查: 未发现高危风险 (扫描 {len(APPSETTINGS_FILES)} 个 appsettings)"
        })
    return findings


# ===== 3. 源码硬编码密钥扫描 =====
SECRET_PATTERNS = [
    # (pattern, description, severity)
    (r'(?i)(?:password|passwd|pwd)\s*=\s*["\']([^"\']{4,})["\']',
     "硬编码 Password", SEV_FAIL),
    (r'(?i)(?:api[_-]?key|apikey|secret[_-]?key)\s*=\s*["\']([A-Za-z0-9_\-]{16,})["\']',
     "硬编码 API Key/Secret", SEV_FAIL),
    (r'(?i)Bearer\s+([A-Za-z0-9_\-\.]{20,})',
     "硬编码 Bearer Token", SEV_FAIL),
    (r'(?i)(?:aws|aliyun|minio)[_-]?(?:access[_-]?key|secret[_-]?key)\s*[:=]\s*["\']([A-Za-z0-9]{16,})["\']',
     "硬编码云服务 AccessKey", SEV_FAIL),
    (r'postgres(?:ql)?://[^:]+:[^@]+@',
     "含密码的 Postgres 连接串", SEV_FAIL),
    (r'-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----',
     "PEM 私钥", SEV_FAIL),
    (r'(?i)jdbc:[a-z]+://[^:]+:[^@]+@',
     "JDBC 含密码连接串", SEV_FAIL),
]

# 排除的路径/文件 (测试用 dev 凭证不算)
EXCLUDE_PATH_PATTERNS = [
    re.compile(r"\\bin\\", re.IGNORECASE),
    re.compile(r"\\obj\\", re.IGNORECASE),
    re.compile(r"\.Tests\\", re.IGNORECASE),
    re.compile(r"\\test", re.IGNORECASE),
    re.compile(r"\\docs\\", re.IGNORECASE),
    re.compile(r"_test_.*\.py$", re.IGNORECASE),
]


def is_excluded(path: Path) -> bool:
    p = str(path)
    return any(rx.search(p) for rx in EXCLUDE_PATH_PATTERNS)


def scan_source() -> List[Dict[str, Any]]:
    """扫描 .cs 源码中的硬编码密钥"""
    findings: List[Dict[str, Any]] = []
    if not SRC_CS.exists():
        return findings

    files_scanned = 0
    for cs_file in SRC_CS.rglob("*.cs"):
        if is_excluded(cs_file):
            continue
        files_scanned += 1
        try:
            content = cs_file.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue

        # 按行扫描, 报告行号
        for lineno, line in enumerate(content.splitlines(), start=1):
            # 跳过注释行
            stripped = line.strip()
            if stripped.startswith("//") or stripped.startswith("/*") or stripped.startswith("*"):
                continue
            for pat, desc, sev in SECRET_PATTERNS:
                m = re.search(pat, line)
                if m:
                    # 二次过滤: 排除变量赋值如 var password = "" 这种占位
                    val = m.group(1) if m.lastindex else ""
                    if val and val not in ("", "null", "your-password-here", "TODO", "xxx", "change-me"):
                        findings.append({
                            "severity": sev, "category": "source",
                            "file": str(cs_file.relative_to(ROOT)),
                            "line": lineno,
                            "message": f"{desc}: {val[:30]}...",
                            "matched": m.group(0)[:80],
                        })

    if not findings:
        findings.append({
            "severity": SEV_OK, "category": "source",
            "message": f"源码扫描: 未发现硬编码密钥 (扫描 {files_scanned} 个 .cs 文件)"
        })
    return findings


# ===== 报告输出 =====
def render_report(findings: List[Dict[str, Any]]) -> Dict[str, Any]:
    """汇总各维度, 计算严重度"""
    by_cat: Dict[str, List[Dict]] = {"deps": [], "config": [], "source": []}
    for f in findings:
        by_cat.setdefault(f.get("category", "other"), []).append(f)

    fail_count = sum(1 for f in findings if f.get("severity") == SEV_FAIL)
    warn_count = sum(1 for f in findings if f.get("severity") == SEV_WARN)
    ok_count = sum(1 for f in findings if f.get("severity") == SEV_OK)

    return {
        "summary": {
            "fail": fail_count,
            "warn": warn_count,
            "ok": ok_count,
            "total": len(findings),
            "exit_code": 2 if fail_count > 0 else (1 if warn_count > 0 else 0),
        },
        "deps": by_cat.get("deps", []),
        "config": by_cat.get("config", []),
        "source": by_cat.get("source", []),
    }


def render_console(report: Dict[str, Any]) -> None:
    print("=" * 70)
    print(f"  SakuraFilter 安全扫描报告")
    print("=" * 70)
    s = report["summary"]
    print(f"  汇总: FAIL={s['fail']}  WARN={s['warn']}  OK={s['ok']}  TOTAL={s['total']}")
    print()

    icons = {SEV_FAIL: "[FAIL]", SEV_WARN: "[WARN]", SEV_OK: "[ OK ]"}

    for cat_name, cat_label in [("deps", "依赖审计"), ("config", "配置检查"), ("source", "源码扫描")]:
        items = report.get(cat_name, [])
        if not items:
            continue
        print(f"--- {cat_label} ---")
        for f in items:
            sev = f.get("severity", "WARN")
            icon = icons.get(sev, "[????]")
            file_part = ""
            if f.get("file"):
                file_part = f" {f['file']}"
                if f.get("line"):
                    file_part += f":{f['line']}"
            print(f"  {icon}{file_part}  {f['message']}")
        print()

    print("=" * 70)
    code = s["exit_code"]
    if code == 0:
        print("  ✓ 扫描通过, 无安全风险")
    elif code == 1:
        print("  ⚠ 扫描完成, 有警告需要关注")
    else:
        print("  ✗ 扫描完成, 发现高危问题, 请立即修复")
    print("=" * 70)


def main():
    parser = argparse.ArgumentParser(description="SakuraFilter 安全扫描")
    parser.add_argument("--skip-vuln", action="store_true", help="跳过依赖审计 (慢)")
    parser.add_argument("--json", action="store_true", help="输出 JSON 报告到 stdout")
    parser.add_argument("--out", type=str, default=None, help="报告输出路径 (默认 spike-test/security_scan_report.json)")
    args = parser.parse_args()

    findings: List[Dict[str, Any]] = []

    print("[1/3] 依赖审计 (dotnet list package --vulnerable)..." if not args.skip_vuln else "[1/3] 依赖审计: 跳过", flush=True)
    if not args.skip_vuln:
        findings.extend(audit_dependencies())

    print("[2/3] 配置检查...", flush=True)
    findings.extend(check_configs())

    print("[3/3] 源码扫描...", flush=True)
    findings.extend(scan_source())

    report = render_report(findings)

    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        render_console(report)

    if args.out:
        out_path = Path(args.out)
        out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        if not args.json:
            print(f"\n报告已写入: {out_path.relative_to(ROOT)}")

    sys.exit(report["summary"]["exit_code"])


if __name__ == "__main__":
    main()
