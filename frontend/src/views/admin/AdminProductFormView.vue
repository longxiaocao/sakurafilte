<script setup lang="ts">
// Day 9: 后台产品表单 (新增/编辑)
//   - 7 分区布局 (与 ProductFormDto 一致)
//   - 行内保存
// Day 10: 分区 2 oemBrand 改为 el-autocomplete, 从 dictApi.oemBrands.typeahead 自动补全 (P1.3)
// Day 10+ P2.2: 7 分区全部接入 typeahead (productName1/2/type/oemNo3/media/machine/engine)
// Day 10+ P5.1: 包装尺寸 L/W/H → Volume 自动计算 (m³), 母箱同理
// Day 10+ P5.2: 尺寸/性能字段后挂 ? 图标, 鼠标悬停显示字段说明 (FieldHelpPopover)
import { ref, reactive, onMounted, computed, watch, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { adminProductApi, imageApi, dictApi } from '@/api'
import type { ProductDetail } from '@/api/types'
import FieldHelpPopover from '@/components/FieldHelpPopover.vue'  // P5.2

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

const loading = ref(false)
const saving = ref(false)

const images = ref<{ slot: number; imageKey: string; imageUrl: string }[]>([])

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
    images.value = p.images || []
    // E2E BD.3 修复 v2: 保存 GET 时的 RowVersion (xmin), PUT 时带回实现乐观锁
    rowVersion.value = p.rowVersion ?? 0
  } catch (e: any) {} finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    if (isEdit.value) {
      // E2E BD.3 修复 v2: 带回 GET 时的 RowVersion, 后端用此值检测并发冲突
      await adminProductApi.update(productId.value, { ...form, rowVersion: rowVersion.value }, 'admin')
      ElMessage.success('已保存')
    } else {
      await adminProductApi.create(form, 'admin')
      ElMessage.success('已创建')
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
      if (title.includes('已被修改') || detail.includes('已被其他用户修改') || detail.includes('lost update')) {
        ElMessage.error('数据已被其他管理员修改, 请刷新后重试')
        // 自动重新加载最新数据, 避免用户手动刷新
        // P2-7 修复 v2: 保存 timer 引用, 卸载时清理, 避免用户导航离开后意外 reload
        if (reloadTimer !== null) clearTimeout(reloadTimer)
        reloadTimer = setTimeout(() => window.location.reload(), 1500)
      } else {
        ElMessage.error('产品已存在，请检查 OEM 号')
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

async function uploadImage(slot: number, e: Event) {
  // Day 9.3: 前端 slot 范围校验, 与后端 AdminProductImageService.UploadAsync 一致
  if (slot < 1 || slot > 6 || !Number.isInteger(slot)) {
    ElMessage.error('Slot 非法: ' + slot + ', 必须在 1-6 之间')
    return
  }
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  if (!isEdit.value) {
    ElMessage.warning('请先保存产品再上传图片')
    return
  }
  try {
    const r = await imageApi.upload(productId.value, slot, file)
    ElMessage.success(`Slot ${slot} 上传成功`)
    images.value[slot - 1] = r
  } catch (e: any) {}
  input.value = ''
}

async function removeImage(slot: number) {
  // Day 9.3: 前端 slot 范围校验
  if (slot < 1 || slot > 6 || !Number.isInteger(slot)) {
    ElMessage.error('Slot 非法: ' + slot + ', 必须在 1-6 之间')
    return
  }
  try {
    await imageApi.remove(productId.value, slot)
    images.value[slot - 1] = undefined as any
    ElMessage.success(`Slot ${slot} 已删除`)
  } catch (e: any) {}
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
  <div class="p-3 max-w-screen-xl mx-auto" v-loading="loading">
    <div class="flex items-center gap-2 mb-3">
      <el-button @click="router.back()" size="small">返回</el-button>
      <h1 class="text-lg font-medium">{{ isEdit ? `编辑产品 #${productId}` : '新增产品' }}</h1>
      <div class="flex-1" />
      <el-button type="primary" @click="save" :loading="saving">保存</el-button>
    </div>

    <el-form :model="form" label-position="top" label-width="100px" size="small">
      <el-collapse v-model="activeNames">
        <!-- 分区 1: 基础信息 -->
        <el-collapse-item title="① 基础信息" name="1">
          <div class="grid grid-cols-3 gap-3">
            <!-- P2.2: productName1/2/type 全部 typeahead -->
            <el-form-item label="产品名 1">
              <el-autocomplete v-model="form.productName1" :fetch-suggestions="queryProductName1"
                placeholder="输入自动补全" clearable size="small" :trigger-on-focus="true" :debounce="200" />
            </el-form-item>
            <el-form-item label="产品名 2">
              <el-autocomplete v-model="form.productName2" :fetch-suggestions="queryProductName2"
                placeholder="输入自动补全" clearable size="small" :trigger-on-focus="true" :debounce="200" />
            </el-form-item>
            <el-form-item label="类型">
              <el-autocomplete v-model="form.type" :fetch-suggestions="queryType"
                placeholder="oil/fuel/air/cabin/others" clearable size="small" :trigger-on-focus="true" :debounce="200" />
            </el-form-item>
            <el-form-item label="MR.1"><el-input v-model="form.mr1" /></el-form-item>
            <el-form-item label="OEM 2 (必填)"><el-input v-model="form.oem2" /></el-form-item>
            <el-form-item label="发布">
              <el-switch v-model="form.isPublished" />
            </el-form-item>
            <el-form-item label="备注" class="col-span-3">
              <el-input v-model="form.remark" type="textarea" :rows="2" />
            </el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 2: 交叉引用 -->
        <el-collapse-item :title="`② 交叉引用 (${form.crossReferences.length})`" name="2">
          <div v-for="(x, i) in form.crossReferences" :key="i" class="flex gap-2 mb-2">
            <!-- Day 10: P1.3 自动补全 — 字典为空时降级为自由输入 -->
            <el-autocomplete
              v-model="x.oemBrand"
              :fetch-suggestions="queryOemBrands"
              placeholder="品牌 (输入自动补全)"
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
              placeholder="OEM 3 (输入自动补全)" style="width: 240px" clearable size="small"
              :trigger-on-focus="true" :debounce="200" />
            <el-input v-model="x.productName1" placeholder="产品名" size="small" />
            <el-button text type="danger" @click="removeXref(i)">删除</el-button>
          </div>
          <el-button @click="addXref" size="small">+ 添加交叉引用</el-button>
        </el-collapse-item>

        <!-- 分区 3: 尺寸 -->
        <el-collapse-item title="③ 尺寸 (mm)" name="3">
          <div class="grid grid-cols-4 gap-3">
            <el-form-item label="D1"><el-input-number v-model="form.d1Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D2"><el-input-number v-model="form.d2Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D3"><el-input-number v-model="form.d3Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D4"><el-input-number v-model="form.d4Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H1"><el-input-number v-model="form.h1Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H2"><el-input-number v-model="form.h2Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H3"><el-input-number v-model="form.h3Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="H4"><el-input-number v-model="form.h4Mm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="D7 螺纹"><el-input v-model="form.d7Thread" /></el-form-item>
            <el-form-item label="D8 螺纹"><el-input v-model="form.d8Thread" /></el-form-item>
            <el-form-item label="单向阀数"><el-input-number v-model="form.noCheckValves" :min="0" /></el-form-item>
            <el-form-item label="旁通阀数"><el-input-number v-model="form.noBypassValves" :min="0" /></el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 5: 性能 -->
        <el-collapse-item title="④ 性能" name="5">
          <div class="grid grid-cols-3 gap-3">
            <!-- P2.2: Media 字段 typeahead (2 字段) -->
            <el-form-item label="Media">
              <el-autocomplete v-model="form.media" :fetch-suggestions="queryMedia"
                placeholder="输入自动补全 (name/model OR 匹配)" clearable size="small"
                :trigger-on-focus="true" :debounce="200" value-key="value" />
            </el-form-item>
            <el-form-item label="MediaModel"><el-input v-model="form.mediaModel" /></el-form-item>
            <el-form-item label="效率 1"><el-input v-model="form.efficiency1" /></el-form-item>
            <el-form-item label="效率 2"><el-input v-model="form.efficiency2" /></el-form-item>
            <el-form-item label="旁通阀 LR"><el-input-number v-model="form.bypassValveLr" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="旁通阀 HR"><el-input-number v-model="form.bypassValveHr" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="旁通压力"><el-input-number v-model="form.bypassPressure" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="破裂压力 (bar)"><el-input-number v-model="form.collapsePressureBar" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="密封材料"><el-input v-model="form.sealingMaterial" /></el-form-item>
            <el-form-item label="温度范围"><el-input v-model="form.tempRange" /></el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 6: 包装 -->
        <el-collapse-item title="⑤ 包装" name="6">
          <div class="grid grid-cols-4 gap-3">
            <el-form-item label="箱/件">
              <el-input-number v-model="form.qtyPerCarton" :min="0" />
              <FieldHelpPopover field-key="qtyPerCarton" />
            </el-form-item>
            <el-form-item label="重量 (kg)">
              <el-input-number v-model="form.weightKgs" :min="0" :precision="3" />
              <FieldHelpPopover field-key="weightKgs" />
            </el-form-item>
            <el-form-item label="箱长 (mm)">
              <el-input-number v-model="form.cartonLengthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="cartonLengthMm" />
            </el-form-item>
            <el-form-item label="箱宽 (mm)">
              <el-input-number v-model="form.cartonWidthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="cartonWidthMm" />
            </el-form-item>
            <el-form-item label="箱高 (mm)">
              <el-input-number v-model="form.cartonHeightMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="cartonHeightMm" />
            </el-form-item>
            <!-- P5.1: 体积自动计算 (L*W*H/1e9 m³), 后端 DeriveVolume 兜底 -->
            <el-form-item label="箱体积 (m³)">
              <el-input
                :model-value="cartonVolumeText"
                readonly
                placeholder="自动计算"
                class="!w-32"
              >
                <template #append>只读</template>
              </el-input>
              <FieldHelpPopover field-key="volumePerCartonM3" />
            </el-form-item>
            <el-form-item label="母箱数量">
              <el-input-number v-model="form.masterBoxQty" :min="0" />
              <FieldHelpPopover field-key="masterBoxQty" />
            </el-form-item>
            <el-form-item label="母箱重 (kg)">
              <el-input-number v-model="form.masterBoxWeightKgs" :min="0" :precision="3" />
              <FieldHelpPopover field-key="masterBoxWeightKgs" />
            </el-form-item>
            <el-form-item label="母箱长 (mm)">
              <el-input-number v-model="form.masterBoxLengthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="masterBoxLengthMm" />
            </el-form-item>
            <el-form-item label="母箱宽 (mm)">
              <el-input-number v-model="form.masterBoxWidthMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="masterBoxWidthMm" />
            </el-form-item>
            <el-form-item label="母箱高 (mm)">
              <el-input-number v-model="form.masterBoxHeightMm" :min="0" :precision="2" />
              <FieldHelpPopover field-key="masterBoxHeightMm" />
            </el-form-item>
            <el-form-item label="母箱体积 (m³)">
              <el-input
                :model-value="masterBoxVolumeText"
                readonly
                placeholder="自动计算"
                class="!w-32"
              >
                <template #append>只读</template>
              </el-input>
            </el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 7: 车型 (P2.2: machine/engine 字段全部 typeahead) -->
        <el-collapse-item :title="`⑥ 适用车型 (${form.machineApplications.length})`" name="7">
          <div v-for="(m, i) in form.machineApplications" :key="i" class="grid grid-cols-5 gap-2 mb-2">
            <!-- 机型品牌: typeahead -->
            <el-autocomplete v-model="m.machineBrand" :fetch-suggestions="queryMachineBrand"
              placeholder="品牌 (必填)" size="small" clearable :trigger-on-focus="true" :debounce="200"
              :class="{ 'app-required': isAppRowDirty(m) && !m.machineBrand?.trim() }" />
            <!-- 机型型号: typeahead (与 brand 联动共享查询) -->
            <el-autocomplete v-model="m.machineModel" :fetch-suggestions="queryMachineModel"
              placeholder="型号 (必填)" size="small" clearable :trigger-on-focus="true" :debounce="200"
              :class="{ 'app-required': isAppRowDirty(m) && !m.machineModel?.trim() }" />
            <el-input v-model="m.modelName" placeholder="名称" size="small" />
            <!-- 发动机品牌: typeahead -->
            <el-autocomplete v-model="m.engineBrand" :fetch-suggestions="queryEngineBrand"
              placeholder="发动机品牌" size="small" clearable :trigger-on-focus="true" :debounce="200" />
            <div class="flex gap-1">
              <!-- 发动机型号: typeahead -->
              <el-autocomplete v-model="m.engineType" :fetch-suggestions="queryEngineType"
                placeholder="发动机型号" size="small" clearable :trigger-on-focus="true" :debounce="200" />
              <el-button text type="danger" @click="removeApp(i)">×</el-button>
            </div>
          </div>
          <el-button @click="addApp" size="small">+ 添加车型</el-button>
        </el-collapse-item>

        <!-- 分区 8: 图片 (仅编辑) -->
        <el-collapse-item v-if="isEdit" title="⑦ 图片 (1-6 槽位)" name="8">
          <div class="grid grid-cols-3 gap-3">
            <div v-for="slot in 6" :key="slot" class="hairline p-2">
              <div class="text-xs text-muted mb-1">Slot {{ slot }}</div>
              <div v-if="images[slot - 1]" class="mb-2">
                <img :src="images[slot - 1].imageUrl" class="w-full h-32 object-contain bg-[var(--color-bg-hover)]" />
                <el-button text type="danger" size="small" @click="removeImage(slot)" class="mt-1 w-full">删除</el-button>
              </div>
              <div v-else>
                <input type="file" accept="image/*" @change="uploadImage(slot, $event)" class="text-xs" />
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
