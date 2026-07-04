<script setup lang="ts">
// 空状态组件 (P1.5)
//   - 三种语义: empty (无数据) / no-result (搜索无结果) / error (加载失败)
//   - 工业极简融合风: 大图标 + 简洁文案 + 可选操作按钮
//   - 全部使用 CSS 变量, 跟随主题切换
import { computed } from 'vue'

interface Props {
  // 状态类型
  type?: 'empty' | 'no-result' | 'error'
  // 主标题
  title?: string
  // 副标题
  description?: string
  // 操作按钮文字 (留空则不显示)
  actionText?: string
  // 图标 (Element Plus 图标组件名, 默认按 type 自动选择)
  icon?: string
}

const props = withDefaults(defineProps<Props>(), {
  type: 'empty',
  title: '',
  description: '',
  actionText: '',
  icon: ''
})

const emit = defineEmits<{ action: [] }>()

const defaultConfig = {
  empty: { icon: 'Inbox', title: '暂无数据', description: '当前列表为空' },
  'no-result': { icon: 'Search', title: '未找到匹配结果', description: '尝试调整搜索条件或更换关键词' },
  error: { icon: 'WarningFilled', title: '加载失败', description: '请稍后重试或联系管理员' }
}

const config = computed(() => ({
  icon: props.icon || defaultConfig[props.type].icon,
  title: props.title || defaultConfig[props.type].title,
  description: props.description || defaultConfig[props.type].description
}))

function onAction() {
  emit('action')
}
</script>

<template>
  <div
    class="empty-state py-12 px-4 text-center"
    :class="type === 'error' ? 'text-red-600' : 'text-muted'"
    role="status"
    :aria-label="config.title"
    aria-live="polite"
  >
    <el-icon class="text-5xl mb-3 opacity-50" aria-hidden="true">
      <component :is="config.icon" />
    </el-icon>
    <div class="text-base font-medium mb-1 text-[var(--color-text)]">{{ config.title }}</div>
    <div class="text-sm mb-4">{{ config.description }}</div>
    <el-button
      v-if="actionText"
      size="default"
      @click="onAction"
      :aria-label="actionText"
    >
      {{ actionText }}
    </el-button>
  </div>
</template>

<style scoped>
.empty-state {
  min-height: 240px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}
</style>
