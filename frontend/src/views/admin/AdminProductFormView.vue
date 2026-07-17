<script setup lang="ts">
// Day 9: 后台产品表单 (新增/编辑)
//   - 7 分区布局 (与 ProductFormDto 一致)
//   - 行内保存
// Day 10: 分区 2 oemBrand 改为 el-autocomplete, 从 dictApi.oemBrands.typeahead 自动补全 (P1.3)
// Day 10+ P2.2: 7 分区全部接入 typeahead (productName1/2/type/oemNo3/media/machine/engine)
// Day 10+ P5.1: 包装尺寸 L/W/H → Volume 自动计算 (m³), 母箱同理
// Day 10+ P5.2: 尺寸/性能字段后挂 ? 图标, 鼠标悬停显示字段说明 (FieldHelpPopover)
import { ref, reactive, onMounted, computed, watch, onBeforeUnmount, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { adminProductApi, imageApi, dictApi } from '@/api'
import type { ProductDetail } from '@/api/types'
import FieldHelpPopover from '@/components/FieldHelpPopover.vue'  // P5.2

const { t } = useI18n()

const route = useRoute()
const router = useRouter()

const isEdit = computed(() => !!route.params.id)
const productId = computed(() => (route.params.id ? Number(route.params.id) : 0))
const activeNames = ref(['1', '3', '5', '6'])

// P2-7 修复 v2: 保存乐观锁冲突后的 reload timer 引用, 卸载时清理
//   WHY: 用户在 1.5s 内导航离开后, 旧 timer 仍会触发 window.location.reload() 造成意外跳转
let reloadTimer: ReturnType<typeof setTimeout> | null = null

const form = reactive<any>({
  productName1: '',
  productName2: '',
  type: '',
  mr1: '',
  oem2: '',
  isPublished: true,
  remark: '',
  d1Mm: null,
  d2Mm: null,
  d3Mm: null,
  d4Mm: null,
  h1Mm: null,
  h2Mm: null,
  h3Mm: null,
  h4Mm: null,
  d7Thread: '',
  d8Thread: '',
  noCheckValves: null,
  noBypassValves: null,
  media: '',
  mediaModel: '',
  bypassValveLr: null,
  bypassValveHr: null,
  efficiency1: '',
  efficiency2: '',
  bypassPressure: null,
  collapsePressureBar: null,
  sealingMaterial: '',
  tempRange: '',
  qtyPerCarton: null,
  weightKgs: null,
  cartonLengthMm: null,
  cartonWidthMm: null,
  cartonHeightMm: null,
  volumePerCartonM3: null,  // P5.1 自动计算, 也可显式覆盖
  masterBoxQty: null,
  masterBoxWeightKgs: null,
  masterBoxLengthMm: null,
  masterBoxWidthMm: null,
  masterBoxHeightMm: null,
  crossReferences: [],
  machineApplications: []
})

// E2E BD.3 修复 v2: 乐观锁并发令牌 (PG xmin), GET 时保存, PUT 时带回
//   后端用此值覆盖实体加载时的 xmin, 检测"先读后写"并发丢失更新
const rowVersion = ref<number>(0)

// V2 Task 1.1: MR.1 前端校验规则 (与后端 AdminProductService.ValidateForm 对齐)
//   - required: 后端 MR1_REQUIRED 抛 ArgumentException, 前端同步拦截避免 400 往返
//   - pattern: 后端正则 ^[A-Za-z0-9]{1,10}$, 前端同口径
//   - max 10: maxlength 属性已限制, rules 兜底防止粘贴超长
const mr1Rules = [
  { required: true, message: 'MR.1 必填', trigger: 'blur' },
  { pattern: /^[A-Za-z0-9]{1,10}$/, message: 'MR.1 必须为 1-10 位字母+数字', trigger: 'blur' }
]

// V2 Task 1.1: el-form ref, save 前调 validate() 触发 rules 校验
const formRef = ref()

const loading = ref(false)
const saving = ref(false)
const uploading = ref(false)
const removing = ref(false)

// 改进 3.1: 图片上传进度条 state
//   uploadingSlot 标识当前上传位置 ('primary' | `detail-${slot}`), 用于 UI 定位进度条
//   uploadProgress 0-100 整数, 上传开始前置 0, 完成/失败后由 finally 重置
//   WHY 单一槽位: uploading.value 互斥锁保证同时仅一个上传, 无需多 slot 并发进度
const uploadingSlot = ref<string>('')
const uploadProgress = ref<number>(0)

// V2 Task 3.3.1: 图片分层 (主图按 OEM 3 + 详情图按 MR.1 共享)
//   images[0] 为主图 (slot=1, imageRole=primary)
//   images[1..5] 为详情图 (slot=2-6, imageRole=detail)
//   WHY 复用数组: 旧 UI 6 slot 网格布局保留, 仅语义分层
const images = ref<Record<number, { slot: number; imageKey: string; imageUrl: string; oemNo3?: string | null; imageRole?: string }>>({})
// V2: 主图上传时选择的 OEM 3 (从 form.crossReferences 已保存的 oemList 中选)
const selectedOemNo3ForPrimary = ref<string>('')

// 改进 3.2: 主图 OEM 3 切换拦截标志, 避免 watch 在回退赋值时递归触发
//   WHY: 用户取消切换时需回退 selectedOemNo3ForPrimary.value = oldVal, 该赋值会再次触发 watch
//   通过该 flag 跳过回退赋值的二次拦截, 防止"回退→触发 watch→再次判断→回退"死循环
let primaryOemNo3Changing = false

// 改进 3.2: 主图 OEM 3 切换提示 (已有主图且新 OEM 3 ≠ 当前主图 OEM 3 时弹 confirm)
//   场景: 用户已为主图 slot=1 上传了关联 OEM 3="A" 的主图, 此时切到 "B" 会导致:
//     - 旧主图仍以 "A" 命名存储在 MinIO, 不会自动删除
//     - 重新上传会以 "B" 命名写入新主图, 与旧主图并存造成存储冗余
//   解决: 切换前 confirm 让用户知晓影响, 取消则回退, 确认则提示后续需重新上传
watch(selectedOemNo3ForPrimary, async (newVal, oldVal) => {
  // 改进 3.2: 回退赋值时跳过, 避免递归
  if (primaryOemNo3Changing) return
  // 跳过初次赋值 (load 时从空 → 默认值) 或值未变化
  if (newVal === oldVal) return
  // 跳过从空到非空的首次设置 (load 场景)
  if (!oldVal) return
  const existingPrimary = images.value[1]
  // 已有主图且切换后的 OEM 3 与现有主图 OEM 3 不同 → 提示
  if (existingPrimary && existingPrimary.oemNo3 && existingPrimary.oemNo3 !== newVal) {
    try {
      await ElMessageBox.confirm(
        `切换 OEM 3 将导致现有主图 (关联 OEM 3: ${existingPrimary.oemNo3}) 不再匹配。\n` +
        `旧主图不会自动删除, 重新上传会以新 OEM 3 命名写入新文件。\n` +
        `是否继续切换?`,
        '切换主图 OEM 3',
        { confirmButtonText: '继续切换', cancelButtonText: '取消', type: 'warning' }
      )
      // 用户确认: 保留新选择, 提示需重新上传
      ElMessage.info('已切换 OEM 3, 请重新上传主图以匹配新关联')
    } catch {
      // 用户取消: 回退到旧值 (用 flag 跳过递归 watch)
      primaryOemNo3Changing = true
      selectedOemNo3ForPrimary.value = oldVal
      // nextTick 后解除 flag, 确保 watch 已完成触发周期
      await nextTick()
      primaryOemNo3Changing = false
    }
  }
})

// ===== P5.1: 包装/母箱体积自动计算 =====
//   L * W * H / 1e9 m³ (mm → m → m³)
//   任一为空 → null (不显示)
//   后端 AdminProductService.DeriveVolume 是兜底, 这里只做实时预览
function computeVolumeM3(l: number | null | undefined, w: number | null | undefined, h: number | null | undefined): number | null {
  if (l == null || w == null || h == null) return null
  if (l <= 0 || w <= 0 || h <= 0) return null
  // 精度保留 6 位小数, 后端 decimal(18,6) 一致
  return Math.round((l * w * h) / 1_000_000) / 1_000_000
}

const cartonVolume = computed(() => computeVolumeM3(form.cartonLengthMm, form.cartonWidthMm, form.cartonHeightMm))
const masterBoxVolume = computed(() => computeVolumeM3(form.masterBoxLengthMm, form.masterBoxWidthMm, form.masterBoxHeightMm))

const cartonVolumeText = computed(() => cartonVolume.value == null ? '' : cartonVolume.value.toFixed(6))
const masterBoxVolumeText = computed(() => masterBoxVolume.value == null ? '' : masterBoxVolume.value.toFixed(6))

// 监听 L/W/H 变化, 同步到 form.volumePerCartonM3 (避免只读 el-input 提交时空字段)
watch(cartonVolume, (v) => {
  form.volumePerCartonM3 = v == null ? null : v
})

async function load() {
  if (!isEdit.value) return
  loading.value = true
  try {
    const p = await adminProductApi.get(productId.value)
    Object.assign(form, {
      productName1: p.productName1,
      productName2: p.productName2,
      type: p.type,
      mr1: p.mr1,
      oem2: p.oem2,
      isPublished: p.isPublished,
      remark: p.remark,
      d1Mm: p.d1Mm, d2Mm: p.d2Mm, d3Mm: p.d3Mm, d4Mm: p.d4Mm,
      h1Mm: p.h1Mm, h2Mm: p.h2Mm, h3Mm: p.h3Mm, h4Mm: p.h4Mm,
      d7Thread: p.d7Thread, d8Thread: p.d8Thread,
      noCheckValves: p.noCheckValves, noBypassValves: p.noBypassValves,
      media: p.media, mediaModel: p.mediaModel,
      bypassValveLr: p.bypassValveLr, bypassValveHr: p.bypassValveHr,
      efficiency1: p.efficiency1, efficiency2: p.efficiency2,
      bypassPressure: p.bypassPressure, collapsePressureBar: p.collapsePressureBar,
      sealingMaterial: p.sealingMaterial, tempRange: p.tempRange,
      qtyPerCarton: p.qtyPerCarton, weightKgs: p.weightKgs,
      cartonLengthMm: p.cartonLengthMm, cartonWidthMm: p.cartonWidthMm, cartonHeightMm: p.cartonHeightMm,
      masterBoxQty: p.masterBoxQty, masterBoxWeightKgs: p.masterBoxWeightKgs,
      masterBoxLengthMm: p.masterBoxLengthMm, masterBoxWidthMm: p.masterBoxWidthMm, masterBoxHeightMm: p.masterBoxHeightMm
    })
    form.crossReferences = p.crossReferences.map((x) => ({ ...x }))
    form.machineApplications = p.machineApplications.map((m) => ({ ...m }))
    // V2 Task 3.3.1: images 改为 Record<slot, img> 结构 (主图 slot=1, 详情图 slot=2-6)
    images.value = {}
    if (Array.isArray(p.images)) {
      for (const img of p.images) {
        if (img && img.slot) {
          images.value[img.slot] = {
            slot: img.slot,
            imageKey: img.imageKey,
            imageUrl: img.imageUrl,
            oemNo3: (img as any).oemNo3 ?? null,
            imageRole: (img as any).imageRole ?? (img.slot === 1 ? 'primary' : 'detail')
          }
        }
      }
    }
    // V2: 默认选中第一个上架 OEM 3 作为主图关联
    selectedOemNo3ForPrimary.value = form.crossReferences.find((x: any) => x.oemNo3)?.oemNo3 || ''
    // E2E BD.3 修复 v2: 保存 GET 时的 RowVersion (xmin), PUT 时带回实现乐观锁
    rowVersion.value = p.rowVersion ?? 0
  } catch (e: any) {} finally {
    loading.value = false
  }
}

async function save() {
  // V2 Task 1.1: 提交前触发 el-form validate, 同步拦截 MR.1 必填/格式错误
  //   WHY: rules trigger='blur' 在用户未离开输入框直接点保存时不触发, 需显式 validate
  if (formRef.value) {
    try {
      await formRef.value.validate()
    } catch {
      ElMessage.error('表单校验未通过, 请检查 MR.1 等必填字段')
      return
    }
  }
  saving.value = true
  try {
    if (isEdit.value) {
      // E2E BD.3 修复 v2: 带回 GET 时的 RowVersion, 后端用此值检测并发冲突
      await adminProductApi.update(productId.value, { ...form, rowVersion: rowVersion.value }, 'admin')
      ElMessage.success(t('admin.productformview.success.saved'))
    } else {
      await adminProductApi.create(form, 'admin')
      ElMessage.success(t('admin.productformview.success.created'))
    }
    router.push('/admin/products')
  } catch (e: any) {
    // P0-1.3: 识别 409 (产品已存在), 给出用户友好提示
    //   后端并发场景下 AnyAsync 检查与 SaveChangesAsync 之间有 TOCTOU 窗口,
    //   第二个请求触发 23505 唯一约束冲突, 端点映射为 409 Conflict
    //   拦截器已展示后端原始 title/detail, 这里补充更友好的行动指引
    if (e?.response?.status === 409 || e?.problem?.status === 409) {
      // E2E BD.3 修复: 区分两种 409 — OEM 重复 vs 乐观锁冲突 (数据已被他人修改)
      //   后端 ProblemDetails.title 区分: "产品已存在" / "数据已被修改"
      const title = e?.response?.data?.title || e?.problem?.title || ''
      const detail = e?.response?.data?.detail || e?.problem?.detail || ''
      if (title.includes(t('admin.productformview.string.by_modify')) || detail.includes(t('admin.productformview.string.by_user_modify')) || detail.includes('lost update')) {
        ElMessage.error(t('admin.productformview.error.data_has_been_modified_by'))
        // 自动重新加载最新数据, 避免用户手动刷新
        // P2-7 修复 v2: 保存 timer 引用, 卸载时清理, 避免用户导航离开后意外 reload
        if (reloadTimer !== null) clearTimeout(reloadTimer)
        reloadTimer = setTimeout(() => window.location.reload(), 1500)
      } else {
        ElMessage.error(t('admin.productformview.error.product_already_exists_please'))
      }
    }
  } finally {
    saving.value = false
  }
}

function addXref() {
  form.crossReferences.push({ oemBrand: '', oemNo3: '' })
}
function removeXref(idx: number) {
  form.crossReferences.splice(idx, 1)
}
function addApp() {
  form.machineApplications.push({ machineBrand: '', machineModel: '' })
}
function removeApp(idx: number) {
  form.machineApplications.splice(idx, 1)
}

// Day 10: P1.3 OEM 品牌 typeahead
//   el-autocomplete 调用约定: 返回 Promise<Array<{value, [任意字段]}>>
//   - 我们用 { brand } 作为 value, 因为 v-model 直接绑 oemBrand 字符串
//   - 后端 typeahead 已按 sort_order 排, 取前 20 条
//   - 字典为空时返回 [], el-autocomplete 自动降级为自由输入
async function queryOemBrands(q: string, cb: (items: { value: string; brand: string }[]) => void) {
  try {
    const { items } = await dictApi.oemBrands.typeahead(q || '', 20)
    // 转成 el-autocomplete 期望的 { value } 格式
    const mapped = items.map((it) => ({ value: it.brand, brand: it.brand }))
    cb(mapped)
  } catch (e) {
    cb([])
  }
}

// Day 10+ P2.2: 7 分区 typeahead (复用 queryOemBrands 模板, 适配单/多字段字典)
//   - 单字段字典: ProductName1, ProductName2, Type, OemNo3
//   - 多字段字典: Media (2 字段), Machine (3 字段), Engine (2 字段)
async function queryProductName1(q: string, cb: (items: { value: string }[]) => void) {
  try {
    const { items } = await dictApi.productName1s.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.productName1 })))
  } catch { cb([]) }
}
async function queryProductName2(q: string, cb: (items: { value: string }[]) => void) {
  try {
    const { items } = await dictApi.productName2s.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.productName2 })))
  } catch { cb([]) }
}
async function queryType(q: string, cb: (items: { value: string }[]) => void) {
  try {
    const { items } = await dictApi.types.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.type })))
  } catch { cb([]) }
}
async function queryOemNo3(q: string, cb: (items: { value: string }[]) => void) {
  try {
    const { items } = await dictApi.oemNo3s.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.oemNo3 })))
  } catch { cb([]) }
}
// Media: 显示 "name (model)" 让用户看清完整项
async function queryMedia(q: string, cb: (items: { value: string; mediaName: string; mediaModel: string | null }[]) => void) {
  try {
    const { items } = await dictApi.medias.typeahead(q || '', 20)
    cb(items.map((it) => ({
      value: it.mediaModel ? `${it.mediaName} (${it.mediaModel})` : it.mediaName,
      mediaName: it.mediaName,
      mediaModel: it.mediaModel
    })))
  } catch { cb([]) }
}
// Machine: 显示 "brand / model / name"
async function queryMachineBrand(q: string, cb: (items: { value: string; machineBrand: string }[]) => void) {
  try {
    const { items } = await dictApi.machines.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.machineBrand, machineBrand: it.machineBrand })))
  } catch { cb([]) }
}
async function queryMachineModel(q: string, cb: (items: { value: string; machineModel: string | null }[]) => void) {
  try {
    const { items } = await dictApi.machines.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.machineModel || '', machineModel: it.machineModel })))
  } catch { cb([]) }
}
async function queryEngineBrand(q: string, cb: (items: { value: string; engineBrand: string }[]) => void) {
  try {
    const { items } = await dictApi.engines.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.engineBrand, engineBrand: it.engineBrand })))
  } catch { cb([]) }
}
async function queryEngineType(q: string, cb: (items: { value: string; engineType: string | null }[]) => void) {
  try {
    const { items } = await dictApi.engines.typeahead(q || '', 20)
    cb(items.map((it) => ({ value: it.engineType || '', engineType: it.engineType })))
  } catch { cb([]) }
}

// V2 Task 3.3.2: 上传主图 (slot=1, 需选 OEM 3)
async function uploadPrimaryImage(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  if (!isEdit.value) {
    ElMessage.warning('请先保存产品后再上传图片')
    return
  }
  if (!form.mr1) {
    ElMessage.error('MR.1 缺失, 无法上传图片')
    return
  }
  if (!selectedOemNo3ForPrimary.value) {
    ElMessage.error('请先选择主图关联的 OEM 3')
    return
  }
  if (uploading.value) return
  uploading.value = true
  // 改进 3.1: 进度条 state 初始化
  uploadingSlot.value = 'primary'
  uploadProgress.value = 0
  try {
    const r = await imageApi.uploadPrimary(form.mr1, selectedOemNo3ForPrimary.value, file, (p) => {
      uploadProgress.value = p
    })
    ElMessage.success('主图上传成功')
    images.value[1] = { slot: 1, imageKey: r.imageKey, imageUrl: r.imageUrl, oemNo3: r.oemNo3, imageRole: 'primary' }
  } catch {
    // 已被拦截器
  } finally {
    uploading.value = false
    uploadingSlot.value = ''
    uploadProgress.value = 0
  }
  input.value = ''
}

// V2 Task 3.3.2: 上传详情图 (slot 2-6, 按 MR.1 命名)
async function uploadDetailImage(slot: number, e: Event) {
  // V2 Task 3.3.2: 前端校验 slot 必须 2-6
  if (slot < 2 || slot > 6 || !Number.isInteger(slot)) {
    ElMessage.error(`详情图 slot 必须在 2-6 之间 (当前 ${slot})`)
    return
  }
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  if (!isEdit.value) {
    ElMessage.warning('请先保存产品后再上传图片')
    return
  }
  if (!form.mr1) {
    ElMessage.error('MR.1 缺失, 无法上传图片')
    return
  }
  if (uploading.value) return
  uploading.value = true
  // 改进 3.1: 进度条 state 初始化
  uploadingSlot.value = `detail-${slot}`
  uploadProgress.value = 0
  try {
    const r = await imageApi.uploadDetail(form.mr1, slot, file, (p) => {
      uploadProgress.value = p
    })
    ElMessage.success(`详情图 slot ${slot} 上传成功`)
    images.value[slot] = { slot, imageKey: r.imageKey, imageUrl: r.imageUrl, oemNo3: null, imageRole: 'detail' }
  } catch {
    // 已被拦截器
  } finally {
    uploading.value = false
    uploadingSlot.value = ''
    uploadProgress.value = 0
  }
  input.value = ''
}

// V2: 删除主图
async function removePrimaryImage() {
  if (removing.value) return
  if (!form.mr1) return
  removing.value = true
  try {
    await imageApi.remove(form.mr1, 'primary', 1)
    delete images.value[1]
    ElMessage.success('主图已删除')
  } catch {
    // 已被拦截器
  } finally {
    removing.value = false
  }
}

// V2: 删除详情图
async function removeDetailImage(slot: number) {
  if (slot < 2 || slot > 6) return
  if (removing.value) return
  if (!form.mr1) return
  removing.value = true
  try {
    await imageApi.remove(form.mr1, 'detail', slot)
    delete images.value[slot]
    ElMessage.success(`详情图 slot ${slot} 已删除`)
  } catch {
    // 已被拦截器
  } finally {
    removing.value = false
  }
}

function isAppRowDirty(m: any): boolean {
  // Day 9.4: 车型行只要填了一个字段就算 dirty, 必填 brand/model
  return !!(m.machineBrand?.trim() || m.machineModel?.trim() || m.modelName?.trim() || m.engineBrand?.trim() || m.engineType?.trim())
}

onMounted(load)

// P2-7 修复 v2: 组件卸载时清理 reloadTimer, 避免用户导航离开后意外 reload
onBeforeUnmount(() => {
  if (reloadTimer !== null) {
    clearTimeout(reloadTimer)
    reloadTimer = null
  }
})
</script>

<template>
  <div class="p-3 w-full" v-loading="loading">
    <div class="flex items-center gap-2 mb-3">
      <el-button @click="router.back()" size="small">返回</el-button>
      <h1 class="text-lg font-medium">{{ isEdit ? t('admin.productformview.string.edit_product_id', { id: productId }) : t('admin.productformview.templatetext.add_product') }}</h1>
      <div class="flex-1" />
      <el-button type="primary" @click="save" :loading="saving">保存</el-button>
    </div>

    <el-form ref="formRef" :model="form" label-position="top" label-width="100px" size="small">
      <el-collapse v-model="activeNames">
        <!-- 分区 1: 基础信息 -->
        <el-collapse-item :title="t('admin.productformview.title.basic_info')" name="1">
          <div class="grid grid-cols-3 gap-3">
            <!-- P2.2: productName1/2/type 全部 typeahead -->
            <el-form-item :label="t('common.action.product_name_1')">
              <el-autocomplete v-model="form.productName1" :fetch-suggestions="queryProductName1"
                :placeholder="t('common.field.input_autocomplete')" clearable size="small" :trigger-on-focus="true" :debounce="200" />
            </el-form-item>
            <el-form-item :label="t('common.action.product_name_2')">
              <el-autocomplete v-model="form.productName2" :fetch-suggestions="queryProductName2"
                :placeholder="t('common.field.input_autocomplete')" clearable size="small" :trigger-on-focus="true" :debounce="200" />
            </el-form-item>
            <el-form-item :label="t('common.action.type')">
              <el-autocomplete v-model="form.type" :fetch-suggestions="queryType"
                placeholder="oil/fuel/air/cabin/others" clearable size="small" :trigger-on-focus="true" :debounce="200" />
            </el-form-item>
            <!-- V2 Task 1.1: MR.1 输入校验 (与后端 AdminProductService.ValidateForm 对齐)
                 - maxlength=10: 防止超长 (后端 PG 22001 + ArgumentException 双兜底)
                 - pattern=[A-Za-z0-9]{1,10}: 前端拦截非法字符, 与后端正则 ^[A-Za-z0-9]{1,10}$ 一致
                 - 必填: 后端 MR1_REQUIRED 校验, 前端 rules 同步拦截, 避免无谓 400 往返 -->
            <el-form-item label="MR.1" prop="mr1" :rules="mr1Rules">
              <el-input
                v-model="form.mr1"
                maxlength="10"
                pattern="[A-Za-z0-9]{1,10}"
                placeholder="1-10 位字母+数字 (必填)"
                show-word-limit
              />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.oem_required')"><el-input v-model="form.oem2" /></el-form-item>
            <el-form-item :label="t('common.field.publish')">
              <el-switch v-model="form.isPublished" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.remark')" class="col-span-3">
              <el-input v-model="form.remark" type="textarea" :rows="2" />
            </el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 2: 交叉引用 -->
        <el-collapse-item :title="`t('admin.productformview.string.cross_reference_count', { count: form.crossReferences.length })`" name="2">
          <div v-for="(x, i) in form.crossReferences" :key="i" class="flex gap-2 mb-2">
            <!-- Day 10: P1.3 自动补全 — 字典为空时降级为自由输入 -->
            <el-autocomplete
              v-model="x.oemBrand"
              :fetch-suggestions="queryOemBrands"
              :placeholder="t('admin.productformview.placeholder.brand_input_auto')"
              style="width: 200px"
              clearable
              size="small"
              :trigger-on-focus="true"
              :debounce="200"
            >
              <template #default="{ item }">
                <span>{{ item.brand }}</span>
              </template>
            </el-autocomplete>
            <!-- P2.2: OEM 3 typeahead -->
            <el-autocomplete v-model="x.oemNo3" :fetch-suggestions="queryOemNo3"
              :placeholder="t('admin.productformview.placeholder.oem_input_auto')" style="width: 240px" clearable size="small"
              :trigger-on-focus="true" :debounce="200" />
            <el-input v-model="x.productName1" :placeholder="t('common.field.product_name')" size="small" />
            <el-button text type="danger" @click="removeXref(i)">删除</el-button>
          </div>
          <el-button @click="addXref" size="small">+ 添加交叉引用</el-button>
        </el-collapse-item>

        <!-- 分区 3: 尺寸 -->
        <el-collapse-item :title="t('admin.productformview.title.dimensions_mm')" name="3">
          <div class="grid grid-cols-4 gap-3">
            <el-form-item label="D1"><el-input-number v-model="form.d1Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D2"><el-input-number v-model="form.d2Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D3"><el-input-number v-model="form.d3Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D4"><el-input-number v-model="form.d4Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H1"><el-input-number v-model="form.h1Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H2"><el-input-number v-model="form.h2Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H3"><el-input-number v-model="form.h3Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H4"><el-input-number v-model="form.h4Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item :label="t('common.field.d7_thread')"><el-input v-model="form.d7Thread" /></el-form-item>
            <el-form-item :label="t('common.field.d8_thread')"><el-input v-model="form.d8Thread" /></el-form-item>
            <el-form-item :label="t('common.field.check_valve_count')"><el-input-number v-model="form.noCheckValves" :min="0" /></el-form-item>
            <el-form-item :label="t('common.field.bypass_valve_count')"><el-input-number v-model="form.noBypassValves" :min="0" /></el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 5: 性能 -->
        <el-collapse-item :title="$t('common.field.performance')" name="5">
          <div class="grid grid-cols-3 gap-3">
            <!-- P2.2: Media 字段 typeahead (2 字段) -->
            <el-form-item label="Media">
              <el-autocomplete v-model="form.media" :fetch-suggestions="queryMedia"
                :placeholder="t('admin.productformview.placeholder.input_auto_name_model')" clearable size="small"
                :trigger-on-focus="true" :debounce="200" value-key="value" />
            </el-form-item>
            <el-form-item label="MediaModel"><el-input v-model="form.mediaModel" /></el-form-item>
            <el-form-item :label="t('common.field.efficiency_1')"><el-input v-model="form.efficiency1" /></el-form-item>
            <el-form-item :label="t('common.field.efficiency_2')"><el-input v-model="form.efficiency2" /></el-form-item>
            <el-form-item :label="t('admin.productformview.label.bypass_valve_lr')"><el-input-number v-model="form.bypassValveLr" :min="0" :precision="2" /></el-form-item>
            <el-form-item :label="t('admin.productformview.label.bypass_valve_hr')"><el-input-number v-model="form.bypassValveHr" :min="0" :precision="2" /></el-form-item>
            <el-form-item :label="t('common.field.bypass_pressure')"><el-input-number v-model="form.bypassPressure" :min="0" :precision="2" /></el-form-item>
            <el-form-item :label="t('admin.productformview.label.collapse_pressure_bar')"><el-input-number v-model="form.collapsePressureBar" :min="0" :precision="2" /></el-form-item>
            <el-form-item :label="t('common.action.seal_material')"><el-input v-model="form.sealingMaterial" /></el-form-item>
            <el-form-item :label="t('common.field.temperature_range')"><el-input v-model="form.tempRange" /></el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 6: 包装 -->
        <el-collapse-item :title="$t('common.field.packaging')" name="6">
          <div class="grid grid-cols-4 gap-3">
            <el-form-item :label="t('common.action.carton_per_pcs')">
              <el-input-number v-model="form.qtyPerCarton" :min="0" />
              <FieldHelpPopover field-key="qtyPerCarton" />
            </el-form-item>
            <el-form-item :label="t('common.field.weight_kg')">
              <el-input-number v-model="form.weightKgs" :min="0" :precision="3" />
              <FieldHelpPopover field-key="weightKgs" />
            </el-form-item>
            <el-form-item :label="t('common.field.carton_length_mm')">
              <el-input-number v-model="form.cartonLengthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="cartonLengthMm" />
            </el-form-item>
            <el-form-item :label="t('common.field.carton_width_mm')">
              <el-input-number v-model="form.cartonWidthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="cartonWidthMm" />
            </el-form-item>
            <el-form-item :label="t('common.field.carton_height_mm')">
              <el-input-number v-model="form.cartonHeightMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="cartonHeightMm" />
            </el-form-item>
            <!-- P5.1: 体积自动计算 (L*W*H/1e9 m³), 后端 DeriveVolume 兜底 -->
            <el-form-item :label="t('common.field.carton_volume_m3')">
              <el-input
                :model-value="cartonVolumeText"
                readonly
                :placeholder="t('common.field.auto_calculated')"
                class="!w-32"
              >
                <template #append>只读</template>
              </el-input>
              <FieldHelpPopover field-key="volumePerCartonM3" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.master_box_qty')">
              <el-input-number v-model="form.masterBoxQty" :min="0" />
              <FieldHelpPopover field-key="masterBoxQty" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.master_carton_kg')">
              <el-input-number v-model="form.masterBoxWeightKgs" :min="0" :precision="3" />
              <FieldHelpPopover field-key="masterBoxWeightKgs" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.master_carton_length_mm')">
              <el-input-number v-model="form.masterBoxLengthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="masterBoxLengthMm" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.master_carton_width_mm')">
              <el-input-number v-model="form.masterBoxWidthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="masterBoxWidthMm" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.master_carton_height_mm')">
              <el-input-number v-model="form.masterBoxHeightMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="masterBoxHeightMm" />
            </el-form-item>
            <el-form-item :label="t('admin.productformview.label.master_box_volume_m')">
              <el-input
                :model-value="masterBoxVolumeText"
                readonly
                :placeholder="t('common.field.auto_calculated')"
                class="!w-32"
              >
                <template #append>只读</template>
              </el-input>
            </el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 7: 车型 (P2.2: machine/engine 字段全部 typeahead) -->
        <el-collapse-item :title="`t('admin.productformview.string.machine_applications_count', { count: form.machineApplications.length })`" name="7">
          <div v-for="(m, i) in form.machineApplications" :key="i" class="grid grid-cols-5 gap-2 mb-2">
            <!-- 机型品牌: typeahead -->
            <el-autocomplete v-model="m.machineBrand" :fetch-suggestions="queryMachineBrand"
              :placeholder="t('admin.productformview.placeholder.brand_required')" size="small" clearable :trigger-on-focus="true" :debounce="200"
              :class="{ 'app-required': isAppRowDirty(m) && !m.machineBrand?.trim() }" />
            <!-- 机型型号: typeahead (与 brand 联动共享查询) -->
            <el-autocomplete v-model="m.machineModel" :fetch-suggestions="queryMachineModel"
              :placeholder="t('admin.productformview.placeholder.model_required')" size="small" clearable :trigger-on-focus="true" :debounce="200"
              :class="{ 'app-required': isAppRowDirty(m) && !m.machineModel?.trim() }" />
            <el-input v-model="m.modelName" :placeholder="t('common.action.name')" size="small" />
            <!-- 发动机品牌: typeahead -->
            <el-autocomplete v-model="m.engineBrand" :fetch-suggestions="queryEngineBrand"
              :placeholder="t('common.field.engine_brand')" size="small" clearable :trigger-on-focus="true" :debounce="200" />
            <div class="flex gap-1">
              <!-- 发动机型号: typeahead -->
              <el-autocomplete v-model="m.engineType" :fetch-suggestions="queryEngineType"
                :placeholder="t('admin.productformview.placeholder.engine_model')" size="small" clearable :trigger-on-focus="true" :debounce="200" />
              <el-button text type="danger" @click="removeApp(i)">×</el-button>
            </div>
          </div>
          <el-button @click="addApp" size="small">+ 添加车型</el-button>
        </el-collapse-item>

        <!-- V2 Task 3.3.1: 分区 8 图片 (主图区 + 详情图区分层, 仅编辑) -->
        <el-collapse-item v-if="isEdit" :title="t('admin.productformview.title.image')" name="8">
          <!-- 主图区: 按 OEM 3 命名, slot=1, 同 OEM 3 仅 1 张 -->
          <div class="mb-4 hairline p-3">
            <div class="text-sm font-medium mb-2">主图 (按 OEM 3 命名, slot=1)</div>
            <div class="flex items-center gap-2 mb-2">
              <span class="text-xs text-muted">关联 OEM 3:</span>
              <el-select
                v-model="selectedOemNo3ForPrimary"
                placeholder="选择 OEM 3"
                size="small"
                style="width: 240px"
                filterable
              >
                <el-option
                  v-for="x in form.crossReferences.filter((x: any) => x.oemNo3)"
                  :key="x.oemNo3"
                  :label="`${x.oemBrand || ''} - ${x.oemNo3}`"
                  :value="x.oemNo3"
                />
              </el-select>
            </div>
            <div v-if="images[1]" class="mb-2">
              <img :src="images[1].imageUrl" class="w-full h-32 object-contain bg-[var(--color-bg-hover)]" />
              <div class="text-xs text-muted mt-1">OEM 3: {{ images[1].oemNo3 || '-' }}</div>
              <el-button text type="danger" size="small" @click="removePrimaryImage" class="mt-1 w-full">删除主图</el-button>
            </div>
            <div v-else>
              <input type="file" accept="image/*" @change="uploadPrimaryImage" class="text-xs" />
              <!-- 改进 3.1: 主图上传进度条 -->
              <el-progress
                v-if="uploadingSlot === 'primary'"
                :percentage="uploadProgress"
                :stroke-width="6"
                :status="uploadProgress >= 100 ? 'success' : ''"
                class="mt-2"
              />
            </div>
          </div>

          <!-- 详情图区: 按 MR.1 共享, slot 2-6 -->
          <div class="hairline p-3">
            <div class="text-sm font-medium mb-2">详情图 (按 MR.1 共享, slot 2-6)</div>
            <div class="grid grid-cols-3 gap-3">
              <div v-for="slot in [2, 3, 4, 5, 6]" :key="slot" class="hairline p-2">
                <div class="text-xs text-muted mb-1">Slot {{ slot }}</div>
                <div v-if="images[slot]" class="mb-2">
                  <img :src="images[slot].imageUrl" class="w-full h-24 object-contain bg-[var(--color-bg-hover)]" />
                  <el-button text type="danger" size="small" @click="removeDetailImage(slot)" class="mt-1 w-full">删除</el-button>
                </div>
                <div v-else>
                  <input type="file" accept="image/*" @change="uploadDetailImage(slot, $event)" class="text-xs" />
                  <!-- 改进 3.1: 详情图上传进度条 -->
                  <el-progress
                    v-if="uploadingSlot === `detail-${slot}`"
                    :percentage="uploadProgress"
                    :stroke-width="6"
                    :status="uploadProgress >= 100 ? 'success' : ''"
                    class="mt-1"
                  />
                </div>
              </div>
            </div>
          </div>
        </el-collapse-item>
      </el-collapse>
    </el-form>
  </div>
</template>
<style scoped>
/* Day 9.4: 车型必填字段未填时红框提示 */
.app-required :deep(.el-input__wrapper) { box-shadow: 0 0 0 1px #ef4444 inset !important; }
</style>
