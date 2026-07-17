<script setup lang="ts">
// V2 Task 4.5.3: 产品详情页"询盘"子组件 (Vue client mount)
//   - 显示询盘按钮, 点击弹窗表单 (ElDialog)
//   - 表单: 联系人/电话/邮箱/留言
//   - 必填校验: 联系人 + (电话|邮箱 至少一项)
//   - 提交: 后端 /api/public/inquiry 待 Phase 5 实现, 当前用 mailto: 兜底
//   - 注意: props.oemNo3 实际传值为 product.oemNoDisplay (产品 OEM 编号)
import { ref, reactive, computed } from 'vue'
import { ElMessage } from 'element-plus'

interface InquiryProps {
  mr1: string | null
  oemNo3: string
  brand?: string | null
  productName1?: string | null
}

const props = defineProps<InquiryProps>()

const dialogVisible = ref(false)

interface InquiryForm {
  contact: string
  phone: string
  email: string
  message: string
}

const form = reactive<InquiryForm>({
  contact: '',
  phone: '',
  email: '',
  message: ''
})

const canSubmit = computed(() => {
  return form.contact.trim() !== '' && (form.phone.trim() !== '' || form.email.trim() !== '')
})

function openDialog(): void {
  // 预填留言: 产品基本信息
  if (!form.message) {
    form.message = `询盘: ${props.productName1 ?? ''} ${props.brand ?? ''} ${props.oemNo3}`.replace(/\s+/g, ' ').trim()
  }
  dialogVisible.value = true
}

function submit(): void {
  if (!canSubmit.value) {
    ElMessage.warning('请填写联系人, 并至少提供电话或邮箱')
    return
  }
  // TODO Phase 5: POST /api/public/inquiry (后端 API 待实现)
  // 当前: 用 mailto: 兜底, 让用户用邮件客户端发送
  const subject = encodeURIComponent(`[询盘] ${props.oemNo3} - ${props.productName1 ?? ''}`)
  const body = encodeURIComponent(
    `联系人: ${form.contact}\n` +
    `电话: ${form.phone}\n` +
    `邮箱: ${form.email}\n` +
    `产品 OEM: ${props.oemNo3}\n` +
    `MR.1: ${props.mr1 ?? '-'}\n` +
    `品牌: ${props.brand ?? '-'}\n\n` +
    `留言:\n${form.message}`
  )
  window.location.href = `mailto:sales@sakurafilter.com?subject=${subject}&body=${body}`
  dialogVisible.value = false
  ElMessage.success('已打开邮件客户端, 请发送询盘邮件')
}
</script>

<template>
  <div class="inquiry-app">
    <button type="button" class="inquiry-btn" @click="openDialog">
      立即询盘
    </button>
    <el-dialog v-model="dialogVisible" title="产品询盘" width="500px">
      <el-form label-width="80px">
        <el-form-item label="联系人" required>
          <el-input v-model="form.contact" placeholder="请输入您的姓名" maxlength="50" />
        </el-form-item>
        <el-form-item label="电话">
          <el-input v-model="form.phone" placeholder="电话或邮箱至少填一项" maxlength="30" />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input v-model="form.email" placeholder="电话或邮箱至少填一项" maxlength="80" />
        </el-form-item>
        <el-form-item label="产品">
          <span class="product-info">
            {{ props.oemNo3 }} / {{ props.brand ?? '-' }} / {{ props.productName1 ?? '-' }}
          </span>
        </el-form-item>
        <el-form-item label="留言">
          <el-input v-model="form.message" type="textarea" :rows="4" maxlength="500" show-word-limit />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!canSubmit" @click="submit">发送询盘</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.inquiry-btn {
  padding: 8px 16px;
  font-size: 14px;
  background: #fff;
  color: #000;
  border: 1px solid #000;
  cursor: pointer;
  border-radius: 4px;
}
.inquiry-btn:hover {
  background: #f5f5f5;
}
.product-info {
  font-size: 13px;
  color: #666;
}
</style>
