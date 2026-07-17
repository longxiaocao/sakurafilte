// V2 Task 1.3.4: HTML 安全过滤器 (聚合搜索 _formatted 字段渲染用)
// 设计原则 (规则 4.3 禁止随意引入新依赖):
//   - 后端 MeiliSearchProvider.SanitizeFormatted 已做白名单 XSS 防御 (只允许 <mark> 标签)
//   - 前端双保险: 再做一次白名单过滤, 防止后端配置变更或 CDN 篡改导致 XSS
//   - 不引入 dompurify (体积 22KB), 改用 30 行正则实现等价效果
//
// 安全保证:
//   1. 转义所有 HTML 实体 (< > & " ')
//   2. 还原 <mark></mark> 为真实标签 (后端高亮标记)
//   3. 不允许任何其他 HTML 标签 (script/iframe/style/a/img 全部转义为文本)
//
// 使用场景: v-html 渲染 _formatted 字段前调用
//   <span v-html="sanitizeFormatted(hit.formatted?.product_name_1)"></span>

/**
 * V2 Task 1.3.4: HTML 白名单过滤 (只允许 <mark> 标签)
 * @param raw 后端返回的 _formatted 字段值 (已做 SanitizeFormatted, 但前端双保险)
 * @returns 安全的 HTML 字符串, 可直接 v-html 渲染
 */
export function sanitizeFormatted(raw: string | null | undefined): string {
  if (!raw) return ''
  // 步骤 1: 全量 HTML 转义 (所有 < > & " ' 都变实体)
  //   WHY 先全量转义: 假设后端可能被绕过 (配置错误/版本回退), 前端独立防御
  let s = escapeHtml(raw)
  // 步骤 2: 还原 <mark></mark> 为真实标签
  //   后端 SanitizeFormatted 输出形如 "Oil &lt;mark&gt;Filter&lt;/mark&gt;"
  //   转义后变成 "Oil &lt;mark&gt;Filter&lt;/mark&gt;"
  //   还原 &lt;mark&gt; → <mark>, &lt;/mark&gt; → </mark>
  s = s.replace(/&lt;mark&gt;/g, '<mark>').replace(/&lt;\/mark&gt;/g, '</mark>')
  return s
}

/**
 * HTML 实体转义 (防 XSS 基础工具)
 */
function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')  // 必须先转义 & 防止二次转义
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}
