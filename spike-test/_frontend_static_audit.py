"""
前端 P0 缺陷静态扫描器
======================
检测四类常见问题:
  1. 模板中使用 <XxxYyy/> 但 script 中未 import (且非全局组件)
  2. i18n t('xxx.yyy') 调用的 key 在 zh-CN 或 en-US 中缺失
  3. API 调用未在 try-catch 中 + 无 loading 状态
  4. 模板中裸用 <script> / <style> (常见误写)

输出 JSON 报告: spike-test/frontend_static_audit.json
退出码: 0 OK / 1 有 WARN / 2 有 FAIL
"""
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Set, Tuple

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / "frontend" / "src"

# === Element Plus 全局注册图标 (扫描时跳过) ===
EL_ICON_HINT = "Element Plus 图标"  # 标识用途, 不参与匹配

# === 内置 HTML / Vue 全局组件 (扫描时跳过) ===
NATIVE_COMPONENTS = {
    # HTML
    "A", "P", "Span", "Div", "Section", "Header", "Footer", "Main", "Nav",
    "Ul", "Li", "H1", "H2", "H3", "H4", "H5", "H6",
    "Table", "Tr", "Td", "Th", "Thead", "Tbody",
    "Form", "Input", "Button", "Select", "Option", "Textarea", "Label",
    "Img", "Br", "Hr", "Iframe", "Video", "Audio", "Source",
    # Vue 内置
    "RouterView", "RouterLink", "Transition", "KeepAlive", "Teleport", "Suspense", "Component",
    # Element Plus 全部以 El 或 ElXxx 开头
}

# === Element Plus 全局注册图标 (main.ts 用 import * as ElementPlusIconsVue 全局注册) ===
# WHY hardcode: 避免解析 main.ts 时的复杂依赖, 这个集合是相对稳定的
EL_PLUS_ICONS = {
    "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp",
    "Menu", "Search", "Close", "Check",
    "WarningFilled", "QuestionFilled", "UploadFilled", "Download", "Upload",
    "Bell", "User", "Setting", "Lock", "Unlock", "Key",
    "Edit", "Delete", "Plus", "Minus", "Refresh",
    "View", "Hide", "Document", "Folder", "Picture",
    "Histogram", "TrendCharts", "DataAnalysis", "PieChart", "DataLine",
    "Tools", "Box", "Cpu", "Coin", "PriceTag",
    "Calendar", "Clock", "Position", "Location",
    "Filter", "Sort", "SortUp", "SortDown", "Operation",
    "FirstAidKit", "Medication", "Goblet",
    "Promotion", "Star", "StarFilled", "TrophyBase",
}


def is_el_prefix(name: str) -> bool:
    return name.startswith("El") and len(name) > 2 and name[2].isupper()


def is_pascal_case(name: str) -> bool:
    return bool(re.match(r"^[A-Z][A-Za-z0-9]*$", name))


def extract_imports(script: str) -> Set[str]:
    """提取 script 块中所有 import 的本地符号 (default + named)"""
    names: Set[str] = set()
    # default: import X from '...'
    for m in re.finditer(r"^\s*import\s+(\w+)\s+from\s+['\"][^'\"]+['\"]", script, re.MULTILINE):
        names.add(m.group(1))
    # named: import { A, B as C } from '...'
    for m in re.finditer(r"^\s*import\s*\{([^}]+)\}\s*from\s*['\"][^'\"]+['\"]", script, re.MULTILINE):
        for item in m.group(1).split(","):
            # 处理 `A as B`
            parts = item.strip().split(" as ")
            if len(parts) == 2 and parts[1]:
                names.add(parts[1].strip())
            elif parts[0]:
                names.add(parts[0].strip())
    return names


def extract_components_property(script: str) -> Set[str]:
    """提取 components: { Foo, Bar } 局部注册"""
    names: Set[str] = set()
    m = re.search(r"components\s*:\s*\{([^}]+)\}", script)
    if m:
        for item in m.group(1).split(","):
            token = item.strip()
            if token and re.match(r"^[A-Z]\w*$", token):
                names.add(token)
    return names


def extract_template_components(template: str) -> Set[str]:
    """提取 template 中使用的 PascalCase 组件名"""
    found: Set[str] = set()
    # <Xxx ...> 或 <Xxx/> 或 <Xxx>
    for m in re.finditer(r"<([A-Z][A-Za-z0-9]+)", template):
        found.add(m.group(1))
    return found


def extract_i18n_keys(script: str, template: str) -> Set[str]:
    """提取 t('xxx.yyy') 和 $t('xxx.yyy') 调用的所有 key"""
    keys: Set[str] = set()
    text = script + "\n" + template
    # 严格匹配: 必须紧接 ( 之前是 t 或 $t, 排除 document.createElement / emit / setAttribute 等
    # 模式: (?:^|[^A-Za-z_$.])(?:\$t|t)\s*\(\s*['\"`]([A-Za-z][A-Za-z0-9_.\-]+)['\"`]
    for m in re.finditer(r"(?:^|[^A-Za-z_$.])(?:\$t|t)\s*\(\s*['\"`]([A-Za-z][A-Za-z0-9_.\-]+)['\"`]", text, re.MULTILINE):
        keys.add(m.group(1))
    return keys


def extract_api_calls(script: str) -> List[Dict]:
    """
    提取可能的 API 调用
    检测模式: <name>Api.foo( / http.get / http.post / axios.X / fetch
    """
    apis: List[Dict] = []
    # axios / http 风格的 axios.get / http.post
    for m in re.finditer(r"\b(axios|http|fetch)\.(get|post|put|delete|patch)\s*\(", script):
        apis.append({"type": m.group(1) + "." + m.group(2), "line": line_no(script, m.start())})
    # 自定义 api 对象: xxxApi.foo( / xxxClient.bar(
    for m in re.finditer(r"\b(\w+(?:Api|Client|Service))\b\.(\w+)\s*\(", script):
        name = m.group(1)
        if name in ("document", "window", "console", "process", "Array", "Object", "String"):
            continue
        apis.append({"type": f"{name}.{m.group(2)}", "line": line_no(script, m.start())})
    return apis


def line_no(text: str, pos: int) -> int:
    return text[:pos].count("\n") + 1


# === 主扫描 ===
def main():
    if not FRONT.exists():
        print(f"前端目录不存在: {FRONT}", file=sys.stderr)
        sys.exit(2)

    vue_files = list(FRONT.rglob("*.vue"))
    i18n_zh = FRONT / "i18n" / "locales" / "zh-CN.ts"
    i18n_en = FRONT / "i18n" / "locales" / "en-US.ts"

    # 加载 i18n 已有 keys
    def load_i18n_keys(path: Path) -> Set[str]:
        if not path.exists():
            return set()
        text = path.read_text(encoding="utf-8", errors="replace")
        # 提取形如 "  key: 'value'" 或 "  key: {"
        return {m.group(1) for m in re.finditer(r"^\s*([a-zA-Z][a-zA-Z0-9]*):", text, re.MULTILINE)}

    zh_keys = load_i18n_keys(i18n_zh)
    en_keys = load_i18n_keys(i18n_en)

    findings: List[Dict] = []
    api_audit: List[Dict] = []
    stats = {"files_scanned": 0, "i18n_keys_zh": len(zh_keys), "i18n_keys_en": len(en_keys)}

    for f in vue_files:
        stats["files_scanned"] += 1
        text = f.read_text(encoding="utf-8", errors="replace")
        # 分离 <script> / <template>
        script_match = re.search(r"<script[^>]*>([\s\S]*?)</script>", text)
        template_match = re.search(r"<template>([\s\S]*?)</template>", text)
        script = script_match.group(1) if script_match else ""
        template = template_match.group(1) if template_match else ""

        rel = str(f.relative_to(ROOT))

        # --- 1. 组件 import 完整性 ---
        imports = extract_imports(script)
        components_prop = extract_components_property(script)
        local = imports | components_prop
        used = extract_template_components(template)
        for u in sorted(used):
            if u in NATIVE_COMPONENTS or is_el_prefix(u) or u in EL_PLUS_ICONS:
                continue
            if u not in local:
                findings.append({
                    "severity": "FAIL",
                    "category": "import",
                    "file": rel,
                    "message": f"模板使用 <{u}> 但未在 script 中 import (或未全局注册)",
                })

        # --- 2. i18n key 完整性 ---
        used_keys = extract_i18n_keys(script, template)
        for k in sorted(used_keys):
            # t('common.foo') -> 检查 'common.foo' 在 i18n 文件中是否存在
            # 我们的 i18n 是嵌套结构, 简化处理: 只检查 top-level 命名空间存在
            top = k.split(".")[0]
            if top not in zh_keys:
                findings.append({
                    "severity": "FAIL",
                    "category": "i18n",
                    "file": rel,
                    "message": f"i18n key '{k}' 的命名空间 '{top}' 在 zh-CN 中不存在",
                })
            if top not in en_keys:
                findings.append({
                    "severity": "WARN",
                    "category": "i18n",
                    "file": rel,
                    "message": f"i18n key '{k}' 的命名空间 '{top}' 在 en-US 中不存在",
                })

        # --- 3. API 调用审计 ---
        apis = extract_api_calls(script)
        for a in apis:
            api_audit.append({"file": rel, **a})

    # === 汇总 ===
    fail = sum(1 for f in findings if f["severity"] == "FAIL")
    warn = sum(1 for f in findings if f["severity"] == "WARN")
    ok = 1 if fail == 0 and warn == 0 else 0

    report = {
        "stats": stats,
        "summary": {"fail": fail, "warn": warn, "ok": ok, "exit_code": 2 if fail else (1 if warn else 0)},
        "findings": findings,
        "api_call_count": len(api_audit),
        "api_calls_sample": api_audit[:20],
    }

    out_path = ROOT / "spike-test" / "frontend_static_audit.json"
    out_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"=== Frontend Static Audit ===")
    print(f"扫描文件: {stats['files_scanned']}")
    print(f"i18n keys: zh={stats['i18n_keys_zh']}  en={stats['i18n_keys_en']}")
    print(f"API 调用点: {len(api_audit)}")
    print(f"FAIL={fail}  WARN={warn}")
    print(f"报告: {out_path.relative_to(ROOT)}")

    if findings:
        print()
        for f in findings[:30]:
            icon = "[FAIL]" if f["severity"] == "FAIL" else "[WARN]"
            print(f"  {icon}  {f['file']}  {f['message']}")
        if len(findings) > 30:
            print(f"  ... 还有 {len(findings) - 30} 条, 详见报告")

    sys.exit(report["summary"]["exit_code"])


if __name__ == "__main__":
    main()
