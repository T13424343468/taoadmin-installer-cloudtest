<template>
  <div class="page-container">
    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'job:add'" type="primary" @click="openForm()">
        <template #icon><PlusOutlined /></template>
        新增任务
      </a-button>
      <a-button style="margin-left: 8px" @click="loadData(1)">刷新</a-button>
      <a-space v-if="selectedCount > 0" style="margin-left: 12px">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button v-permission="'job:pause'" :loading="batchLoading" @click="onBatchToggle('pause')">批量暂停</a-button>
        <a-button v-permission="'job:resume'" :loading="batchLoading" @click="onBatchToggle('resume')">批量恢复</a-button>
        <a-button v-permission="'job:delete'" danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
        <a @click="onClearSelection">清空勾选</a>
      </a-space>
      <a-button
        v-else-if="pagination.total > 0"
        style="margin-left: 12px"
        @click="onSelectAll"
      >选中全部 {{ pagination.total }} 条</a-button>
    </div>

    <a-table
      :data-source="data"
      :columns="columns"
      :loading="loading"
      :pagination="pagination"
      row-key="id"
      size="middle"
      :row-selection="{ selectedRowKeys, onChange: onTableSelectChange }"
      @change="onTableChange"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'status'">
          <a-badge
            :status="record.status === 'RUNNING' ? 'processing' : 'default'"
            :text="record.status === 'RUNNING' ? '运行中' : '已暂停'"
          />
        </template>
        <template v-else-if="column.key === 'handler'">
          <code>{{ record.handler }}</code>
        </template>
        <template v-else-if="column.key === 'lastResult'">
          <a-tag v-if="record.lastResult" :color="record.lastResult === 'SUCCESS' ? 'green' : 'red'">
            {{ record.lastResult }}
          </a-tag>
          <span v-else class="text-muted">-</span>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a v-permission="'job:run'" @click="onRunOnce(record)">执行一次</a>
            <a v-permission="'job:list'" @click="openHistory(record)">历史</a>
            <a v-if="record.status === 'RUNNING'" v-permission="'job:pause'" @click="onToggle(record, 'pause')">
              暂停
            </a>
            <a v-else v-permission="'job:resume'" @click="onToggle(record, 'resume')">恢复</a>
            <a v-permission="'job:edit'" @click="openForm(record)">编辑</a>
            <a-popconfirm
              v-permission="'job:delete'"
              title="确定删除该任务吗？"
              @confirm="onDelete(record)"
            >
              <a class="danger-link">删除</a>
            </a-popconfirm>
          </a-space>
        </template>
      </template>
    </a-table>

    <!-- 新增/编辑弹窗 -->
    <a-modal
      v-model:open="formVisible"
      :title="form.id ? '编辑任务' : '新增任务'"
      :confirm-loading="submitting"
      @ok="onSubmit"
    >
      <a-form :model="form" :rules="formRules" layout="vertical">
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="任务名称" name="name">
              <a-input v-model:value="form.name" placeholder="如：清理过期Token" />
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="任务分组" name="group">
              <a-input v-model:value="form.group" placeholder="如：system" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="Cron 表达式" name="cron">
              <a-input v-model:value="form.cron" placeholder="如 0 0/5 * * * ?" />
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="处理器" name="handler">
              <a-input v-model:value="form.handler" placeholder="如 cleanExpiredTokensJob" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="任务参数">
          <a-textarea v-model:value="form.params" :rows="2" placeholder='JSON 格式参数，如 {"days": 30}' />
        </a-form-item>
        <a-form-item label="失败自动重试次数" help="执行失败后自动重试的次数，0 表示不重试">
          <a-input-number v-model:value="form.retryCount" :min="0" :max="10" style="width: 160px" />
        </a-form-item>
        <a-form-item label="描述">
          <a-textarea v-model:value="form.description" :rows="2" placeholder="任务说明" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 执行历史抽屉 -->
    <a-drawer
      v-model:open="historyVisible"
      :title="`执行历史：${historyJob?.name ?? ''}`"
      width="720"
      destroy-on-close
    >
      <a-table
        :data-source="historyLogs"
        :columns="historyColumns"
        :loading="historyLoading"
        row-key="id"
        size="middle"
        :pagination="historyPagination"
        @change="onHistoryPageChange"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'success'">
            <a-tag :color="record.success ? 'green' : 'red'">{{ record.success ? '成功' : '失败' }}</a-tag>
          </template>
          <template v-else-if="column.key === 'durationMs'">
            {{ record.durationMs }} ms
          </template>
          <template v-else-if="column.key === 'retryIndex'">
            {{ record.retryIndex === 0 ? '首次' : `第 ${record.retryIndex} 次重试` }}
          </template>
          <template v-else-if="column.key === 'message'">
            <span v-if="record.message" class="error-text">{{ record.message }}</span>
            <span v-else class="text-muted">-</span>
          </template>
        </template>
      </a-table>
    </a-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { PlusOutlined } from '@ant-design/icons-vue';
import { useBatchSelection } from '@/composables/useBatchSelection';
import { pageAfterDelete } from '@/utils/listPage';
import { runBatchOneByOne } from '@/utils/batchRun';
import {
  getJobPage,
  saveJob,
  deleteJob,
  pauseJob,
  resumeJob,
  runJobOnce,
  getJobLogs,
  type JobItem,
  type JobLogItem,
} from '@/api/job';

const loading = ref(false);
const data = ref<JobItem[]>([]);

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
} = useBatchSelection(async () => {
  const res = await getJobPage({ pageIndex: 1, pageSize: 10000 });
  return (res.items || []).map((j) => j.id);
});

async function onSelectAll() {
  await selectAll();
  message.info(`已按当前筛选条件选中全部 ${selectedCount.value} 条`);
}

function onClearSelection() {
  clearSelection();
}

const pagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showQuickJumper: true,
  showTotal: (t: number) => `共 ${t} 条`,
});

const columns = [
  { title: '任务名称', dataIndex: 'name', key: 'name', width: 160 },
  { title: '分组', dataIndex: 'group', key: 'group', width: 100 },
  { title: 'Cron', dataIndex: 'cron', key: 'cron', width: 140 },
  { title: '处理器', key: 'handler', width: 200 },
  { title: '状态', key: 'status', width: 100 },
  { title: '上次执行', dataIndex: 'lastFireTime', key: 'lastFireTime', width: 170 },
  { title: '下次执行', dataIndex: 'nextFireTime', key: 'nextFireTime', width: 170 },
  { title: '上次结果', key: 'lastResult', width: 100 },
  { title: '操作', key: 'action', width: 240, fixed: 'right' as const },
];

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) pagination.current = pageIndex;
    const res = await getJobPage({ pageIndex: pagination.current, pageSize: pagination.pageSize });
    data.value = res.items || [];
    pagination.total = res.total || 0;
    setTotal(res.total || 0);
  } finally {
    loading.value = false;
  }
}

function onTableChange(pag: any) {
  pagination.current = pag.current;
  pagination.pageSize = pag.pageSize;
  loadData();
}

// ============ 新增/编辑 ============

const formVisible = ref(false);
const submitting = ref(false);
const form = reactive<{
  id?: string;
  name: string;
  group?: string;
  cron: string;
  handler: string;
  params?: string;
  description?: string;
  retryCount?: number;
}>({ name: '', cron: '', handler: '', retryCount: 0 });

const formRules = {
  name: [{ required: true, message: '请输入任务名称', trigger: 'blur' }],
  cron: [{ required: true, message: '请输入 Cron 表达式', trigger: 'blur' }],
  handler: [{ required: true, message: '请输入处理器', trigger: 'blur' }],
};

function openForm(record?: JobItem) {
  if (record) {
    Object.assign(form, { ...record });
  } else {
    Object.assign(form, { id: undefined, name: '', group: '', cron: '', handler: '', params: '', description: '', retryCount: 0 });
  }
  formVisible.value = true;
}

async function onSubmit() {
  submitting.value = true;
  try {
    await saveJob({ ...form });
    message.success(form.id ? '更新成功' : '新增成功');
    formVisible.value = false;
    loadData();
  } finally {
    submitting.value = false;
  }
}

async function onDelete(record: JobItem) {
  await deleteJob(record.id);
  message.success('删除成功');
  loadData(pageAfterDelete(pagination.total, 1, pagination.current, pagination.pageSize));
}

// ============ 批量操作 ============

function onBatchDelete() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除任务',
    content: `确定删除选中的 ${ids.length} 个任务吗？删除后不可恢复。`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const r = await runBatchOneByOne(ids, (id) => deleteJob(id));
        message.success(`删除完成：成功 ${r.okCount} 条${r.failedCount ? `，失败 ${r.failedCount} 条` : ''}`);
        clearSelection();
        loadData(pageAfterDelete(pagination.total, r.okCount, pagination.current, pagination.pageSize));
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

function onBatchToggle(action: 'pause' | 'resume') {
  const ids = getSelectedIds();
  if (!ids.length) return;
  const label = action === 'pause' ? '暂停' : '恢复';
  Modal.confirm({
    title: `批量${label}`,
    content: `确定${label}选中的 ${ids.length} 个任务吗？`,
    async onOk() {
      batchLoading.value = true;
      try {
        const r = await runBatchOneByOne(ids, (id) => (action === 'pause' ? pauseJob(id) : resumeJob(id)));
        message.success(`批量${label}完成：成功 ${r.okCount} 条${r.failedCount ? `，失败 ${r.failedCount} 条` : ''}`);
        clearSelection();
        loadData();
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

// ============ 暂停 / 恢复 / 执行一次 ============

async function onToggle(record: JobItem, action: 'pause' | 'resume') {
  if (action === 'pause') {
    await pauseJob(record.id);
    message.success(`任务「${record.name}」已暂停`);
  } else {
    await resumeJob(record.id);
    message.success(`任务「${record.name}」已恢复`);
  }
  loadData();
}

function onRunOnce(record: JobItem) {
  Modal.confirm({
    title: '立即执行',
    content: `确定立即执行任务「${record.name}」一次吗？`,
    onOk: async () => {
      await runJobOnce(record.id);
      message.success('触发执行成功，请稍后查看执行结果');
      loadData();
    },
  });
}

// ============ 执行历史（⑫） ============

const historyVisible = ref(false);
const historyLoading = ref(false);
const historyJob = ref<JobItem | null>(null);
const historyLogs = ref<JobLogItem[]>([]);
const historyPagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showTotal: (t: number) => `共 ${t} 条`,
});

const historyColumns = [
  { title: '开始时间', dataIndex: 'startTime', key: 'startTime', width: 170 },
  { title: '结果', key: 'success', width: 80 },
  { title: '耗时', dataIndex: 'durationMs', key: 'durationMs', width: 90 },
  { title: '重试序号', dataIndex: 'retryIndex', key: 'retryIndex', width: 90 },
  { title: '失败原因', key: 'message', ellipsis: true },
];

async function openHistory(record: JobItem) {
  historyJob.value = record;
  historyVisible.value = true;
  historyPagination.current = 1;
  await fetchHistory();
}

async function fetchHistory() {
  if (!historyJob.value) return;
  historyLoading.value = true;
  try {
    const res = await getJobLogs(historyJob.value.id, {
      pageIndex: historyPagination.current,
      pageSize: historyPagination.pageSize,
    });
    historyLogs.value = res.items || [];
    historyPagination.total = res.total || 0;
  } finally {
    historyLoading.value = false;
  }
}

function onHistoryPageChange(pag: any) {
  historyPagination.current = pag.current;
  historyPagination.pageSize = pag.pageSize;
  fetchHistory();
}

onMounted(() => loadData());
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}

.batch-count {
  color: #1677ff;
}
</style>
