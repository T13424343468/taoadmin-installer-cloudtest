<template>
  <a-card title="审计日志">
    <template #extra>
      <a-space>
        <template v-if="selectedCount > 0">
          <span class="batch-count">已选 {{ selectedCount }} 项</span>
          <a-button danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
          <a @click="clearSelection">清空勾选</a>
        </template>
        <a-popconfirm title="立即按保留策略清理过期日志？" @confirm="onCleanup">
          <a-button danger :loading="cleaning">清理过期日志</a-button>
        </a-popconfirm>
        <a-button :loading="exporting" @click="onExport">导出 Excel</a-button>
        <a-button v-if="selectedCount === 0 && total > 0" @click="onSelectAll">选中全部 {{ total }} 条</a-button>
      </a-space>
    </template>

    <a-form layout="inline" style="margin-bottom: 12px" @submit.prevent="reload">
      <a-form-item label="类别">
        <a-select v-model:value="query.category" style="width: 120px" allowClear placeholder="全部">
          <a-select-option value="login">登录</a-select-option>
          <a-select-option value="operation">操作</a-select-option>
        </a-select>
      </a-form-item>
      <a-form-item label="账号">
        <a-input v-model:value="query.userName" placeholder="账号" style="width: 140px" allowClear />
      </a-form-item>
      <a-form-item label="结果">
        <a-select v-model:value="success" style="width: 110px" allowClear placeholder="全部">
          <a-select-option :value="1">成功</a-select-option>
          <a-select-option :value="0">失败</a-select-option>
        </a-select>
      </a-form-item>
      <a-form-item label="时间区间">
        <a-range-picker v-model:value="range" show-time value-format="YYYY-MM-DD HH:mm:ss" />
      </a-form-item>
      <a-form-item>
        <a-button type="primary" @click="reload">查询</a-button>
      </a-form-item>
    </a-form>

    <a-table
      :columns="columns"
      :data-source="rows"
      :loading="loading"
      :pagination="{
        current: query.pageIndex,
        pageSize: query.pageSize,
        total,
        showSizeChanger: true,
        onChange: (p: number, s: number) => { query.pageIndex = p; query.pageSize = s; void load(); },
      }"
      row-key="id"
      size="small"
      :row-selection="{ selectedRowKeys, onChange: onTableSelectChange }"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'success'">
          <a-tag :color="record.success ? 'green' : 'red'">{{ record.success ? '成功' : '失败' }}</a-tag>
        </template>
      </template>
    </a-table>
  </a-card>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { useBatchSelection } from '@/composables/useBatchSelection';
import { pageAfterDelete } from '@/utils/listPage';
import { getAuditLogs, cleanupAuditLogs, batchDeleteAuditLogs, exportAuditLogs, type AuditLogItem } from '@/api/audit';
import type { Dayjs } from 'dayjs';

const columns = [
  { title: '时间', dataIndex: 'createdAt', key: 'createdAt', width: 170 },
  { title: '类别', dataIndex: 'category', key: 'category', width: 100 },
  { title: '账号', dataIndex: 'userName', key: 'userName', width: 120 },
  { title: '动作', dataIndex: 'action', key: 'action' },
  { title: '结果', key: 'success', width: 80 },
  { title: 'IP', dataIndex: 'ip', key: 'ip', width: 140 },
];

const rows = ref<AuditLogItem[]>([]);
const total = ref(0);
const loading = ref(false);
const cleaning = ref(false);
const success = ref<number | undefined>();
const range = ref<[string, string] | Dayjs[] | undefined>();

// ============ 批量选择（跨页保留） ============

const batchLoading = ref(false);
const {
  selectedRowKeys,
  selectedCount,
  onTableSelectChange,
  setTotal,
  selectAll,
  clearSelection,
  getSelectedIds,
} = useBatchSelection(async () => rows.value.map((r) => r.id));

async function onSelectAll() {
  await selectAll();
  message.info(`已按当前筛选条件选中全部 ${selectedCount.value} 条`);
}

const query = reactive({
  pageIndex: 1,
  pageSize: 20,
  category: undefined as string | undefined,
  userName: undefined as string | undefined,
});

// 游标缓存：记录每一页返回的下一页游标，翻下一页时带上可恒定速度定位（亿级深翻页不衰减）
const cursorMap = new Map<number, string>();

function rangeParams() {
  const r = range.value as [string, string] | undefined;
  return r && r[0] && r[1] ? { begin: r[0], end: r[1] } : {};
}

async function load() {
  loading.value = true;
  try {
    // 上一页的游标存在时走游标翻页（顺序翻页场景）；跳页/首次加载回退 OFFSET
    const prevCursor = cursorMap.get(query.pageIndex - 1);
    const page = await getAuditLogs({
      ...query,
      success: success.value,
      ...rangeParams(),
      ...(prevCursor ? { cursor: prevCursor } : {}),
    });
    rows.value = page.items;
    total.value = page.total;
    setTotal(page.total);
    if (page.nextCursor) cursorMap.set(query.pageIndex, page.nextCursor);
    else cursorMap.delete(query.pageIndex);
  } finally {
    loading.value = false;
  }
}

function reload() {
  query.pageIndex = 1;
  cursorMap.clear();
  clearSelection();
  void load();
}

function onBatchDelete() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除',
    content: `确定删除选中的 ${ids.length} 条审计日志吗？删除后不可恢复。`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const r = await batchDeleteAuditLogs(ids);
        message.success(`批量删除完成：成功 ${r.deleted} 条`);
        clearSelection();
        query.pageIndex = pageAfterDelete(total.value, r.deleted, query.pageIndex, query.pageSize);
        cursorMap.clear();
        await load();
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

async function onCleanup() {
  cleaning.value = true;
  try {
    const r = await cleanupAuditLogs();
    message.success(`已清理 ${r.deleted} 条过期日志`);
    query.pageIndex = pageAfterDelete(total.value, r.deleted, query.pageIndex, query.pageSize);
    cursorMap.clear();
    await load();
  } finally {
    cleaning.value = false;
  }
}

// ============ 导出 Excel（⑮）：按当前筛选条件，后端上限 5 万条 ============

const exporting = ref(false);

async function onExport() {
  exporting.value = true;
  try {
    const blob = await exportAuditLogs({
      category: query.category,
      userName: query.userName,
      success: success.value,
      ...rangeParams(),
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `audit-log-${new Date().toISOString().slice(0, 19).replace(/[-:T]/g, '')}.xlsx`;
    a.click();
    URL.revokeObjectURL(url);
    message.success('导出完成，已按当前筛选条件生成');
  } finally {
    exporting.value = false;
  }
}

onMounted(load);
</script>

<style scoped>
.batch-count {
  color: #1677ff;
}
</style>
