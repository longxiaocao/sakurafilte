<script setup lang="ts">
// Day 9: 后台产品表单 (新增/编辑)
//   - 7 分区布局 (与 ProductFormDto 一致)
//   - 行内保存
import { ref, reactive, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { adminProductApi, imageApi } from '@/api'
import type { ProductDetail } from '@/api/types'

const route = useRoute()
const router = useRouter()

const isEdit = computed(() => !!route.params.id)
const productId = computed(() => (route.params.id ? Number(route.params.id) : 0))
const activeNames = ref(['1', '3', '5', '6'])

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
  masterBoxQty: null,
  masterBoxWeightKgs: null,
  masterBoxLengthMm: null,
  masterBoxWidthMm: null,
  masterBoxHeightMm: null,
  crossReferences: [],
  machineApplications: []
})

const loading = ref(false)
const saving = ref(false)

const images = ref<{ slot: number; imageKey: string; imageUrl: string }[]>([])

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
  } catch (e: any) {} finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    if (isEdit.value) {
      await adminProductApi.update(productId.value, form, 'admin')
      ElMessage.success('已保存')
    } else {
      await adminProductApi.create(form, 'admin')
      ElMessage.success('已创建')
    }
    router.push('/admin/products')
  } catch (e: any) {} finally {
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

onMounted(load)
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
            <el-form-item label="产品名 1"><el-input v-model="form.productName1" /></el-form-item>
            <el-form-item label="产品名 2"><el-input v-model="form.productName2" /></el-form-item>
            <el-form-item label="类型">
              <el-select v-model="form.type" clearable>
                <el-option label="oil" value="oil" />
                <el-option label="fuel" value="fuel" />
                <el-option label="air" value="air" />
                <el-option label="cabin" value="cabin" />
                <el-option label="others" value="others" />
              </el-select>
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
            <el-input v-model="x.oemBrand" placeholder="品牌" style="width: 200px" />
            <el-input v-model="x.oemNo3" placeholder="OEM 3" style="width: 240px" />
            <el-input v-model="x.productName1" placeholder="产品名" />
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
            <el-form-item label="Media"><el-input v-model="form.media" /></el-form-item>
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
            <el-form-item label="箱/件"><el-input-number v-model="form.qtyPerCarton" :min="0" /></el-form-item>
            <el-form-item label="重量 (kg)"><el-input-number v-model="form.weightKgs" :min="0" :precision="3" /></el-form-item>
            <el-form-item label="箱长 (mm)"><el-input-number v-model="form.cartonLengthMm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="箱宽 (mm)"><el-input-number v-model="form.cartonWidthMm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="箱高 (mm)"><el-input-number v-model="form.cartonHeightMm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="母箱数量"><el-input-number v-model="form.masterBoxQty" :min="0" /></el-form-item>
            <el-form-item label="母箱重量 (kg)"><el-input-number v-model="form.masterBoxWeightKgs" :min="0" :precision="3" /></el-form-item>
            <el-form-item label="母箱长 (mm)"><el-input-number v-model="form.masterBoxLengthMm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="母箱宽 (mm)"><el-input-number v-model="form.masterBoxWidthMm" :min="0" :precision="2" /></el-form-item>
            <el-form-item label="母箱高 (mm)"><el-input-number v-model="form.masterBoxHeightMm" :min="0" :precision="2" /></el-form-item>
          </div>
        </el-collapse-item>

        <!-- 分区 7: 车型 -->
        <el-collapse-item :title="`⑥ 适用车型 (${form.machineApplications.length})`" name="7">
          <div v-for="(m, i) in form.machineApplications" :key="i" class="grid grid-cols-5 gap-2 mb-2">
            <el-input v-model="m.machineBrand" placeholder="品牌" />
            <el-input v-model="m.machineModel" placeholder="型号" />
            <el-input v-model="m.modelName" placeholder="名称" />
            <el-input v-model="m.engineBrand" placeholder="发动机品牌" />
            <div class="flex gap-1">
              <el-input v-model="m.engineType" placeholder="发动机型号" />
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
                <img :src="images[slot - 1].imageUrl" class="w-full h-32 object-contain bg-neutral-50" />
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
