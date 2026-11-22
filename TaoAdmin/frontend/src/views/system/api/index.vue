<template>
  <div class="page-container">
    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'sys:api:add'" type="primary" @click="openForm()">
        <template #icon><PlusOutlined /></template>
        新增接口
      </a-button>
      <a-button style="margin-left: 8px" @click="loadData(1)">刷新</a-button>
      <a-space v-if="selectedCount > 0" style="margin-left: 12px">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button v-permission="'sys:api:delete'" danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
        <a @click="clearSelection">清空勾选</a>
      </a-space>
      <a-button
        v-else-if="pagination.total > 0"
        style="margin-left: 12px"
        @click="onSelectAll"
      >选中全部 {{ pagination.total }} 条</a-button>
    </div>

    <!-- 表格 -->
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
        <template v-if="column.key === 'method'">
          <a-tag :color="methodColor(record.method)">{{ record.method }}</a-tag>
        </template>
        <template v-else-if="column.key === 'path'">
          <code>{{ record.path }}</code>
        </template>
        <template v-else-if="column.key === 'status'">
          <a-tag :color="Number(record.status) === 1 ? 'green' : 'red'">
            {{ Number(record.status) === 1 ? '启用' : '禁用' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a v-permission="'sys:api:edit'" @click="openForm(record)">编辑</a>
            <a-popconfirm
              v-permission="'sys:api:delete'"
              title="确定删除该接口吗？"
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
      :title="form.id ? '编辑接口' : '新增接口'"
      :confirm-loading="submitting"
      @ok="onSubmit"
    >
      <a-form :model="form" :rules="formRules" layout="vertical">
        <a-form-item label="接口名称" name="name">
          <a-input v-model:value="form.name" placeholder="如：查询用户列表" />
        </a-form-item>
        <a-row :gutter="16">
          <a-col :span="8">
            <a-form-item label="请求方法" name="method">
              <a-select v-model:value="form.method">
                <a-select-option value="GET">GET</a-select-option>
                <a-select-option value="POST">POST</a-select-option>
                <a-select-option value="PUT">PUT</a-select-option>
                <a-select-option value="DELETE">DELETE</a-select-option>
                <a-select-option value="PATCH">PATCH</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
          <a-col :span="16">
            <a-form-item label="请求路径" name="path">
              <a-input v-model:value="form.path" placeholder="如 /identity/users" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="所属服务" name="service">
          <a-input v-model:value="form.service" placeholder="如 identity-service" />
        </a-form-item>
        <a-form-item label="描述">
          <a-textarea v-model:value="form.description" :rows="3" placeholder="接口描述" />
        </a-form-item>
        <a-form-item label="状态" name="status">
          <a-radio-group v-model:value="form.status">
            <a-radio :value="1">启用</a-radio>
            <a-radio :value="0">禁用</a-radio>
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
import { pageAfterDelete } from '@/utils/listPage';
import {
  getApiPage,
  saveApi,
  deleteApi,
  batchDeleteApis,
  type ApiResourceItem,
} from '@/api/system';

const loading = ref(false);
const data = ref<ApiResourceItem[]>([]);

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
  const res = await getApiPage({ pageIndex: 1, pageSize: 10000 });
  return (res.items || []).map((a) => a.id);
});

async function onSelectAll() {
  await selectAll();
  message.info(`已选中全部 ${selectedCount.value} 条接口`);
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
  { title: '接口名称', dataIndex: 'name', key: 'name', width: 180 },
  { title: '方法', key: 'method', width: 90 },
  { title: '路径', key: 'path', width: 260 },
  { title: '所属服务', dataIndex: 'service', key: 'service', width: 160 },
  { title: '描述', dataIndex: 'description', key: 'description', ellipsis: true },
  { title: '状态', key: 'status', width: 80 },
  { title: '操作', key: 'action', width: 140, fixed: 'right' as const },
];

function methodColor(method: string): string {
  const map: Record<string, string> = {
    GET: 'blue',
    POST: 'green',
    PUT: 'orange',
    DELETE: 'red',
    PATCH: 'purple',
  };
  return map[method?.toUpperCase()] || 'default';
}

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) pagination.current = pageIndex;
    const res = await getApiPage({ pageIndex: pagination.current, pageSize: pagination.pageSize });
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
  method: string;
  path: string;
  service?: string;
  description?: string;
  status: number;
}>({ name: '', method: 'GET', path: '', status: 1 });

const formRules = {
  name: [{ required: true, message: '请输入接口名称', trigger: 'blur' }],
  path: [{ required: true, message: '请输入请求路径', trigger: 'blur' }],
};

function openForm(record?: ApiResourceItem) {
  if (record) {
    Object.assign(form, { ...record });
  } else {
    Object.assign(form, { id: undefined, name: '', method: 'GET', path: '', service: '', description: '', status: 1 });
  }
  formVisible.value = true;
}

async function onSubmit() {
  submitting.value = true;
  try {
    await saveApi({ ...form });
    message.success(form.id ? '更新成功' : '新增成功');
    formVisible.value = false;
    loadData();
  } finally {
    submitting.value = false;
  }
}

async function onDelete(record: ApiResourceItem) {
  await deleteApi(record.id);
  message.success('删除成功');
  loadData(pageAfterDelete(pagination.total, 1, pagination.current, pagination.pageSize));
}

// ============ 批量删除 ============

function onBatchDelete() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除接口',
    content: `确定删除选中的 ${ids.length} 个接口吗？其角色授权关联会一并清理。`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const res = await batchDeleteApis(ids);
        message.success(`删除完成：成功 ${res.deleted} 条`);
        clearSelection();
        loadData(pageAfterDelete(pagination.total, res.deleted, pagination.current, pagination.pageSize));
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
