<template>
  <div class="filter-bar">
    <div class="filter-bar-fields">
      <a-form layout="inline" @submit.prevent>
        <template v-for="f in fields" :key="f.key">
          <a-form-item v-if="f.primary || expanded" :label="f.label">
            <!-- 输入框 -->
            <a-input
              v-if="f.type === 'input'"
              :value="values[f.key]"
              :placeholder="f.placeholder || '请输入'"
              allow-clear
              :style="{ width: `${f.width || 180}px` }"
              @update:value="(v: string) => setValue(f.key, v)"
              @press-enter="f.pressEnter !== false && emit('search')"
            />
            <!-- 下拉选择 -->
            <a-select
              v-else-if="f.type === 'select'"
              :value="values[f.key]"
              :placeholder="f.placeholder || '全部'"
              allow-clear
              :options="f.options || []"
              :style="{ width: `${f.width || 130}px` }"
              @update:value="(v: any) => setValue(f.key, v)"
            />
            <!-- 时间范围 -->
            <a-range-picker
              v-else-if="f.type === 'dateRange'"
              :value="values[f.key]"
              show-time
              format="YYYY-MM-DD HH:mm:ss"
              :placeholder="['开始时间', '结束时间']"
              :style="{ width: `${f.width || 360}px` }"
              @update:value="(v: any) => setValue(f.key, v)"
            />
          </a-form-item>
        </template>
        <!-- 页面自定义的筛选项插槽 -->
        <slot name="fields"></slot>
      </a-form>
    </div>

    <div class="filter-bar-actions">
      <a-button type="primary" :loading="loading" @click="emit('search')">查询</a-button>
      <a-button style="margin-left: 8px" @click="emit('reset')">重置</a-button>
      <!-- 页面额外操作按钮插槽 -->
      <slot name="extra"></slot>
      <a-button v-if="hasCollapsed" type="link" class="filter-bar-toggle" @click="expanded = !expanded">
        {{ expanded ? '收起' : '展开' }}
        <UpOutlined v-if="expanded" />
        <DownOutlined v-else />
      </a-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { DownOutlined, UpOutlined } from '@ant-design/icons-vue';

/** 筛选项定义：primary 的项默认显示，其余折叠到展开区 */
export interface FilterField {
  /** 对应 values 里的字段名 */
  key: string;
  label: string;
  /** 控件类型 */
  type: 'input' | 'select' | 'dateRange';
  /** 是否为核心筛选项（默认显示在最左侧） */
  primary?: boolean;
  placeholder?: string;
  /** select 的选项 */
  options?: { label: string; value: any }[];
  /** 控件宽度（px），input 默认 180、select 130、dateRange 360 */
  width?: number;
  /** input 回车是否触发查询，默认 true */
  pressEnter?: boolean;
}

const props = defineProps<{
  /** 筛选值对象（页面里的响应式对象，直接读写字段） */
  values: Record<string, any>;
  fields: FilterField[];
  loading?: boolean;
}>();

const emit = defineEmits<{
  (e: 'search'): void;
  (e: 'reset'): void;
}>();

const expanded = ref(false);

/** 被折叠的次要筛选项数量，大于 0 才显示展开按钮 */
const hasCollapsed = computed(() => props.fields.some((f) => !f.primary));

function setValue(key: string, val: any) {
  props.values[key] = val;
}
</script>

<style scoped>
.filter-bar {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}

.filter-bar-fields {
  flex: 1;
  min-width: 0;
}

.filter-bar-fields :deep(.ant-form-item) {
  margin-right: 12px;
  margin-bottom: 8px;
}

.filter-bar-actions {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  padding-bottom: 8px;
}

.filter-bar-toggle {
  padding: 0 4px;
}
</style>
