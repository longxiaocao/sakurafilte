"""改进 1: OpenAPI 契约自动生成 — 从 Swagger JSON 生成 TypeScript interface

WHY: 项目存在 6 类前后端字段不匹配问题 (cascade/machineCategory/imageUrl 等),
     根因是手工维护 types.ts。本脚本从后端 Swagger 自动生成 types, 消除手工错误。

使用:
  python _gen_types.py [--output frontend/src/api/generated-types.ts]

工作流:
  1. 后端启动后, 拉取 /swagger/v1/swagger.json
  2. 解析 components.schemas, 提取 record/class 定义的 DTO
  3. 生成 TypeScript interface (camelCase 字段)
  4. 输出到 frontend/src/api/generated-types.ts
  5. 开发时可 diff 手工 types.ts 与生成 types, 发现字段漂移

不替换手工 types.ts (含业务注释), 而是生成对照文件供审查。
"""
import json
import sys
import urllib.request
from pathlib import Path

BASE = "http://localhost:5148"
SWAGGER_URL = f"{BASE}/swagger/v1/swagger.json"
DEFAULT_OUTPUT = Path(__file__).parent.parent / "frontend" / "src" / "api" / "generated-types.ts"

# C# 类型 → TypeScript 类型映射
TYPE_MAP = {
    "string": "string",
    "integer": "number",
    "int32": "number",
    "int64": "number",
    "long": "number",
    "number": "number",
    "float": "number",
    "double": "number",
    "decimal": "number",
    "boolean": "boolean",
    "bool": "boolean",
    "DateTime": "string",
    "DateTimeOffset": "string",
    "DateOnly": "string",
    "TimeOnly": "string",
    "Guid": "string",
    "object": "Record<string, any>",
    "any": "any",
}


def fetch_swagger():
    """拉取 Swagger JSON"""
    req = urllib.request.Request(SWAGGER_URL)
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read())


def cs_type_to_ts(cs_type: str, nullable: bool = False) -> str:
    """C# 类型名转 TypeScript"""
    # 处理 nullable (如 int?)
    base = cs_type.replace("?", "").strip()
    # 处理泛型集合
    if base.startswith("List<") or base.startswith("IEnumerable<") or base.startswith("ICollection<"):
        inner = base.split("<", 1)[1].rstrip(">")
        return f"{cs_type_to_ts(inner)}[]"
    if base.startswith("Dictionary<") or base.startswith("IDictionary<"):
        return "Record<string, any>"
    ts = TYPE_MAP.get(base, "any")
    return f"{ts} | null" if nullable else ts


def schema_to_interface(name: str, schema: dict) -> str:
    """将 OpenAPI schema 转为 TypeScript interface"""
    lines = [f"export interface {name} {{"]
    props = schema.get("properties", {})
    required = set(schema.get("required", []))

    for prop_name, prop_schema in props.items():
        # OpenAPI 用 camelCase (ASP.NET Core System.Text.Json 默认)
        ts_name = prop_name
        # 类型推断
        prop_type = prop_schema.get("type", "")
        if prop_type == "array":
            items = prop_schema.get("items", {})
            # WHY: array item 类型必须走 TYPE_MAP 映射 (integer→number, 否则生成 integer[] 非法 TS)
            if "$ref" in items:
                ts_type = f"{items['$ref'].split('/')[-1]}[]"
            else:
                raw_item = items.get("type", "any")
                ts_type = f"{TYPE_MAP.get(raw_item, raw_item)}[]"
        elif prop_type == "integer" or prop_type == "number":
            ts_type = "number"
        elif prop_type == "boolean":
            ts_type = "boolean"
        elif prop_type == "string":
            ts_type = "string"
        elif "$ref" in prop_schema:
            ts_type = prop_schema["$ref"].split("/")[-1]
        else:
            ts_type = "any"

        # nullable 处理
        nullable = prop_schema.get("nullable", False) or prop_name not in required
        if nullable and not ts_type.endswith("null") and ts_type != "any":
            ts_type = f"{ts_type} | null"

        # 可选字段用 ?
        optional = "?" if nullable else ""
        lines.append(f"  {ts_name}{optional}: {ts_type}")

    lines.append("}")
    return "\n".join(lines)


def generate_types(swagger: dict) -> str:
    """生成完整的 TypeScript 文件"""
    schemas = swagger.get("components", {}).get("schemas", {})
    # 过滤: 只生成业务 DTO (record 类型), 排除框架内置 (ProblemDetails 等)
    skip_prefixes = ("ProblemDetails", "HttpValidation", "Microsoft", "AspNetCore")

    interfaces = []
    skipped = []
    for name, schema in schemas.items():
        if any(name.startswith(p) for p in skip_prefixes):
            skipped.append(name)
            continue
        # 只处理 object 类型 (有 properties)
        if schema.get("type") == "object" and schema.get("properties"):
            interfaces.append(schema_to_interface(name, schema))

    header = """// ============================================
// 自动生成 — 请勿手工修改
// 生成方式: python _gen_types.py
// 数据源: /swagger/v1/swagger.json
// 用途: 与手工 types.ts 对照, 发现字段漂移
// ============================================

"""
    footer = f"""
// 共生成 {len(interfaces)} 个 interface (跳过 {len(skipped)} 个框架内置 schema)
"""
    return header + "\n\n".join(interfaces) + footer


def main():
    output = Path(sys.argv[2]) if len(sys.argv) > 2 and sys.argv[1] == "--output" else DEFAULT_OUTPUT

    print(f"拉取 Swagger: {SWAGGER_URL}")
    swagger = fetch_swagger()
    print(f"  schemas: {len(swagger.get('components', {}).get('schemas', {}))} 个")

    typescript = generate_types(swagger)

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(typescript, encoding="utf-8")
    print(f"  生成: {output}")
    print(f"  interface 数: {typescript.count('export interface')}")

    # 对照报告
    manual_types = output.parent / "types.ts"
    if manual_types.exists():
        manual_text = manual_types.read_text(encoding="utf-8")
        manual_interfaces = set()
        for line in manual_text.split("\n"):
            if line.strip().startswith("export interface "):
                name = line.strip().split("export interface ")[1].split(" ")[0].split("{")[0].strip()
                manual_interfaces.add(name)
        generated_interfaces = set()
        for line in typescript.split("\n"):
            if line.strip().startswith("export interface "):
                name = line.strip().split("export interface ")[1].split(" ")[0].split("{")[0].strip()
                generated_interfaces.add(name)
        only_manual = manual_interfaces - generated_interfaces
        only_generated = generated_interfaces - manual_interfaces
        print(f"\n===== 字段漂移对照 =====")
        print(f"  手工 types.ts: {len(manual_interfaces)} interface")
        print(f"  生成 types: {len(generated_interfaces)} interface")
        if only_manual:
            print(f"  仅手工有 (生成缺失): {sorted(only_manual)}")
        if only_generated:
            print(f"  仅生成有 (手工缺失): {sorted(only_generated)}")
        if not only_manual and not only_generated:
            print(f"  ✓ interface 名称完全一致")


if __name__ == "__main__":
    main()
