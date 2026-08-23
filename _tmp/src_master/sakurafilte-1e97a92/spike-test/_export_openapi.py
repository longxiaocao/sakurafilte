"""
批次 6d: 导出 OpenAPI JSON Schema

WHY:
- Swagger UI 在开发环境可用, 但生产环境关闭 (防泄露)
- 离线分发场景需静态 API 文档 (用户偏好: 完全离线)
- CI 集成需要可机读的契约 (供前端 codegen / 联调测试)

WHAT:
- 启动后端, GET /swagger/v1/swagger.json
- 保存到 spike-test/openapi.json
- 同步生成 Markdown API 参考 (spike-test/API.md)
- 验证必填项完整性 (path/method/response 类型)

USE:
  python spike-test/_export_openapi.py                    # 完整导出
  python spike-test/_export_openapi.py --markdown-only    # 仅 Markdown
  python spike-test/_export_openapi.py --no-markdown      # 仅 JSON
"""
import argparse
import json
import sys
from pathlib import Path
from urllib import request, error

ROOT = Path(__file__).parent
JSON_PATH = ROOT / "openapi.json"
MD_PATH = ROOT / "API.md"
BACKEND = "http://localhost:5148"


def fetch_openapi() -> dict:
    """从 /swagger/v1/swagger.json 拉取 OpenAPI 3.0 schema"""
    url = f"{BACKEND}/swagger/v1/swagger.json"
    print(f"[*] 拉取 {url} ...")
    try:
        with request.urlopen(url, timeout=15) as resp:
            if resp.status != 200:
                print(f"[!] HTTP {resp.status}", file=sys.stderr)
                sys.exit(1)
            return json.loads(resp.read().decode("utf-8"))
    except error.URLError as e:
        print(f"[!] 无法连接后端 {url}: {e}", file=sys.stderr)
        print("    请先启动后端: cd backend/src/SakuraFilter.Api && dotnet run", file=sys.stderr)
        sys.exit(2)


def save_json(schema: dict) -> None:
    JSON_PATH.write_text(json.dumps(schema, ensure_ascii=False, indent=2), encoding="utf-8")
    size_kb = JSON_PATH.stat().st_size / 1024
    print(f"[OK] OpenAPI JSON: {JSON_PATH} ({size_kb:.1f} KB)")


def to_markdown(schema: dict) -> str:
    """从 OpenAPI schema 生成 Markdown API 参考"""
    lines: list[str] = []
    info = schema.get("info", {})
    lines.append(f"# {info.get('title', 'API')}")
    lines.append("")
    lines.append(f"**Version**: {info.get('version', '')}")
    lines.append("")
    if info.get("description"):
        # 多行 description 处理
        for desc_line in info["description"].split("\n"):
            desc_line = desc_line.strip()
            if desc_line:
                lines.append(desc_line)
            else:
                lines.append("")
        lines.append("")
    if info.get("contact"):
        c = info["contact"]
        lines.append(f"**Contact**: {c.get('name', '')} <{c.get('email', '')}>")
        lines.append("")

    lines.append("---")
    lines.append("")

    # 安全方案
    sec = schema.get("components", {}).get("securitySchemes", {})
    if sec:
        lines.append("## 认证方式")
        lines.append("")
        for name, defn in sec.items():
            lines.append(f"### {name}")
            lines.append("")
            lines.append(f"- **Type**: {defn.get('type', '')}")
            if defn.get("scheme"):
                lines.append(f"- **Scheme**: {defn['scheme']}")
            if defn.get("bearerFormat"):
                lines.append(f"- **Bearer Format**: {defn['bearerFormat']}")
            if defn.get("description"):
                lines.append(f"- **Description**: {defn['description']}")
            lines.append("")

    # 按 tag 分组, 改进: minimal API 端点按 URL 前缀推断 tag
    paths = schema.get("paths", {})
    by_tag: dict[str, list[tuple[str, str, dict]]] = {}
    no_tag: list[tuple[str, str, dict]] = []
    for path, methods in paths.items():
        for method, op in methods.items():
            if method.lower() not in ("get", "post", "put", "delete", "patch"):
                continue
            tags = op.get("tags")
            # 过滤掉泛化的 'SakuraFilter.Api' 标签, 改用 URL 前缀
            tags = [t for t in tags if t and t != "SakuraFilter.Api"] if tags else []
            if not tags:
                # 从 URL 推断: /api/admin/products → Admin/Products
                parts = [p for p in path.split("/") if p and p != "api"]
                if parts:
                    if parts[0] in ("admin", "public"):
                        tag = " ".join(p.capitalize() for p in parts[:2])
                    else:
                        tag = parts[0].capitalize()
                else:
                    tag = "Default"
                tags = [tag]
            for tag in tags:
                by_tag.setdefault(tag, []).append((path, method.upper(), op))

    # 统计
    total = sum(len(v) for v in by_tag.values())
    lines.append("## 端点索引")
    lines.append("")
    lines.append(f"共 **{len(paths)}** 个路径, **{total}** 个端点, 分布在 **{len(by_tag)}** 个模块:")
    lines.append("")
    for tag in sorted(by_tag.keys()):
        lines.append(f"- [{tag}](#{tag.lower().replace(' ', '-')}) ({len(by_tag[tag])} 端点)")
    lines.append("")

    # 详细
    for tag in sorted(by_tag.keys()):
        lines.append(f"## {tag}")
        lines.append("")
        for path, method, op in sorted(by_tag[tag]):
            lines.append(f"### {method} {path}")
            lines.append("")
            summary = op.get("summary", "")
            if summary:
                lines.append(f"**{summary}**")
                lines.append("")
            desc = op.get("description", "")
            if desc:
                for dl in desc.split("\n"):
                    dl = dl.strip()
                    if dl:
                        lines.append(dl)
                lines.append("")

            # Parameters
            params = op.get("parameters", [])
            if params:
                lines.append("**Parameters**:")
                lines.append("")
                lines.append("| Name | In | Type | Required | Description |")
                lines.append("|------|----|------|----------|-------------|")
                for p in params:
                    schema_p = p.get("schema", {})
                    ptype = schema_p.get("type") or schema_p.get("$ref", "?")
                    if "$ref" in str(ptype):
                        ptype = ptype.split("/")[-1]
                    lines.append(f"| `{p.get('name', '')}` | {p.get('in', '')} | {ptype} | "
                                 f"{'✓' if p.get('required') else '✗'} | {p.get('description', '')} |")
                lines.append("")

            # Request body
            rb = op.get("requestBody", {})
            if rb:
                lines.append("**Request Body**:")
                lines.append("")
                content = rb.get("content", {})
                for ctype, defn in content.items():
                    sch = defn.get("schema", {})
                    if "$ref" in str(sch):
                        ref_name = sch["$ref"].split("/")[-1]
                        lines.append(f"- Content-Type: `{ctype}` (schema: `{ref_name}`)")
                    else:
                        lines.append(f"- Content-Type: `{ctype}`")
                lines.append("")

            # Responses
            responses = op.get("responses", {})
            if responses:
                lines.append("**Responses**:")
                lines.append("")
                for code, resp in responses.items():
                    desc = resp.get("description", "")
                    lines.append(f"- `{code}`: {desc}")
                lines.append("")

            lines.append("---")
            lines.append("")

    # Models
    components = schema.get("components", {}).get("schemas", {})
    if components:
        lines.append("## 数据模型 (Schemas)")
        lines.append("")
        for name in sorted(components.keys()):
            sch = components[name]
            lines.append(f"### {name}")
            lines.append("")
            if sch.get("description"):
                for dl in sch["description"].split("\n"):
                    dl = dl.strip()
                    if dl:
                        lines.append(dl)
                lines.append("")
            props = sch.get("properties", {})
            required = set(sch.get("required", []))
            if props:
                lines.append("| Field | Type | Required | Description |")
                lines.append("|-------|------|----------|-------------|")
                for pname, pdef in props.items():
                    ptype = pdef.get("type") or pdef.get("$ref", "?")
                    if "$ref" in str(ptype):
                        ptype = ptype.split("/")[-1]
                    elif pdef.get("format"):
                        ptype = f"{ptype} ({pdef['format']})"
                    desc = pdef.get("description", "")
                    lines.append(f"| `{pname}` | {ptype} | {'✓' if pname in required else '✗'} | {desc} |")
                lines.append("")

    lines.append("---")
    lines.append("")
    lines.append("> 文档由 `_export_openapi.py` 自动生成于 OpenAPI 3.0 schema。")
    lines.append("> Swagger UI (开发环境): http://localhost:5148/swagger")

    return "\n".join(lines)


def save_markdown(schema: dict) -> None:
    md = to_markdown(schema)
    MD_PATH.write_text(md, encoding="utf-8")
    size_kb = MD_PATH.stat().st_size / 1024
    print(f"[OK] API Markdown: {MD_PATH} ({size_kb:.1f} KB)")


def validate_schema(schema: dict) -> None:
    """验证 schema 完整性"""
    issues = []
    paths = schema.get("paths", {})
    if not paths:
        issues.append("paths 为空")
    for path, methods in paths.items():
        for method, op in methods.items():
            if method.lower() not in ("get", "post", "put", "delete", "patch"):
                continue
            if not op.get("summary"):
                issues.append(f"{method.upper()} {path}: 缺 summary")
            if not op.get("responses"):
                issues.append(f"{method.upper()} {path}: 缺 responses")
    if issues:
        print(f"[WARN] {len(issues)} 项可改进:")
        for i in issues[:10]:
            print(f"  - {i}")
        if len(issues) > 10:
            print(f"  ... +{len(issues) - 10} 项")
    else:
        print("[OK] 所有端点均含 summary + responses")


def main():
    p = argparse.ArgumentParser(description="导出 OpenAPI 文档")
    p.add_argument("--no-markdown", action="store_true", help="不生成 Markdown")
    p.add_argument("--markdown-only", action="store_true", help="不保存 JSON")
    p.add_argument("--validate", action="store_true", help="额外验证完整性")
    args = p.parse_args()

    schema = fetch_openapi()

    if not args.markdown_only:
        save_json(schema)
        if args.validate:
            validate_schema(schema)

    if not args.no_markdown:
        save_markdown(schema)

    # 统计
    paths = schema.get("paths", {})
    total = sum(1 for m in paths.values() for k in m if k.lower() in ("get", "post", "put", "delete", "patch"))
    print(f"\n[i] 统计: {len(paths)} 路径, {total} 端点, "
          f"{len(schema.get('components', {}).get('schemas', {}))} schemas")


if __name__ == "__main__":
    main()
