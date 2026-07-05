"""
i18n 完整路径校验器
====================
WHY 之前的静态扫描有盲区: 只检查顶层命名空间 (如 'search' 是否存在),
  没检查完整路径 (如 'search.placeholder' / 'search.startSearch' 是否都有值).

扫描规则:
  1. 解析 zh-CN.ts 和 en-US.ts 的嵌套结构, 生成完整路径集合
  2. 扫描所有 .vue 文件中的 t('a.b.c') 和 $t('a.b.c') 调用
  3. 报告缺失的 key (zh 缺失 = FAIL, en 缺失 = WARN)
  4. 报告孤立的 key (在 locale 定义了但没有任何地方使用 = 死代码)

退出码: 0 干净 / 1 仅 WARN / 2 有 FAIL
"""
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Set, Tuple

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / "frontend" / "src"
I18N_ZH = FRONT / "i18n" / "locales" / "zh-CN.ts"
I18N_EN = FRONT / "i18n" / "locales" / "en-US.ts"
OUT = ROOT / "spike-test" / "i18n_fullpath_audit.json"


def parse_i18n(path: Path) -> Set[str]:
    """
    解析嵌套的 i18n TS 文件, 返回所有完整路径集合
    支持: key: 'value' (叶子), key: { ... } (嵌套对象)
    """
    if not path.exists():
        return set()

    text = path.read_text(encoding="utf-8", errors="replace")
    # 移除注释
    text = re.sub(r"//[^\n]*", "", text)
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    # 仅保留 export default { ... } 内容
    m = re.search(r"export\s+default\s*\{(.*)\}\s*$", text, re.DOTALL)
    if not m:
        m = re.search(r"export\s+default\s*\{(.*)", text, re.DOTALL)
    if not m:
        return set()
    body = m.group(1)

    paths: Set[str] = set()

    def walk(prefix: str, src: str, depth: int = 0):
        """递归遍历嵌套结构"""
        if depth > 5:  # 防过深
            return
        # 匹配每个顶层 key 块
        # 形态:  key: 'value'   或  key: { ... }  或  key: { ... nested ... }
        i = 0
        n = len(src)
        while i < n:
            # 跳过空白和逗号
            while i < n and src[i] in " \t\n\r,":
                i += 1
            if i >= n:
                break
            # 读 key
            m2 = re.match(r"([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*", src[i:])
            if not m2:
                i += 1
                continue
            key = m2.group(1)
            i += m2.end()
            # 读 value
            if i >= n:
                break
            if src[i] in "'\"":
                # 字符串值 (叶子)
                quote = src[i]
                i += 1
                while i < n and src[i] != quote:
                    if src[i] == "\\":
                        i += 2
                    else:
                        i += 1
                i += 1  # skip closing quote
                full = f"{prefix}{key}" if not prefix else f"{prefix}.{key}"
                paths.add(full)
            elif src[i] == "{":
                # 嵌套对象
                # 找到匹配的 }
                depth2 = 1
                j = i + 1
                while j < n and depth2 > 0:
                    if src[j] == "{":
                        depth2 += 1
                    elif src[j] == "}":
                        depth2 -= 1
                    elif src[j] in "'\"":
                        quote = src[j]
                        j += 1
                        while j < n and src[j] != quote:
                            if src[j] == "\\":
                                j += 2
                            else:
                                j += 1
                    j += 1
                # j 现在在 } 之后
                nested = src[i + 1 : j - 1]
                new_prefix = f"{prefix}{key}" if not prefix else f"{prefix}.{key}"
                walk(new_prefix, nested, depth + 1)
                i = j
            elif src[i] == "[":
                # 数组, 跳过
                depth2 = 1
                j = i + 1
                while j < n and depth2 > 0:
                    if src[j] == "[":
                        depth2 += 1
                    elif src[j] == "]":
                        depth2 -= 1
                    j += 1
                i = j
            else:
                # 数字/true/false 等, 跳过
                i += 1

    walk("", body)
    return paths


def extract_used_keys(text: str) -> Set[str]:
    """提取 t('a.b.c') / $t('a.b.c') / i18n.t('a.b.c') 调用"""
    keys: Set[str] = set()
    # 严格模式: t 之前不能是字母数字/_/$/. (排除 createElement('meta') 等)
    for m in re.finditer(
        r"(?:^|[^A-Za-z0-9_$.])(?:\$t|i18n\.t)\s*\(\s*['\"`]([A-Za-z][A-Za-z0-9_.\-]+)['\"`]",
        text, re.MULTILINE
    ):
        keys.add(m.group(1))
    # 兼容普通 t() (默认导入的 useI18n().t)
    # 只在没有 $t 前缀时匹配, 且前面是非字母
    for m in re.finditer(
        r"(?:^|[^A-Za-z0-9_$.])t\s*\(\s*['\"`]([A-Za-z][A-Za-z0-9_.\-]+)['\"`]",
        text, re.MULTILINE
    ):
        keys.add(m.group(1))
    return keys


def main():
    print("=" * 60)
    print("  i18n 完整路径校验器")
    print("=" * 60)

    zh_keys = parse_i18n(I18N_ZH)
    en_keys = parse_i18n(I18N_EN)
    print(f"  zh-CN: {len(zh_keys)} 个完整路径")
    print(f"  en-US: {len(en_keys)} 个完整路径")

    # 收集所有 .vue / .ts 中的 t() 调用
    used_keys: Set[str] = set()
    file_count = 0
    for f in list(FRONT.rglob("*.vue")) + list(FRONT.rglob("*.ts")):
        file_count += 1
        try:
            text = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        used_keys |= extract_used_keys(text)

    print(f"  扫描文件: {file_count}")
    print(f"  使用中的 key: {len(used_keys)}")

    # === 1. 缺失的 key ===
    missing_zh = sorted(used_keys - zh_keys)
    missing_en = sorted(used_keys - en_keys)
    # 排除常见非 i18n 字符串
    # (如 'zh' 'en' 'zh-CN' 'en-US' 等可能被误识别, 实际是 vue-i18n locale 参数)

    # === 2. 孤立的 key (定义了但没用) ===
    orphan_zh = sorted(zh_keys - used_keys)
    orphan_en = sorted(en_keys - used_keys)

    # === 3. zh 和 en 互查 (应等长, 避免单边漏译) ===
    only_zh = sorted(zh_keys - en_keys)
    only_en = sorted(en_keys - zh_keys)

    findings: List[Dict] = []
    for k in missing_zh:
        findings.append({"severity": "FAIL", "category": "missing_zh", "key": k,
                         "message": f"t('{k}') 但 zh-CN 中无此路径"})
    for k in missing_en:
        findings.append({"severity": "WARN", "category": "missing_en", "key": k,
                         "message": f"t('{k}') 但 en-US 中无此路径 (会 fallback 到 zh)"})
    for k in only_zh:
        findings.append({"severity": "WARN", "category": "only_zh", "key": k,
                         "message": f"'{k}' 仅在 zh 中, en 中未翻译"})
    for k in only_en:
        findings.append({"severity": "WARN", "category": "only_en", "key": k,
                         "message": f"'{k}' 仅在 en 中, zh 中未翻译"})
    # 死代码: 只列出前 20 个避免噪音
    for k in orphan_zh[:20]:
        findings.append({"severity": "INFO", "category": "orphan", "key": k,
                         "message": f"'{k}' 定义但未使用 (考虑删除)"})
    if len(orphan_zh) > 20:
        findings.append({"severity": "INFO", "category": "orphan", "key": "...",
                         "message": f"还有 {len(orphan_zh) - 20} 个未使用的 zh key"})

    fail = sum(1 for f in findings if f["severity"] == "FAIL")
    warn = sum(1 for f in findings if f["severity"] == "WARN")
    info = sum(1 for f in findings if f["severity"] == "INFO")

    report = {
        "stats": {
            "files_scanned": file_count,
            "zh_keys": len(zh_keys),
            "en_keys": len(en_keys),
            "used_keys": len(used_keys),
            "missing_zh": len(missing_zh),
            "missing_en": len(missing_en),
            "orphan_zh": len(orphan_zh),
            "only_zh": len(only_en),  # = en-only
            "only_en": len(only_zh),  # = zh-only
        },
        "summary": {"fail": fail, "warn": warn, "info": info, "exit_code": 2 if fail else (1 if warn else 0)},
        "findings": findings,
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print()
    print(f"  FAIL: {fail}  WARN: {warn}  INFO: {info}")
    print(f"  报告: {OUT.relative_to(ROOT)}")

    if findings:
        print()
        for f in findings[:30]:
            icon = {"FAIL": "[FAIL]", "WARN": "[WARN]", "INFO": "[INFO]"}[f["severity"]]
            print(f"  {icon}  {f['message']}")
        if len(findings) > 30:
            print(f"  ... 还有 {len(findings) - 30} 条")

    sys.exit(report["summary"]["exit_code"])


if __name__ == "__main__":
    main()
