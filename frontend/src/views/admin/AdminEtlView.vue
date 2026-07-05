<script setup lang="ts">
// Day 9: 后台 ETL 触发 + 实时进度页
//   - 实体选择: products / xrefs / apps
//   - 模式选择: full-load / insert-only / upsert
//   - dry-run 切换: 仅校验文件 + 行数, 不写库
//   - 3s 轮询 /api/admin/etl/progress 实时显示状态
//   - 上次完成结果快照 + recent errors
import { ref, reactive, onMounted, onBeforeUnmount, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { etlApi } from '@/api'
import type { EtlActiveTaskInfo, EtlProgress, EtlDryRunResult, EtlHistoryItem, EtlReasonCodeAggregate } from '@/api/types'
import EtlReasonCodePie from '@/components/EtlReasonCodePie.vue'
import { useGlobalDragDrop, DEFAULT_ADMIN_ACCEPT } from '@/composables/useGlobalDragDrop'

// ===== 表单 =====
// Day 11 Phase 1: cascade 默认 true (兼容旧行为), UI 可切换为 false 单独刷新主表
const form = reactive({
  entity: 'products' as 'products' | 'xrefs' | 'apps',
  mode: 'upsert' as 'full-load' | 'insert-only' | 'upsert',
  dryRun: false,
  cascade: true,  // 仅 products + full-load 生效
  jsonlPath: 'D:/data/sakurafilter/products.jsonl'
})

const entityPaths: Record<string, string> = {
  products: 'D:/data/sakurafilter/products.jsonl',
  xrefs: 'D:/data/sakurafilter/xrefs.jsonl',
  apps: 'D:/data/sakurafilter/apps.jsonl'
}

function changeEntity(v: 'products' | 'xrefs' | 'apps') {
  form.entity = v
  form.jsonlPath = entityPaths[v]
}

// ===== 全局拖拽上传集成 (Day 14+: UX 偏好) =====
//   拖动 .jsonl 到窗口 → 自动识别 entity + 填入路径
//   浏览器安全: 拿不到绝对路径, 用默认基础目录 + file.name 拼出服务端路径
//   用户可手动修改最终路径
const { register: registerDrag, unregister: unregisterDrag } = useGlobalDragDrop()

// 服务端默认基础目录 (与后端 PgmDefaultConfig 对齐)
const SERVER_BASE_DIR = 'D:/data/sakurafilter'

function inferEntityByName(name: string): 'products' | 'xrefs' | 'apps' | null {
  const lower = name.toLowerCase()
  if (lower.includes('product')) return 'products'
  if (lower.includes('xref') || lower.includes('cross')) return 'xrefs'
  if (lower.includes('app') || lower.includes('machine')) return 'apps'
  return null
}

function handleFilesDropped(files: File[]) {
  if (files.length === 0) return
  // 取第一个文件
  const f = files[0]
  const inferred = inferEntityByName(f.name)
  // 拼出服务端路径 (后端用 Path.Exists 校验)
  const serverPath = `${SERVER_BASE_DIR}/${f.name}`
  if (inferred) {
    form.entity = inferred
    form.jsonlPath = serverPath
    ElMessage.success(`已自动识别 entity=${inferred}, 文件: ${f.name}`)
  } else {
    form.jsonlPath = serverPath
    ElMessage.info(`已填入文件: ${f.name} (entity 需手动选择)`)
  }
  if (files.length > 1) {
    ElMessage.warning(`本次拖入 ${files.length} 个文件, 仅采用第一个: ${f.name}`)
  }
}

onMounted(() => {
  // 注册全局拖拽 (admin 路径启用)
  registerDrag({
    onFilesDropped: handleFilesDropped,
    acceptRoute: DEFAULT_ADMIN_ACCEPT,
    hintText: '松开以填入 ETL 文件路径'
  })
})

onBeforeUnmount(() => {
  unregisterDrag()
})

// ===== 触发 =====
const submitting = ref(false)
const cancelling = ref(false)
// P1.1 (Task 3): 暂停/恢复状态
const pausing = ref(false)
const resuming = ref(false)
async function doTrigger() {
  try {
    await ElMessageBox.confirm(
      `即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? ' - dry-run' : ''}), 是否继续?`,
      '确认',
      { type: 'warning' }
    )
  } catch {
    return
  }
  submitting.value = true
  try {
    const r = await etlApi.trigger({
      jsonlPath: form.jsonlPath,
      mode: form.mode,
      entityType: form.entity,  // Day 11 Phase 1 BUG FIX: 之前漏传, 后端硬编码 products
      cascade: form.cascade,    // Day 11 Phase 1: cascade 安全锁
      dryRun: form.dryRun
    })
    ElMessage.success(form.dryRun ? 'dry-run 校验完成' : '已触发 ETL, 后台执行中')
    // 触发后立即拉一次进度
    await pollOnce()
    // dry-run 模式下 r 是 { dryRun, file, mode, lines, sizeBytes }
    if (form.dryRun) {
      lastDryRun.value = r as any
    }
  } catch (e: any) {
    // 已被拦截器处理
  } finally {
    submitting.value = false
  }
}

// ===== 进度轮询 =====
const task = ref<EtlActiveTaskInfo>({ inProgress: false })
const lastFinished = ref<EtlProgress | null>(null)
const lastDryRun = ref<EtlDryRunResult | null>(null)
// Day 9.4: dry-run 样本展开/收起 (50 行太长, 默认只显示 10 行)
const showAllSamples = ref(false)

// Day 9.8: 取消原因 reason_code 饼图数据 (运营审计)
const reasonCodeAgg = ref<EtlReasonCodeAggregate | null>(null)
const historyItems = ref<EtlHistoryItem[]>([])
const historyLoading = ref(false)

async function refreshAudit() {
  historyLoading.value = true
  try {
    const [agg, hist] = await Promise.all([
      etlApi.reasonCodeAggregate(),
      etlApi.history(20, 'cancelled')
    ])
    reasonCodeAgg.value = agg
    historyItems.value = hist.items
  } catch (e) {
    // 已被拦截器处理
  } finally {
    historyLoading.value = false
  }
}

// Day 9.1: 持久化最近一次完成结果到 localStorage (刷新页面不丢)
const LS_KEY_FINISHED = 'sakura_etl_last_finished'
try {
  const cached = localStorage.getItem(LS_KEY_FINISHED)
  if (cached) lastFinished.value = JSON.parse(cached)
} catch {
  // 忽略解析失败
}

let pollTimer: number | null = null

async function pollOnce() {
  try {
    const r = await etlApi.progress()
    task.value = r
    // 任务刚结束 (inProgress 由 true 变 false) → 拉一次 legacy status 拿到最终结果
    if (!r.inProgress && r.activeTask && r.activeTask.status === 'completed') {
      const legacy = await etlApi.legacyStatus()
      if (legacy && (legacy as any).status !== 'running') {
        lastFinished.value = legacy
        try {
          localStorage.setItem(LS_KEY_FINISHED, JSON.stringify(legacy))
        } catch {
          // 忽略写入失败 (容量 / 隐私模式)
        }
      }
    }
  } catch (e: any) {
    // 已被拦截器处理
  }
}

// Day 9.4: SSE 替换 3s 轮询
//   关闭 EventSource = 停止订阅, 不需要手动 clearInterval
let eventSource: EventSource | null = null

function connectSSE() {
  if (eventSource) eventSource.close()
  const es = new EventSource("/api/admin/etl/progress/stream")
  es.onmessage = (e) => {
    try {
      const r = JSON.parse(e.data)
      task.value = r
      // 任务刚结束 (inProgress 由 true 变 false) → 拉一次 legacy status 拿到最终结果
      if (!r.inProgress && r.activeTask && r.activeTask.status === 'completed') {
        etlApi.legacyStatus().then((legacy) => {
          if (legacy && (legacy as any).status !== 'running') {
            lastFinished.value = legacy
            try { localStorage.setItem(LS_KEY_FINISHED, JSON.stringify(legacy)) } catch {}
          }
        }).catch(() => {})
      }
      // Day 9.8: 任务进入终态 (completed/failed/cancelled) → 刷新审计饼图 + 历史列表
      if (!r.inProgress && r.activeTask &&
          ['completed', 'failed', 'cancelled'].includes(r.activeTask.status)) {
        refreshAudit()
      }
      // P1.1 (Task 3): 任务进入 paused 状态 → 刷新"恢复"按钮可见性
      if (r.activeTask && r.activeTask.status === 'paused') {
        checkPausedTask()
      }
    } catch {}
  }
  es.onerror = () => {
    // 浏览器会自动重连, 这里只打印 (debug 级避免污染生产控制台)
    // WHY console.debug: SSE 临时断开是常态 (代理/网络抖动), 不应被监控告警捕获
    console.debug('SSE 连接断开, 浏览器将自动重连')
  }
  eventSource = es
}

onMounted(() => {
  connectSSE()
  // Day 9.8: 进入页面立即拉一次审计 (避免要等下次 ETL 完结才显示)
  refreshAudit()
})

onBeforeUnmount(() => {
  if (eventSource) {
    eventSource.close()
    eventSource = null
  }
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})


function clearLastFinished() {
  lastFinished.value = null
  try {
    localStorage.removeItem(LS_KEY_FINISHED)
  } catch {
    // 忽略
  }
  ElMessage.success('已清除')
}

// Day 9.6: 取消原因枚举白名单
//   WHY 固定: 与后端 EtlProgress.AllowedReasonCodes 对齐, 避免传任意字符串
//   运营审计按 reason_code 聚合 (USER_REQUEST/TIMEOUT/SYSTEM_SHUTDOWN/ADMIN_OVERRIDE/OTHER)
const reasonCodeOptions = [
  { value: 'USER_REQUEST', label: '用户主动取消', defaultReason: '用户主动取消' },
  { value: 'ADMIN_OVERRIDE', label: '管理员强制取消', defaultReason: '管理员强制取消' },
  { value: 'TIMEOUT', label: '任务超时', defaultReason: '任务执行超时' },
  { value: 'SYSTEM_SHUTDOWN', label: '系统关闭/重启', defaultReason: '服务关闭/重启' },
  { value: 'OTHER', label: '其他原因', defaultReason: '其他原因' }
] as const

// Day 9.1: 取消当前活跃 ETL 任务
//   Day 9.4: 用 ElMessageBox.prompt 让用户输入取消原因, 写到 etl_progress_log.cancel_reason
//   Day 9.6: 改用枚举下拉 + 默认描述 (与后端 AllowedReasonCodes 对齐, 减少误分类)
async function doCancel() {
  let reasonCode: string = 'USER_REQUEST'
  let reason: string = reasonCodeOptions[0].defaultReason
  try {
    // 步骤 1: 选择 reason_code 枚举 (Element Plus ElMessageBox 自定义 HTML)
    await ElMessageBox({
      title: '取消 ETL 任务',
      message: `
        <div style="text-align:left;font-size:13px;line-height:1.6">
          <div style="margin-bottom:8px;color:#606266">请选择取消原因 (会写入历史审计, 按此码聚合):</div>
          <div id="cancel-reason-list" style="display:flex;flex-direction:column;gap:6px">
            ${reasonCodeOptions.map((o, i) => `
              <label style="display:flex;align-items:flex-start;gap:6px;cursor:pointer;padding:6px 8px;border:1px solid #ebeef5;border-radius:4px;${i === 0 ? 'background:#ecf5ff;border-color:#409eff' : ''}">
                <input type="radio" name="cancel-reason-code" value="${o.value}" ${i === 0 ? 'checked' : ''} style="margin-top:3px" />
                <div>
                  <div style="font-weight:600">${o.label}</div>
                  <div style="color:#909399;font-size:12px">${o.value} — ${o.defaultReason}</div>
                </div>
              </label>
            `).join('')}
          </div>
        </div>
      `,
      showCancelButton: true,
      confirmButtonText: '下一步',
      cancelButtonText: '不取消',
      type: 'warning',
      dangerouslyUseHTMLString: true
    })
    // 提取选中的 code
    const checked = document.querySelector<HTMLInputElement>('input[name="cancel-reason-code"]:checked')
    reasonCode = checked?.value || 'USER_REQUEST'
    const picked = reasonCodeOptions.find(o => o.value === reasonCode) || reasonCodeOptions[0]
    reason = picked.defaultReason
  } catch {
    return
  }
  // 步骤 2: 询问是否填写更详细的描述 (可选, 留空则用默认)
  try {
    const r = await ElMessageBox.prompt('可补充详细描述 (留空用默认)', '取消原因说明', {
      confirmButtonText: '确认取消',
      cancelButtonText: '不取消',
      inputPlaceholder: reason,
      inputValue: reason,
      inputValidator: undefined
    })
    const v = (r.value || '').trim()
    if (v) reason = v
  } catch {
    return
  }
  cancelling.value = true
  try {
    const r = await etlApi.cancel(reason, reasonCode)
    if (r.cancelled) {
      ElMessage.warning(`已发送取消信号 (码: ${reasonCode}), 任务即将终止`)
    } else {
      ElMessage.info(r.reason || '无活跃任务可取消')
    }
  } catch (e: any) {
    // 已被拦截器处理
  } finally {
    cancelling.value = false
  }
}

// ===== 计算 =====
const status = computed(() => task.value.activeTask?.status ?? (task.value.inProgress ? 'running' : 'idle'))
const stage = computed(() => task.value.activeTask?.stage ?? '-')
const progressPct = computed(() => task.value.activeTask?.progressPct ?? null)
// P1.1 (Task 3): 是否有 paused 状态的 ETL (前端根据此显示"恢复"按钮)
const hasPausedTask = ref(false)
async function checkPausedTask() {
  try {
    const hist = await etlApi.history(20, 'paused')
    hasPausedTask.value = (hist?.items?.length ?? 0) > 0
  } catch (e) { hasPausedTask.value = false }
}
onMounted(() => { checkPausedTask() })

// P1.1 (Task 3): 暂停当前活跃 ETL 任务
//   与 Cancel 区别: Cancel 走 cts.Cancel() 抛异常, Pause 走 flag 标记, 当前批次跑完后优雅退出
async function doPause() {
  try {
    await ElMessageBox.confirm(
      '暂停当前 ETL 任务?\n\n当前批次跑完后会优雅退出, checkpoint_id 会写入 etl_progress_log, 后续可用"恢复"按钮从该点续读.\n\n(区别于"取消" — 取消会立即终止并回滚当前批次)',
      '暂停 ETL 任务',
      { type: 'warning', confirmButtonText: '暂停', cancelButtonText: '不暂停' }
    )
  } catch { return }
  pausing.value = true
  try {
    const r = await etlApi.pause()
    if (r.paused) {
      ElMessage.warning(`已发送暂停信号, checkpoint_id=${r.checkpointId ?? '?'}, 当前批次跑完后退出`)
    } else {
      ElMessage.info(r.reason || '无活跃任务可暂停')
    }
  } catch (e: any) {
    // 已被拦截器处理
  } finally {
    pausing.value = false
  }
}

// P1.1 (Task 3): 恢复暂停的 ETL 任务 — 从最近 paused 记录的 checkpoint_id 续读
async function doResume() {
  try {
    await ElMessageBox.confirm(
      '恢复暂停的 ETL 任务?\n\n将从最近一条 paused 记录的 checkpoint_id+1 行开始续读, 跳过已 COMMIT 的批次.',
      '恢复 ETL 任务',
      { type: 'info', confirmButtonText: '恢复', cancelButtonText: '不恢复' }
    )
  } catch { return }
  resuming.value = true
  try {
    const r = await etlApi.resume()
    if (r.resumed) {
      ElMessage.success(`已触发 Resume: entity=${r.entity} checkpoint=${r.checkpointId} (从第 ${r.nextLineNo} 行开始)`)
      hasPausedTask.value = false  // Resume 已触发新的 ETL, paused 记录应已不算最新
    } else {
      ElMessage.warning(r.error || '恢复失败')
    }
  } catch (e: any) {
    // 已被拦截器处理
  } finally {
    resuming.value = false
  }
}

function fmt(n?: number) {
  if (n === undefined || n === null) return '-'
  return n.toLocaleString()
}

function fmtBytes(n?: number) {
  if (!n) return '-'
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(2)} MB`
}

function statusTagType(s: string): 'success' | 'warning' | 'info' | 'danger' | 'primary' {
  if (s === 'running') return 'primary'
  if (s === 'completed') return 'success'
  if (s === 'failed') return 'danger'
  if (s === 'idle') return 'info'
  return 'warning'
}

function stageLabel(s: string) {
  return (
    {
      staging: 'COPY 暂存',
      insert: 'INSERT 写库',
      commit: 'COMMIT 提交',
      meili: 'Meili 同步',
      done: '完成'
    } as Record<string, string>
  )[s] ?? s
}

// Day 9.1: 格式化 JSON 样本 (尝试 JSON.parse 失败则原样返回)
function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}
</script>

<template>
  <div class="p-3 max-w-screen-2xl mx-auto">
    <!-- 顶部：触发表单 -->
    <el-card shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">手动 ETL 触发</span>
          <el-tag size="small" type="info">Day 8.4</el-tag>
        </div>
      </template>

      <el-form :inline="false" label-width="100px" size="default">
        <el-form-item label="实体">
          <el-radio-group v-model="form.entity" @change="changeEntity">
            <el-radio-button value="products">products</el-radio-button>
            <el-radio-button value="xrefs">xrefs</el-radio-button>
            <el-radio-button value="apps">apps</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <el-form-item label="模式">
          <el-radio-group v-model="form.mode">
            <el-radio-button value="full-load">full-load (TRUNCATE+INSERT)</el-radio-button>
            <el-radio-button value="insert-only">insert-only (ON CONFLICT DO NOTHING)</el-radio-button>
            <el-radio-button value="upsert">upsert (ON CONFLICT DO UPDATE)</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <el-form-item label="文件路径">
          <el-input
            v-model="form.jsonlPath"
            placeholder="JSONL 绝对路径"
            style="width: 500px"
            clearable
          />
        </el-form-item>

        <el-form-item label=" ">
          <div class="flex items-center gap-3">
            <el-checkbox v-model="form.dryRun">dry-run (仅校验文件)</el-checkbox>
            <!-- Day 11 Phase 1: cascade 安全锁, 仅 products + full-load 显示 -->
            <el-tooltip
              v-if="form.entity === 'products' && form.mode === 'full-load' && !form.dryRun"
              content="开启: TRUNCATE 同时清空 xrefs/apps (首次全量场景); 关闭: 仅清 products, 保留关联表 (单独刷新主表)"
              placement="top"
            >
              <el-checkbox v-model="form.cascade">cascade (清空关联表)</el-checkbox>
            </el-tooltip>
            <el-button
              type="primary"
              :loading="submitting"
              :disabled="status === 'running'"
              @click="doTrigger"
            >
              {{ form.dryRun ? '执行 dry-run' : '立即导入' }}
            </el-button>
            <el-button
              v-if="status === 'running'"
              type="warning"
              :loading="pausing"
              @click="doPause"
            >
              暂停任务
            </el-button>
            <el-button
              v-if="status === 'running'"
              type="danger"
              :loading="cancelling"
              @click="doCancel"
            >
              取消任务
            </el-button>
            <el-button
              v-if="status !== 'running' && hasPausedTask"
              type="success"
              :loading="resuming"
              @click="doResume"
            >
              恢复暂停的任务
            </el-button>
          </div>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 实时进度 -->
    <el-card shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">实时进度</span>
          <el-tag :type="statusTagType(status)" size="small">{{ status }}</el-tag>
          <el-tag v-if="status === 'running'" size="small" type="info">stage: {{ stageLabel(stage) }}</el-tag>
        </div>
      </template>

      <div v-if="!task.activeTask" class="text-[var(--color-text-muted)] text-sm">
        当前无活跃任务. 等待触发或查看历史任务.
      </div>

      <div v-else>
        <!-- 进度条 -->
        <div class="mb-3">
          <el-progress
            v-if="progressPct !== null"
            :percentage="progressPct"
            :stroke-width="14"
            :text-inside="true"
            :status="status === 'failed' ? 'exception' : status === 'completed' ? 'success' : undefined"
          />
          <el-progress v-else :percentage="0" :stroke-width="14" :indeterminate="true" />
          <div class="text-xs text-[var(--color-text-muted)] mt-1">
            rows: {{ fmt(task.activeTask.rowsProcessed) }} /
            {{ task.activeTask.rowsTotal !== null ? fmt(task.activeTask.rowsTotal) : '?' }}
            · elapsed: {{ task.activeTask.elapsedSec ?? 0 }}s
            · started: {{ task.activeTask.startedAt ?? '-' }}
          </div>
        </div>

        <!-- 实时计数 -->
        <el-descriptions :column="4" size="small" border>
          <el-descriptions-item label="read">{{ fmt(task.activeTask.read) }}</el-descriptions-item>
          <el-descriptions-item label="inserted">{{ fmt(task.activeTask.inserted) }}</el-descriptions-item>
          <el-descriptions-item label="updated">{{ fmt(task.activeTask.updated) }}</el-descriptions-item>
          <el-descriptions-item label="skipped">{{ fmt(task.activeTask.skipped) }}</el-descriptions-item>
          <el-descriptions-item label="errors">{{ fmt(task.activeTask.errors) }}</el-descriptions-item>
          <el-descriptions-item label="indexed">{{ fmt(task.activeTask.indexed) }}</el-descriptions-item>
          <el-descriptions-item label="indexPending">{{ fmt(task.activeTask.indexPending) }}</el-descriptions-item>
          <el-descriptions-item label="currentFile">
            <el-tooltip :content="task.activeTask.currentFile ?? ''" placement="top" v-if="task.activeTask.currentFile">
              <span class="text-xs">{{ task.activeTask.currentFile.length > 40 ? task.activeTask.currentFile.slice(0, 40) + '...' : task.activeTask.currentFile }}</span>
            </el-tooltip>
            <span v-else>-</span>
          </el-descriptions-item>
        </el-descriptions>

        <!-- 错误信息 -->
        <div v-if="task.activeTask.lastError" class="mt-3">
          <el-alert :title="task.activeTask.lastError" type="error" :closable="false" />
        </div>
      </div>
    </el-card>

    <!-- dry-run 结果 (Day 9.1: 含前 5 行样本) -->
    <el-card v-if="lastDryRun" shadow="never" class="mb-3">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">最近 dry-run 校验</span>
          <el-tag size="small" type="info">{{ lastDryRun.mode }}</el-tag>
          <el-tag v-if="lastDryRun.samples && lastDryRun.samples.length > 0" size="small" type="success">
            样本 {{ lastDryRun.samples.length }} 行
          </el-tag>
        </div>
      </template>
      <el-descriptions :column="2" size="small" border>
        <el-descriptions-item label="文件">{{ lastDryRun.file }}</el-descriptions-item>
        <el-descriptions-item label="大小">{{ fmtBytes(lastDryRun.sizeBytes) }}</el-descriptions-item>
        <el-descriptions-item label="行数">{{ fmt(lastDryRun.lines) }}</el-descriptions-item>
        <el-descriptions-item label="模式">{{ lastDryRun.mode }}</el-descriptions-item>
      </el-descriptions>

      <div v-if="lastDryRun.samples && lastDryRun.samples.length > 0" class="mt-3">
        <div class="text-sm font-semibold mb-1">样本预览 (前 {{ lastDryRun.samples?.length || 0 }} 行 JSON)</div>
        <el-table :data="(showAllSamples ? lastDryRun.samples : lastDryRun.samples.slice(0, 10)).map((s, i) => ({ idx: i + 1, raw: s }))" size="small" border max-height="320">
          <el-table-column prop="idx" label="#" width="50" />
          <el-table-column label="原始 JSON">
            <template #default="{ row }">
              <pre class="text-xs whitespace-pre-wrap break-all m-0">{{ prettyJson(row.raw) }}</pre>
            </template>
          </el-table-column>
        </el-table>
        <div class="mt-2 flex justify-end">
          <el-button text size="small" @click="showAllSamples = !showAllSamples">
            {{ showAllSamples ? "收起 (只显示前 10 行)" : "展开全部 " + (lastDryRun.samples?.length || 0) + " 行" }}
          </el-button>
        </div>
      </div>
    </el-card>

    <!-- 最近完成 -->
    <el-card v-if="lastFinished" shadow="never">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">最近一次完成结果</span>
          <el-tag :type="statusTagType(lastFinished.status)" size="small">{{ lastFinished.status }}</el-tag>
          <div class="flex-1" />
          <el-button size="small" text @click="clearLastFinished">清除</el-button>
        </div>
      </template>
      <el-descriptions :column="4" size="small" border>
        <el-descriptions-item label="read">{{ fmt(lastFinished.read) }}</el-descriptions-item>
        <el-descriptions-item label="inserted">{{ fmt(lastFinished.inserted) }}</el-descriptions-item>
        <el-descriptions-item label="updated">{{ fmt(lastFinished.updated) }}</el-descriptions-item>
        <el-descriptions-item label="skipped">{{ fmt(lastFinished.skipped) }}</el-descriptions-item>
        <el-descriptions-item label="skipped_missing_oem">{{ fmt(lastFinished.skippedMissingOem) }}</el-descriptions-item>
        <el-descriptions-item label="skipped_null_field">{{ fmt(lastFinished.skippedNullField) }}</el-descriptions-item>
        <el-descriptions-item label="skipped_duplicate">{{ fmt(lastFinished.skippedDuplicate) }}</el-descriptions-item>
        <el-descriptions-item label="errors">{{ fmt(lastFinished.errors) }}</el-descriptions-item>
        <el-descriptions-item label="indexed">{{ fmt(lastFinished.indexed) }}</el-descriptions-item>
        <el-descriptions-item label="indexPending">{{ fmt(lastFinished.indexPending) }}</el-descriptions-item>
        <el-descriptions-item label="elapsed">{{ lastFinished.elapsedSec ?? 0 }}s</el-descriptions-item>
        <el-descriptions-item label="finishedAt">{{ lastFinished.finishedAt ?? '-' }}</el-descriptions-item>
      </el-descriptions>

      <div v-if="lastFinished.lastError" class="mt-3">
        <el-alert :title="lastFinished.lastError" type="error" :closable="false" />
      </div>

      <div v-if="lastFinished.recentErrors && lastFinished.recentErrors.length > 0" class="mt-3">
        <div class="text-sm font-semibold mb-1">最近错误 (最多 10 条)</div>
        <el-table :data="lastFinished.recentErrors" size="small" max-height="240" border>
          <el-table-column prop="at" label="时间" width="200" />
          <el-table-column prop="message" label="错误" show-overflow-tooltip />
        </el-table>
      </div>
    </el-card>

    <!-- Day 9.8: ETL 取消审计 — reason_code 饼图 + 历史列表 -->
    <el-card shadow="never">
      <template #header>
        <div class="flex items-center gap-2">
          <span class="font-semibold">取消审计 (按 reason_code 聚合)</span>
          <el-tag size="small" type="info">运营可观察</el-tag>
          <div class="flex-1" />
          <el-button size="small" :loading="historyLoading" @click="refreshAudit">刷新</el-button>
        </div>
      </template>
      <div class="audit-grid">
        <!-- 饼图 -->
        <div class="audit-pie">
          <EtlReasonCodePie :data="reasonCodeAgg" />
        </div>
        <!-- 历史表 -->
        <div class="audit-table">
          <div class="text-xs text-[var(--color-text-muted)] mb-2">最近 20 条 cancelled 记录</div>
          <el-table :data="historyItems" size="small" max-height="380" border stripe>
            <el-table-column prop="id" label="#" width="60" />
            <el-table-column prop="entityType" label="entity" width="90" />
            <el-table-column prop="mode" label="mode" width="100" />
            <el-table-column label="reasonCode" width="160">
              <template #default="{ row }">
                <el-tag v-if="row.reasonCode" size="small" type="info">{{ row.reasonCode }}</el-tag>
                <span v-else class="text-gray-400 text-xs">LEGACY</span>
              </template>
            </el-table-column>
            <el-table-column prop="cancelReason" label="原因" show-overflow-tooltip min-width="180" />
            <el-table-column label="已读/插/改" width="120">
              <template #default="{ row }">
                <span class="text-xs">
                  {{ row.readCount }} / {{ row.insertedCount }} / {{ row.updatedCount }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="耗时" width="80">
              <template #default="{ row }">
                <span class="text-xs">{{ row.durationSec.toFixed(1) }}s</span>
              </template>
            </el-table-column>
            <el-table-column label="取消时间" width="170">
              <template #default="{ row }">
                <span class="text-xs text-[var(--color-text-muted)]">{{ (row.cancelledAt || row.finishedAt).slice(0, 19) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.audit-grid {
  display: grid;
  grid-template-columns: minmax(280px, 380px) 1fr;
  gap: 24px;
  align-items: flex-start;
}
.audit-pie {
  padding: 8px 0;
}
.audit-table {
  min-width: 0;  /* 防止 grid item 内容溢出 */
}
@media (max-width: 1024px) {
  .audit-grid {
    grid-template-columns: 1fr;
  }
}
</style>
