<script setup lang="ts">
// Day 10+ P5.2 字段说明 popover
//   - ? 图标按钮, 鼠标悬停显示字段说明
//   - 内容从 data/field-help.ts 静态文案取 (单源真相, 后台表单和帮助页共用)
//   - el-popover 默认 hover 触发, width 280px
import { computed } from 'vue'
import { QuestionFilled } from '@element-plus/icons-vue'
import { getFieldHelp } from '@/data/field-help'

interface Props {
  fieldKey: string  // ProductDetail 字段名 (camelCase)
  placement?: 'top' | 'bottom' | 'left' | 'right'
}

const props = withDefaults(defineProps<Props>(), { placement: 'top' })

const help = computed(() => getFieldHelp(props.fieldKey))
</script>

<template>
  <el-popover
    v-if="help"
    :placement="placement"
    :width="280"
    trigger="hover"
    :show-after="200"
  >
    <template #reference>
      <el-icon class="field-help-trigger text-muted cursor-help" style="vertical-align: middle">
        <QuestionFilled />
      </el-icon>
    </template>
    <div class="text-sm leading-relaxed">
      <div class="font-medium mb-1">
        {{ help.label }}
        <span v-if="help.unit" class="text-muted text-xs ml-1">({{ help.unit }})</span>
      </div>
      <div class="text-muted">{{ help.description }}</div>
      <div v-if="help.example" class="mt-1 text-xs text-muted">
        示例: <code class="bg-neutral-100 dark:bg-neutral-800 px-1">{{ help.example }}</code>
      </div>
    </div>
  </el-popover>
</template>

<style scoped>
.field-help-trigger {
  font-size: 14px;
  margin-left: 4px;
  transition: color 0.15s;
}
.field-help-trigger:hover {
  color: var(--color-accent) !important;
}
</style>
