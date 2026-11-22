<template>
  <div class="page-container">
    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'sys:tenant:manage'" type="primary" @click="openSave()">
        <template #icon><PlusOutlined /></template>
        新增租户
      </a-button>
      <a-space v-if="selectedCount > 0" style="margin-left: 12px">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button v-permission="'sys:tenant:manage'" danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
        <a @click="clearSelection">清空勾选</a>
      </a-space>
    </div>

    <!-- 表格 -->
    <a-table
      :data-source="data"
      :columns="columns"
      :loading="loading"
      :pagination="false"
      row-key="id"
      size="middle"
      :row-selection="{ selectedRowKeys, onChange: onTableSelectChange }"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'status'">
          <a-tag :color="Number(record.status) === 1 ? 'green' : 'red'">
            {{ Number(record.status) === 1 ? '启用' : '停用' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a v-permission="'sys:tenant:manage'" @click="openSave(record)">编辑</a>
            <a-popconfirm
              v-permission="'sys:tenant:manage'"
              :title="Number(record.status) === 1 ? '确定停用该租户吗？' : '确定启用该租户吗？'"
              @confirm="onToggleStatus(record)"
            >
              <a v-permission="'sys:tenant:manage'">{{ Number(record.status) === 1 ? '停用' : '启用' }}</a>
            </a-popconfirm>
            <a-popconfirm
              v-permission="'sys:tenant:manage'"
              title="确定删除该租户吗？"
              @confirm="onDelete(record)"
            >
              <a class="danger-link" v-permission="'sys:tenant:manage'">删除</a>
            </a-popconfirm>
          </a-space>
        </template>
      </template>
    </a-table>

    <!-- 新增/编辑弹窗 -->
    <a-modal
      v-model:open="saveVisible"
      :title="saveForm.id ? '编辑租户' : '新增租户'"
      :confirm-loading="saving"
      @ok="onSave"
    >
      <a-form :model="saveForm" :rules="saveRules" layout="vertical">
        <a-form-item label="租户名称" name="tenantName">
          <a-input v-model:value="saveForm.tenantName" :maxlength="50" show-count placeholder="如：华东分公司" />
        </a-form-item>
        <a-form-item label="租户编码" name="tenantCode" extra="唯一标识，创建后不可修改">
          <a-input v-model:value="saveForm.tenantCode" :maxlength="50" :disabled="!!saveForm.id" placeholder="如：east" />
        </a-form-item>
        <a-form-item label="联系人" name="contact">
          <a-input v-model:value="saveForm.contact" :maxlength="50" placeholder="租户联系人（选填）" />
        </a-form-item>
        <a-form-item label="备注" name="remark">
          <a-textarea v-model:value="saveForm.remark" :rows="3" :maxlength="200" placeholder="租户备注（选填）" />
        </a-form-item>
        <a-form-item label="状态" name="status">
          <a-radio-group v-model:value="saveForm.status">
            <a-radio :value="1">启用</a-radio>
            <a-radio :value="0">停用</a-radio>
          </a-radio-group>
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { PlusOutlined } from '@ant-design/icons-vue';
import { useBatchSelection } from '@/composables/useBatchSelection';
import { getTenantList, saveTenant, deleteTenant, batchDeleteTenants, type TenantItem } from '@/api/tenant';

const loading = ref(false);
const data = ref<TenantItem[]>([]);

// ============ 批量选择 ============

const batchLoading = ref(false);
const {
  selectedRowKeys,
  selectedCount,
  onTableSelectChange,
  clearSelection,
  getSelectedIds,
} = useBatchSelection(async () => data.value.map((t) => t.id));

const columns = [
  { title: '租户名称', dataIndex: 'tenantName', key: 'tenantName' },
  { title: '租户编码', dataIndex: 'tenantCode', key: 'tenantCode', width: 140 },
  { title: '联系人', dataIndex: 'contact', key: 'contact', width: 120 },
  { title: '备注', dataIndex: 'remark', key: 'remark', ellipsis: true },
  { title: '状态', key: 'status', width: 90 },
  { title: '操作', key: 'action', width: 120, fixed: 'right' as const },
];

async function loadData() {
  loading.value = true;
  try {
    data.value = (await getTenantList()) || [];
  } finally {
    loading.value = false;
  }
}

// ============ 新增/编辑/停启用 ============

const saveVisible = ref(false);
const saving = ref(false);
const saveForm = reactive<{
  id?: string;
  tenantName: string;
  tenantCode: string;
  contact?: string;
  remark?: string;
  status: number;
}>({ tenantName: '', tenantCode: '', contact: '', remark: '', status: 1 });

const saveRules = {
  tenantName: [
    { required: true, message: '请输入租户名称', trigger: 'blur' },
    { max: 50, message: '租户名称不超过 50 个字符', trigger: 'blur' },
  ],
  tenantCode: [
    { required: true, message: '请输入租户编码', trigger: 'blur' },
    { max: 50, message: '租户编码不超过 50 个字符', trigger: 'blur' },
  ],
};

function openSave(record?: TenantItem) {
  if (record) {
    saveForm.id = record.id;
    saveForm.tenantName = record.tenantName;
    saveForm.tenantCode = record.tenantCode;
    saveForm.contact = record.contact || '';
    saveForm.remark = record.remark || '';
    saveForm.status = record.status;
  } else {
    saveForm.id = undefined;
    saveForm.tenantName = '';
    saveForm.tenantCode = '';
    saveForm.contact = '';
    saveForm.remark = '';
    saveForm.status = 1;
  }
  saveVisible.value = true;
}

async function onSave() {
  saving.value = true;
  try {
    await saveTenant({ ...saveForm });
    message.success(saveForm.id ? '修改成功' : '新增成功');
    saveVisible.value = false;
    loadData();
  } finally {
    saving.value = false;
  }
}

async function onToggleStatus(record: TenantItem) {
  await saveTenant({
    id: record.id,
    tenantName: record.tenantName,
    tenantCode: record.tenantCode,
    contact: record.contact,
    remark: record.remark,
    status: Number(record.status) === 1 ? 0 : 1,
  });
  message.success(Number(record.status) === 1 ? '已停用' : '已启用');
  loadData();
}

async function onDelete(record: TenantItem) {
  await deleteTenant(record.id);
  message.success('删除成功');
  loadData();
}

// ============ 批量删除 ============

function onBatchDelete() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除租户',
    content: `确定删除选中的 ${ids.length} 个租户吗？`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const res = await batchDeleteTenants(ids);
        message.success(`删除完成：成功 ${res.deleted} 条`);
        clearSelection();
        loadData();
      } finally {
        batchLoading.value = false;
      }
    },
  });
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
