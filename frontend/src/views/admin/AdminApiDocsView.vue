<script setup lang="ts">
// 批次 6d: API 文档浏览器
//   - 实时拉取 /swagger/v1/swagger.json (开发环境)
//   - 按模块分组展示端点 (方法 + 路径 + 摘要 + 参数 + 响应)
//   - 支持搜索/筛选/复制
//   - 加载失败时回退到内嵌的 openapi.json (开发时一键导出)
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'

interface Param { name: string; in: string; required: boolean; description: string; schema: any }
interface Response { code: string; description: string }
interface Endpoint { method: string; path: string; op: any; params: Param[]; responses: Response[]; tag: string }
interface Module { name: string; endpoints: Endpoint[] }

const schema = ref<any>(null)
const loading = ref(false)
const search = ref('')
const filterTag = ref<string>('')
const expanded = ref<Set<string>>(new Set())

async function fetchSchema() {
  loading.value = true
  try {
    // 优先从后端 Swagger 拉 (实时)
    let r = await fetch('/swagger/v1/swagger.json')
    if (r.ok) {
      schema.value = await r.json()
    } else {
      throw new Error('Swagger 不可用, 尝试回退')
    }
  } catch {
    // 回退: 加载本地 openapi.json
    try {
      const r2 = await fetch('/openapi.json')
      if (r2.ok) {
        schema.value = await r2.json()
        ElMessage.warning('已加载离线 openapi.json, 后端 Swagger 不可用')
      } else {
        ElMessage.error('无法加载 API 文档 (Swagger + 离线备份均失败)')
      }
    } catch {
      ElMessage.error('无法加载 API 文档')
    }
  } finally {
    loading.value = false
  }
}

const modules = computed<Module[]>(() => {
  if (!schema.value) return []
  const paths = schema.value.paths || {}
  const byTag: Record<string, Endpoint[]> = {}

  for (const [path, methods] of Object.entries(paths)) {
    for (const [method, op] of Object.entries(methods as any)) {
      if (!['get', 'post', 'put', 'delete', 'patch'].includes(method.toLowerCase())) continue
      let tags = (op as any).tags?.filter((t: string) => t && t !== 'SakuraFilter.Api') || []
      if (tags.length === 0) {
        const parts = path.split('/').filter((p: string) => p && p !== 'api')
        if (parts.length === 0) tags = ['Default']
        else if (parts[0] === 'admin' || parts[0] === 'public') {
          tags = [parts[0].charAt(0).toUpperCase() + parts[0].slice(1) + ' ' + (parts[1] || '').charAt(0).toUpperCase() + (parts[1] || '').slice(1)]
        } else {
          tags = [parts[0].charAt(0).toUpperCase() + parts[0].slice(1)]
        }
      }

      const params: Param[] = ((op as any).parameters || []).map((p: any) => ({
        name: p.name,
        in: p.in,
        required: !!p.required,
        description: p.description || '',
        schema: p.schema || {},
      }))

      const responses: Response[] = Object.entries((op as any).responses || {}).map(([code, r]: [string, any]) => ({
        code,
        description: r.description || '',
      }))

      const ep: Endpoint = { method: method.toUpperCase(), path, op, params, responses, tag: tags[0] }
      byTag[tags[0]] = byTag[tags[0]] || []
      byTag[tags[0]].push(ep)
    }
  }

  return Object.entries(byTag)
    .map(([name, endpoints]) => ({ name, endpoints: endpoints.sort((a, b) => a.path.localeCompare(b.path)) }))
    .sort((a, b) => a.name.localeCompare(b.name))
})

const allTags = computed(() => modules.value.map((m) => m.name))

const filteredModules = computed<Module[]>(() => {
  let mods = modules.value
  if (filterTag.value) {
    mods = mods.filter((m) => m.name === filterTag.value)
  }
  if (search.value.trim()) {
    const q = search.value.toLowerCase()
    mods = mods
      .map((m) => ({
        ...m,
        endpoints: m.endpoints.filter(
          (e) =>
            e.path.toLowerCase().includes(q) ||
            e.method.toLowerCase().includes(q) ||
            (e.op.summary || '').toLowerCase().includes(q),
        ),
      }))
      .filter((m) => m.endpoints.length > 0)
  }
  return mods
})

const totalEndpoints = computed(() => modules.value.reduce((s, m) => s + m.endpoints.length, 0))
const totalSchemas = computed(() => Object.keys(schema.value?.components?.schemas || {}).length)

function toggle(path: string, method: string) {
  const key = `${method}-${path}`
  if (expanded.value.has(key)) expanded.value.delete(key)
  else expanded.value.add(key)
  // 触发响应式更新
  expanded.value = new Set(expanded.value)
}

function isExpanded(path: string, method: string): boolean {
  return expanded.value.has(`${method}-${path}`)
}

function methodColor(method: string): string {
  return {
    GET: 'text-blue-600',
    POST: 'text-green-600',
    PUT: 'text-yellow-600',
    DELETE: 'text-red-600',
    PATCH: 'text-purple-600',
  }[method] || 'text-neutral-700'
}

function responseColor(code: string): string {
  const c = parseInt(code)
  if (c >= 200 && c < 300) return 'text-green-600'
  if (c >= 300 && c < 400) return 'text-blue-600'
  if (c >= 400 && c < 500) return 'text-yellow-600'
  if (c >= 500) return 'text-red-600'
  return 'text-neutral-700'
}

async function copyCurl(ep: Endpoint) {
  const lines: string[] = []
  lines.push(`curl -X ${ep.method} 'http://localhost:5148${ep.path}'`)
  if (ep.params.filter((p) => p.in === 'query').length) {
    const qs = ep.params.filter((p) => p.in === 'query').map((p) => `${p.name}=VALUE`).join('&')
    lines[0] += `?${qs}`
  }
  if (ep.method !== 'GET' && ep.op.requestBody) {
    lines.push(`  -H 'Content-Type: application/json'`)
    lines.push(`  -H 'Authorization: Bearer <token>'`)
    lines.push(`  -d '{}'`)
  } else {
    lines.push(`  -H 'Authorization: Bearer <token>'`)
  }
  const text = lines.join(' \\\n')
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('cURL 已复制')
  } catch {
    ElMessage.error('复制失败')
  }
}

function refresh() {
  fetchSchema()
}

onMounted(fetchSchema)
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <div class="flex items-center justify-between mb-3 flex-wrap gap-2">
      <div>
        <h1 class="text-lg font-medium">API 文档</h1>
        <p class="text-xs text-muted">
          批次 6d — OpenAPI 3.0 浏览器
          <span v-if="schema">· 实时拉取自 /swagger/v1/swagger.json</span>
        </p>
      </div>
      <div class="flex items-center gap-2">
        <button
          @click="refresh"
          :disabled="loading"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)] disabled:opacity-50"
        >{{ loading ? '加载中…' : '↻ 刷新' }}</button>
        <a
          href="http://localhost:5148/swagger"
          target="_blank"
          rel="noopener"
          class="px-2 py-1 text-xs hairline hover:bg-[var(--color-bg-hover)]"
        >Swagger UI ↗</a>
      </div>
    </div>

    <!-- 统计 -->
    <div v-if="schema" class="grid grid-cols-2 md:grid-cols-3 gap-2 mb-3">
      <div class="hairline p-2">
        <div class="text-xs text-muted">模块数</div>
        <div class="text-lg font-medium">{{ modules.length }}</div>
      </div>
      <div class="hairline p-2">
        <div class="text-xs text-muted">端点数</div>
        <div class="text-lg font-medium">{{ totalEndpoints }}</div>
      </div>
      <div class="hairline p-2">
        <div class="text-xs text-muted">数据模型</div>
        <div class="text-lg font-medium">{{ totalSchemas }}</div>
      </div>
    </div>

    <!-- 过滤 -->
    <div class="flex items-center gap-2 mb-3 flex-wrap">
      <select
        v-model="filterTag"
        class="px-2 py-1 text-xs hairline bg-[var(--color-bg-elevated)]"
        aria-label="按模块筛选"
      >
        <option value="">全部模块</option>
        <option v-for="t in allTags" :key="t" :value="t">{{ t }}</option>
      </select>
      <input
        v-model="search"
        type="text"
        placeholder="搜索路径 / 方法 / 摘要…"
        class="px-2 py-1 text-xs hairline bg-[var(--color-bg-elevated)] flex-1 min-w-[200px]"
        aria-label="搜索端点"
      />
      <span class="text-xs text-muted">
        显示 {{ filteredModules.reduce((s, m) => s + m.endpoints.length, 0) }} / {{ totalEndpoints }}
      </span>
    </div>

    <!-- 加载中 -->
    <div v-if="loading && !schema" class="text-center text-sm text-muted py-8">加载中…</div>
    <div v-else-if="!schema" class="text-center text-sm text-muted py-8">
      暂无数据, 请确认后端已启动 (http://localhost:5148)
    </div>

    <!-- 模块列表 -->
    <div v-else class="space-y-3">
      <section v-for="mod in filteredModules" :key="mod.name" class="hairline p-3">
        <h2 class="text-base font-medium mb-2">
          {{ mod.name }}
          <span class="text-xs text-muted">({{ mod.endpoints.length }} 端点)</span>
        </h2>
        <ul class="divide-y divide-[var(--color-border)]">
          <li v-for="ep in mod.endpoints" :key="ep.method + ep.path" class="py-2">
            <div class="flex items-center gap-2 flex-wrap">
              <button
                @click="toggle(ep.path, ep.method)"
                class="flex items-center gap-2 flex-1 text-left hover:bg-[var(--color-bg-hover)] -m-1 p-1"
              >
                <span class="text-xs">{{ isExpanded(ep.path, ep.method) ? '▼' : '▶' }}</span>
                <span :class="['text-xs font-mono font-medium uppercase shrink-0', methodColor(ep.method)]">
                  {{ ep.method }}
                </span>
                <code class="text-sm break-all">{{ ep.path }}</code>
              </button>
              <button
                @click="copyCurl(ep)"
                class="px-2 py-0.5 text-[10px] hairline hover:bg-[var(--color-bg-hover)]"
                :aria-label="`复制 ${ep.method} ${ep.path} 的 cURL`"
              >cURL</button>
            </div>
            <div v-if="ep.op.summary" class="text-xs text-muted ml-7 mt-0.5">
              {{ ep.op.summary }}
            </div>
            <div v-if="isExpanded(ep.path, ep.method)" class="ml-7 mt-2 text-xs space-y-2">
              <div v-if="ep.params.length">
                <div class="font-medium mb-1">Parameters:</div>
                <table class="w-full text-xs">
                  <thead class="text-left text-muted">
                    <tr>
                      <th class="pr-2">Name</th>
                      <th class="pr-2">In</th>
                      <th class="pr-2">Required</th>
                      <th>Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="p in ep.params" :key="p.name" class="border-t border-[var(--color-border)]">
                      <td class="pr-2 font-mono">{{ p.name }}</td>
                      <td class="pr-2">{{ p.in }}</td>
                      <td class="pr-2">{{ p.required ? '✓' : '✗' }}</td>
                      <td>{{ p.description || '—' }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div v-if="ep.responses.length">
                <div class="font-medium mb-1">Responses:</div>
                <ul>
                  <li v-for="r in ep.responses" :key="r.code">
                    <span :class="['font-mono', responseColor(r.code)]">{{ r.code }}</span>
                    {{ r.description }}
                  </li>
                </ul>
              </div>
              <div v-if="ep.op.description" class="text-muted">
                <div class="font-medium mb-1">Description:</div>
                <pre class="whitespace-pre-wrap font-sans">{{ ep.op.description }}</pre>
              </div>
            </div>
          </li>
        </ul>
      </section>
    </div>

    <div v-if="!loading && schema && filteredModules.length === 0" class="text-center text-sm text-muted py-8">
      无匹配的端点
    </div>
  </div>
</template>
