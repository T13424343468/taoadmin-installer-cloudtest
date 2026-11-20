<template>
  <div class="page-container">
    <a-tabs v-model:activeKey="activeTab" @change="onTabChange">
      <!-- 页签一：流程定义 -->
      <a-tab-pane key="definitions" tab="流程定义">
        <a-table
          :data-source="definitions"
          :columns="defColumns"
          :loading="loading"
          :pagination="false"
          row-key="id"
          size="middle"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'nodes'">
              <a-tag v-for="(n, i) in record.nodes" :key="String(i)" style="margin-right: 4px">
                {{ Number(i) + 1 }}. {{ n.name }}（{{ n.approverRole }}）
              </a-tag>
            </template>
            <template v-else-if="column.key === 'action'">
              <a @click="openSubmit(record)">发起审批</a>
            </template>
          </template>
        </a-table>
      </a-tab-pane>

      <!-- 页签二：流程实例 -->
      <a-tab-pane key="instances" tab="流程实例">
        <a-table
          :data-source="instances"
          :columns="instanceColumns"
          :loading="loading"
          :pagination="instancePagination"
          row-key="id"
          size="middle"
          @change="onInstanceTableChange"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'statusText'">
              <a-tag :color="Number(record.status) === 2 ? 'green' : Number(record.status) === 3 ? 'red' : 'blue'">
                {{ record.statusText }}
              </a-tag>
            </template>
            <template v-else-if="column.key === 'action'">
              <a @click="openDetail(record)">详情</a>
            </template>
          </template>
        </a-table>
      </a-tab-pane>

      <!-- 页签三：我的待办 -->
      <a-tab-pane key="tasks" tab="我的待办">
        <a-table
          :data-source="tasks"
          :columns="taskColumns"
          :loading="loading"
          :pagination="false"
          row-key="id"
          size="middle"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'action'">
              <a-space>
                <a @click="openApprove(record, true)">通过</a>
                <a class="danger-link" @click="openApprove(record, false)">驳回</a>
              </a-space>
            </template>
          </template>
        </a-table>
      </a-tab-pane>
    </a-tabs>

    <!-- 发起审批弹窗 -->
    <a-modal v-model:open="submitVisible" title="发起审批" :confirm-loading="submitting" @ok="onSubmit">
      <a-form :model="submitForm" :rules="submitRules" layout="vertical">
        <a-form-item label="流程" name="workflowCode">
          <a-select v-model:value="submitForm.workflowCode" :options="defOptions" placeholder="选择流程" />
        </a-form-item>
        <a-form-item label="业务标题" name="bizTitle">
          <a-input v-model:value="submitForm.bizTitle" :maxlength="100" show-count placeholder="如：张三的请假申请" />
        </a-form-item>
        <a-form-item label="备注" name="remark">
          <a-textarea v-model:value="submitForm.remark" :rows="3" :maxlength="200" placeholder="申请说明（选填）" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 实例详情弹窗 -->
    <a-modal v-model:open="detailVisible" :title="detail?.instance?.bizTitle || '流程详情'" :footer="null" width="640">
      <template v-if="detail">
        <div class="detail-meta">
          <a-tag :color="Number(detail.instance.status) === 2 ? 'green' : Number(detail.instance.status) === 3 ? 'red' : 'blue'">
            {{ detail.instance.statusText }}
          </a-tag>
          <span>{{ detail.instance.workflowName }}</span>
          <span class="detail-time">{{ detail.instance.applicantName }} 发起于 {{ detail.instance.createdAt }}</span>
        </div>
        <div v-if="detail.instance.remark" class="detail-remark">{{ detail.instance.remark }}</div>
        <a-steps direction="vertical" size="small" :current="-1" class="detail-steps">
          <a-step
            v-for="t in detail.tasks"
            :key="t.id"
            :title="t.nodeName"
            :description="taskDescription(t)"
          >
            <template #icon>
              <span class="step-status" :class="`step-status-${t.status}`">
                {{ Number(t.status) === 1 ? '✓' : Number(t.status) === 2 ? '✕' : '…' }}
              </span>
            </template>
          </a-step>
        </a-steps>
      </template>
    </a-modal>

    <!-- 审批弹窗 -->
    <a-modal
      v-model:open="approveVisible"
      :title="approveForm.approved ? '审批通过' : '审批驳回'"
      :confirm-loading="approving"
      @ok="onApprove"
    >
      <div class="approve-task-title">
        {{ approveTaskRef?.bizTitle }} · {{ approveTaskRef?.nodeName }}（{{ approveTaskRef?.applicantName }}）
      </div>
      <a-textarea v-model:value="approveForm.comment" :rows="3" :maxlength="200" placeholder="审批意见（选填）" />
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import { message } from 'ant-design-vue';
import {
  getWorkflowDefinitions,
  submitWorkflow,
  getWorkflowInstances,
  getWorkflowDetail,
  getMyTasks,
  approveTask,
  type WorkflowDef,
  type WorkflowInstance,
  type WorkflowTask,
} from '@/api/workflow';

const activeTab = ref('definitions');
const loading = ref(false);

const definitions = ref<WorkflowDef[]>([]);
const instances = ref<WorkflowInstance[]>([]);
const tasks = ref<WorkflowTask[]>([]);

const defColumns = [
  { title: '流程名称', dataIndex: 'workflowName', key: 'workflowName', width: 140 },
  { title: '编码', dataIndex: 'workflowCode', key: 'workflowCode', width: 140 },
  { title: '说明', dataIndex: 'description', key: 'description', ellipsis: true },
  { title: '审批节点', key: 'nodes' },
  { title: '操作', key: 'action', width: 100, fixed: 'right' as const },
];

const instancePagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showTotal: (t: number) => `共 ${t} 条`,
});

const instanceColumns = [
  { title: '业务标题', dataIndex: 'bizTitle', key: 'bizTitle' },
  { title: '流程', dataIndex: 'workflowName', key: 'workflowName', width: 120 },
  { title: '发起人', dataIndex: 'applicantName', key: 'applicantName', width: 120 },
  { title: '当前节点', dataIndex: 'currentNode', key: 'currentNode', width: 120 },
  { title: '状态', key: 'statusText', width: 90 },
  { title: '发起时间', dataIndex: 'createdAt', key: 'createdAt', width: 170 },
  { title: '操作', key: 'action', width: 80, fixed: 'right' as const },
];

const taskColumns = [
  { title: '业务标题', dataIndex: 'bizTitle', key: 'bizTitle' },
  { title: '流程', dataIndex: 'workflowName', key: 'workflowName', width: 120 },
  { title: '发起人', dataIndex: 'applicantName', key: 'applicantName', width: 120 },
  { title: '节点', dataIndex: 'nodeName', key: 'nodeName', width: 120 },
  { title: '到达时间', dataIndex: 'createdAt', key: 'createdAt', width: 170 },
  { title: '操作', key: 'action', width: 110, fixed: 'right' as const },
];

async function loadTab(tab: string) {
  loading.value = true;
  try {
    if (tab === 'definitions') {
      definitions.value = (await getWorkflowDefinitions()) || [];
    } else if (tab === 'instances') {
      const res = await getWorkflowInstances({ pageIndex: instancePagination.current, pageSize: instancePagination.pageSize });
      instances.value = res.items || [];
      instancePagination.total = Number(res.total) || 0;
    } else if (tab === 'tasks') {
      tasks.value = (await getMyTasks()) || [];
    }
  } finally {
    loading.value = false;
  }
}

function onTabChange(tab: string) {
  loadTab(tab);
}

function onInstanceTableChange(pag: any) {
  instancePagination.current = pag.current;
  instancePagination.pageSize = pag.pageSize;
  loadTab('instances');
}

onMounted(() => loadTab(activeTab.value));

// ============ 发起审批 ============

const submitVisible = ref(false);
const submitting = ref(false);
const submitForm = reactive<{ workflowCode: string; bizTitle: string; remark?: string }>({
  workflowCode: '',
  bizTitle: '',
  remark: '',
});

const defOptions = computed(() =>
  definitions.value.map((d) => ({ label: d.workflowName, value: d.workflowCode }))
);

const submitRules = {
  workflowCode: [{ required: true, message: '请选择流程', trigger: 'change' }],
  bizTitle: [
    { required: true, message: '请输入业务标题', trigger: 'blur' },
    { max: 100, message: '业务标题不超过 100 个字符', trigger: 'blur' },
  ],
};

function openSubmit(def: WorkflowDef) {
  submitForm.workflowCode = def.workflowCode;
  submitForm.bizTitle = '';
  submitForm.remark = '';
  submitVisible.value = true;
}

async function onSubmit() {
  submitting.value = true;
  try {
    await submitWorkflow({ ...submitForm });
    message.success('审批已提交');
    submitVisible.value = false;
  } finally {
    submitting.value = false;
  }
}

// ============ 实例详情 ============

const detailVisible = ref(false);
const detail = ref<{ instance: WorkflowInstance; tasks: WorkflowTask[] } | null>(null);

async function openDetail(record: WorkflowInstance) {
  detail.value = await getWorkflowDetail(record.id);
  detailVisible.value = true;
}

function taskDescription(t: WorkflowTask): string {
  if (Number(t.status) === 0) return `等待处理 · 到达 ${t.createdAt}`;
  return `${t.handlerName || ''} ${Number(t.status) === 1 ? '通过' : '驳回'}${t.comment ? ` · ${t.comment}` : ''} · ${t.handleTime || ''}`;
}

// ============ 审批 ============

const approveVisible = ref(false);
const approving = ref(false);
const approveTaskRef = ref<WorkflowTask | null>(null);
const approveForm = reactive<{ approved: boolean; comment?: string }>({ approved: true, comment: '' });

function openApprove(task: WorkflowTask, approved: boolean) {
  approveTaskRef.value = task;
  approveForm.approved = approved;
  approveForm.comment = '';
  approveVisible.value = true;
}

async function onApprove() {
  if (!approveTaskRef.value) return;
  approving.value = true;
  try {
    await approveTask(approveTaskRef.value.id, { approved: approveForm.approved, comment: approveForm.comment });
    message.success(approveForm.approved ? '审批通过' : '已驳回');
    approveVisible.value = false;
    loadTab('tasks');
  } finally {
    approving.value = false;
  }
}
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}

.detail-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.detail-time {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}

.detail-remark {
  color: rgba(0, 0, 0, 0.65);
  margin-bottom: 12px;
  white-space: pre-wrap;
}

.detail-steps {
  margin-top: 8px;
}

.step-status {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  font-size: 12px;
  color: #fff;
}

.step-status-0 {
  background: #1677ff;
}

.step-status-1 {
  background: #52c41a;
}

.step-status-2 {
  background: #ff4d4f;
}
</style>
