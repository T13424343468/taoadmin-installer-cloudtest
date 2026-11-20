<template>
  <a-card title="审计日志">
    <template #extra>
      <a-popconfirm title="立即按保留策略清理过期日志？" @confirm="onCleanup">
        <a-button danger :loading="cleaning">清理过期日志</a-button>
      </a-popconfirm>
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
import { message } from 'ant-design-vue';
import { getAuditLogs, cleanupAuditLogs, type AuditLogItem } from '@/api/audit';
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

const query = reactive({
  pageIndex: 1,
  pageSize: 20,
  category: undefined as string | undefined,
  userName: undefined as string | undefined,
});

function rangeParams() {
  const r = range.value as [string, string] | undefined;
  return r && r[0] && r[1] ? { begin: r[0], end: r[1] } : {};
}

async function load() {
  loading.value = true;
  try {
    const page = await getAuditLogs({ ...query, success: success.value, ...rangeParams() });
    rows.value = page.items;
    total.value = page.total;
  } finally {
    loading.value = false;
  }
}

function reload() {
  query.pageIndex = 1;
  void load();
}

async function onCleanup() {
  cleaning.value = true;
  try {
    const r = await cleanupAuditLogs();
    message.success(`已清理 ${r.deleted} 条过期日志`);
    await load();
  } finally {
    cleaning.value = false;
  }
}

onMounted(load);
</script>
