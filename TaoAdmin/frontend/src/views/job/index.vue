<template>
  <div class="page-container">
    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'job:add'" type="primary" @click="openForm()">
        <template #icon><PlusOutlined /></template>
        新增任务
      </a-button>
      <a-button style="margin-left: 8px" @click="loadData(1)">刷新</a-button>
    </div>

    <a-table
      :data-source="data"
      :columns="columns"
      :loading="loading"
      :pagination="pagination"
      row-key="id"
      size="middle"
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
        <a-form-item label="描述">
          <a-textarea v-model:value="form.description" :rows="2" placeholder="任务说明" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { PlusOutlined } from '@ant-design/icons-vue';
import {
  getJobPage,
  saveJob,
  deleteJob,
  pauseJob,
  resumeJob,
  runJobOnce,
  type JobItem,
} from '@/api/job';

const loading = ref(false);
const data = ref<JobItem[]>([]);

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
}>({ name: '', cron: '', handler: '' });

const formRules = {
  name: [{ required: true, message: '请输入任务名称', trigger: 'blur' }],
  cron: [{ required: true, message: '请输入 Cron 表达式', trigger: 'blur' }],
  handler: [{ required: true, message: '请输入处理器', trigger: 'blur' }],
};

function openForm(record?: JobItem) {
  if (record) {
    Object.assign(form, { ...record });
  } else {
    Object.assign(form, { id: undefined, name: '', group: '', cron: '', handler: '', params: '', description: '' });
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
  loadData();
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

onMounted(() => loadData());
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}
</style>
