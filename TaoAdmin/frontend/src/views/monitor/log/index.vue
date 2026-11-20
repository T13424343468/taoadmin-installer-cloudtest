<template>
  <div class="page-container">
    <!-- 筛选区 -->
    <FilterBar :values="filters" :fields="filterFields" :loading="loading" @search="loadData(1)" @reset="resetQuery">
      <template #extra>
        <a-button style="margin-left: 8px" @click="autoRefresh = !autoRefresh">
          {{ autoRefresh ? '停止自动刷新' : '开启自动刷新' }}
        </a-button>
      </template>
    </FilterBar>

    <!-- 日志表格（分页模式；数据量大时后端按 pageSize 截断） -->
    <a-table
      :data-source="logs"
      :columns="columns"
      :loading="loading"
      :pagination="pagination"
      row-key="id"
      size="small"
      :scroll="{ x: 1200 }"
      :custom-row="customRow"
      @change="onTableChange"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'timestamp'">
          <span class="log-time">{{ record.timestamp }}</span>
        </template>
        <template v-else-if="column.key === 'level'">
          <span :class="`log-tag-${record.level}`">{{ record.level }}</span>
        </template>
        <template v-else-if="column.key === 'service'">
          <a-tag>{{ record.service }}</a-tag>
        </template>
        <template v-else-if="column.key === 'traceId'">
          <code style="font-size: 11px">{{ record.traceId || '-' }}</code>
        </template>
        <template v-else-if="column.key === 'message'">
          <span class="log-message">{{ record.message }}</span>
        </template>
      </template>
      <template #empty>
        <a-empty description="暂无匹配日志" />
      </template>
    </a-table>

    <!-- 日志详情抽屉（含堆栈） -->
    <a-drawer v-model:open="detailVisible" title="日志详情" width="640">
      <a-descriptions v-if="detail" :column="2" size="small" bordered>
        <a-descriptions-item label="时间" :span="2">{{ detail.timestamp }}</a-descriptions-item>
        <a-descriptions-item label="级别">
          <span :class="`log-tag-${detail.level}`">{{ detail.level }}</span>
        </a-descriptions-item>
        <a-descriptions-item label="服务">{{ detail.service }}</a-descriptions-item>
        <a-descriptions-item label="实例">{{ detail.instance || '-' }}</a-descriptions-item>
        <a-descriptions-item label="线程">{{ detail.thread || '-' }}</a-descriptions-item>
        <a-descriptions-item label="TraceId" :span="2">
          <code>{{ detail.traceId || '-' }}</code>
        </a-descriptions-item>
        <a-descriptions-item label="Logger" :span="2">{{ detail.logger || '-' }}</a-descriptions-item>
      </a-descriptions>

      <div class="detail-section">
        <div class="section-title">消息内容</div>
        <pre class="log-pre">{{ detail?.message }}</pre>
      </div>

      <div v-if="detail?.exception" class="detail-section">
        <div class="section-title">异常堆栈</div>
        <pre class="log-pre exception">{{ detail.exception }}</pre>
      </div>
    </a-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted, watch } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import FilterBar, { type FilterField } from '@/components/FilterBar/index.vue';
import { getLogs, getServices, type LogItem, type ServiceItem } from '@/api/monitor';

const loading = ref(false);
const logs = ref<LogItem[]>([]);
const allServices = ref<ServiceItem[]>([]);

const query = reactive<{
  pageIndex: number;
  pageSize: number;
  level?: string;
  service?: string;
  keyword?: string;
}>({ pageIndex: 1, pageSize: 50 });

/** 筛选值统一放这里，FilterBar 直接读写；时间范围也在其中 */
const filters = reactive<{ timeRange?: [Dayjs, Dayjs] | null; level?: string; service?: string; keyword?: string }>({
  timeRange: null,
});

/** 服务下拉选项（筛选组件引用它，需在 filterFields 之前声明） */
const serviceOptionsForFilter = ref<{ label: string; value: string }[]>([]);

// 筛选项：时间和级别是核心条件默认显示，服务名和关键字折叠到展开区
const filterFields = computed<FilterField[]>(() => [
  { key: 'timeRange', label: '时间范围', type: 'dateRange', primary: true },
  { key: 'level', label: '级别', type: 'select', primary: true, placeholder: '全部', options: [
    { label: 'DEBUG', value: 'DEBUG' },
    { label: 'INFO', value: 'INFO' },
    { label: 'WARN', value: 'WARN' },
    { label: 'ERROR', value: 'ERROR' },
    { label: 'FATAL', value: 'FATAL' },
  ] },
  { key: 'service', label: '服务名', type: 'select', placeholder: '全部服务', options: serviceOptionsForFilter.value },
  { key: 'keyword', label: '关键字', type: 'input', placeholder: '消息关键字 / traceId', width: 220 },
]);

const autoRefresh = ref(false);
let refreshTimer: ReturnType<typeof setInterval> | null = null;

const serviceOptions = ref<{ label: string; value: string }[]>([]);

const pagination = reactive({
  current: 1,
  pageSize: 50,
  total: 0,
  showSizeChanger: true,
  showQuickJumper: true,
  pageSizeOptions: ['20', '50', '100', '200'],
  showTotal: (t: number) => `共 ${t} 条`,
});

const columns = [
  { title: '时间', key: 'timestamp', width: 180 },
  { title: '级别', key: 'level', width: 80 },
  { title: '服务', key: 'service', width: 150 },
  { title: 'TraceId', key: 'traceId', width: 150 },
  { title: '消息', key: 'message', ellipsis: true },
];

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) {
      query.pageIndex = pageIndex;
      pagination.current = pageIndex;
    }
    const params: Record<string, any> = {
      ...query,
      level: filters.level,
      service: filters.service,
      keyword: filters.keyword,
    };
    if (filters.timeRange) {
      params.startTime = filters.timeRange[0].format('YYYY-MM-DD HH:mm:ss');
      params.endTime = filters.timeRange[1].format('YYYY-MM-DD HH:mm:ss');
    }
    const res = await getLogs(params);
    logs.value = res.items || [];
    pagination.total = res.total || 0;
  } finally {
    loading.value = false;
  }
}

function onTableChange(pag: any) {
  query.pageIndex = pag.current;
  query.pageSize = pag.pageSize;
  pagination.current = pag.current;
  pagination.pageSize = pag.pageSize;
  loadData();
}

function resetQuery() {
  filters.level = undefined;
  filters.service = undefined;
  filters.keyword = undefined;
  filters.timeRange = null;
  loadData(1);
}

// ============ 日志详情 ============

const detailVisible = ref(false);
const detail = ref<LogItem | null>(null);

function customRow(record: LogItem) {
  return {
    style: { cursor: 'pointer' },
    onClick: () => {
      detail.value = record;
      detailVisible.value = true;
    },
  };
}

// ============ 自动刷新 ============

watch(autoRefresh, (on) => {
  if (on) {
    refreshTimer = setInterval(() => loadData(), 10000);
  } else if (refreshTimer) {
    clearInterval(refreshTimer);
    refreshTimer = null;
  }
});

onMounted(async () => {
  loadData();
  // 默认展示最近 1 小时
  filters.timeRange = [dayjs().subtract(1, 'hour'), dayjs()];
  const services = (await getServices().catch(() => [])) || [];
  allServices.value = services;
  serviceOptionsForFilter.value = services.map((s) => ({ label: s.displayName || s.serviceName, value: s.serviceName }));
});

onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer);
});
</script>

<style scoped lang="less">
.log-time {
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.65);
}

.log-message {
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
}

.detail-section {
  margin-top: 16px;

  .section-title {
    font-weight: 600;
    margin-bottom: 8px;
  }
}

.log-pre {
  background: #fafafa;
  border: 1px solid #f0f0f0;
  border-radius: 4px;
  padding: 12px;
  font-size: 12px;
  font-family: Consolas, Monaco, monospace;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 360px;
  overflow-y: auto;
  margin: 0;

  &.exception {
    background: #fff2f0;
    border-color: #ffccc7;
    color: #cf1322;
  }
}
</style>
