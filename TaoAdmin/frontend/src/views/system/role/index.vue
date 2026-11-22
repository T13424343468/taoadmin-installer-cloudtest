<template>
  <div class="page-container">
    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'sys:role:add'" type="primary" @click="openForm()">
        <template #icon><PlusOutlined /></template>
        新增角色
      </a-button>
      <a-space v-if="selectedCount > 0" style="margin-left: 12px">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button v-permission="'sys:role:delete'" danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
        <a @click="clearSelection">清空勾选</a>
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
          <a-tag :color="Number(record.status) === 1 ? 'green' : 'red'">
            {{ Number(record.status) === 1 ? '启用' : '禁用' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a v-permission="'sys:role:edit'" @click="openForm(record)">编辑</a>
            <a v-permission="'sys:role:assign'" @click="openPermissionDrawer(record)">分配权限</a>
            <a-popconfirm
              v-permission="'sys:role:delete'"
              title="确定删除该角色吗？"
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
      :title="form.id ? '编辑角色' : '新增角色'"
      :confirm-loading="submitting"
      @ok="onSubmit"
    >
      <a-form :model="form" :rules="formRules" layout="vertical">
        <a-form-item label="角色名称" name="name">
          <a-input v-model:value="form.name" placeholder="如：系统管理员" />
        </a-form-item>
        <a-form-item label="角色编码" name="code">
          <a-input v-model:value="form.code" :disabled="!!form.id" placeholder="如：sys-admin" />
        </a-form-item>
        <a-form-item label="描述">
          <a-textarea v-model:value="form.description" :rows="3" placeholder="角色描述" />
        </a-form-item>
        <a-form-item label="状态" name="status">
          <a-radio-group v-model:value="form.status">
            <a-radio :value="1">启用</a-radio>
            <a-radio :value="0">禁用</a-radio>
          </a-radio-group>
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 分配权限抽屉：左侧菜单树勾选（控制显示隐藏）+ 右侧接口权限列表勾选 -->
    <a-drawer
      v-model:open="permDrawerVisible"
      :title="`分配权限 - ${permTarget?.name || ''}`"
      width="860"
      @close="permDrawerVisible = false"
    >
      <a-row :gutter="16">
        <!-- 左侧：菜单树 -->
        <a-col :span="12">
          <a-card title="菜单权限" size="small">
            <a-tree
              v-model:checkedKeys="checkedMenuIds"
              :tree-data="menuTreeData"
              checkable
              :default-expand-all="true"
              :field-names="{ title: 'name', key: 'id', children: 'children' }"
            />
          </a-card>
        </a-col>

        <!-- 右侧：接口权限 -->
        <a-col :span="12">
          <a-card title="接口权限" size="small">
            <a-input-search v-model:value="apiKeyword" placeholder="搜索接口" style="margin-bottom: 8px" allow-clear />
            <div style="max-height: 500px; overflow-y: auto">
              <a-checkbox
                :checked="isAllApiChecked"
                :indeterminate="isApiIndeterminate"
                style="margin-bottom: 8px"
                @change="onCheckAllApi"
              >
                全选
              </a-checkbox>
              <a-checkbox-group v-model:value="checkedApiIds" style="width: 100%">
                <div v-for="api in filteredApis" :key="api.id" style="display: block; margin-bottom: 6px">
                  <a-checkbox :value="api.id">
                    <a-tag :color="methodColor(api.method)" style="margin-right: 4px">{{ api.method }}</a-tag>
                    <code>{{ api.path }}</code>
                    <span class="text-muted">（{{ api.name }}）</span>
                  </a-checkbox>
                </div>
              </a-checkbox-group>
              <a-empty v-if="filteredApis.length === 0" description="无匹配接口" />
            </div>
          </a-card>
        </a-col>
      </a-row>

      <template #footer>
        <a-button type="primary" :loading="assigning" @click="onAssignPermissions">保存</a-button>
        <a-button style="margin-left: 8px" @click="permDrawerVisible = false">取消</a-button>
      </template>
    </a-drawer>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { PlusOutlined } from '@ant-design/icons-vue';
import { useBatchSelection } from '@/composables/useBatchSelection';
import { pageAfterDelete } from '@/utils/listPage';
import {
  getRolePage,
  saveRole,
  deleteRole,
  batchDeleteRoles,
  assignRolePermissions,
  getRolePermissions,
  getMenuTree,
  getAllApis,
  type RoleItem,
  type MenuRecord,
  type ApiResourceItem,
} from '@/api/system';

const loading = ref(false);
const data = ref<RoleItem[]>([]);

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
  const res = await getRolePage({ pageIndex: 1, pageSize: 10000 });
  return (res.items || []).map((r) => r.id);
});

async function onSelectAll() {
  await selectAll();
  message.info(`已选中全部 ${selectedCount.value} 条角色`);
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
  { title: '角色名称', dataIndex: 'name', key: 'name', width: 160 },
  { title: '角色编码', dataIndex: 'code', key: 'code', width: 160 },
  { title: '描述', dataIndex: 'description', key: 'description', ellipsis: true },
  { title: '状态', key: 'status', width: 80 },
  { title: '创建时间', dataIndex: 'createdAt', key: 'createdAt', width: 170 },
  { title: '操作', key: 'action', width: 220, fixed: 'right' as const },
];

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) pagination.current = pageIndex;
    const res = await getRolePage({ pageIndex: pagination.current, pageSize: pagination.pageSize });
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
const form = reactive<{ id?: string; name: string; code: string; description?: string; status: number }>({
  name: '',
  code: '',
  status: 1,
});

const formRules = {
  name: [{ required: true, message: '请输入角色名称', trigger: 'blur' }],
  code: [{ required: true, message: '请输入角色编码', trigger: 'blur' }],
};

function openForm(record?: RoleItem) {
  if (record) {
    Object.assign(form, { id: record.id, name: record.name, code: record.code, description: record.description, status: Number(record.status) || 0 });
  } else {
    Object.assign(form, { id: undefined, name: '', code: '', description: '', status: 1 });
  }
  formVisible.value = true;
}

async function onSubmit() {
  submitting.value = true;
  try {
    await saveRole({ ...form });
    message.success(form.id ? '更新成功' : '新增成功');
    formVisible.value = false;
    loadData();
  } finally {
    submitting.value = false;
  }
}

async function onDelete(record: RoleItem) {
  await deleteRole(record.id);
  message.success('删除成功');
  loadData(pageAfterDelete(pagination.total, 1, pagination.current, pagination.pageSize));
}

// ============ 批量删除 ============

function onBatchDelete() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除角色',
    content: `确定删除选中的 ${ids.length} 个角色吗？其授权关系会一并清理。`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const res = await batchDeleteRoles(ids);
        message.success(`删除完成：成功 ${res.deleted} 条`);
        clearSelection();
        loadData(pageAfterDelete(pagination.total, res.deleted, pagination.current, pagination.pageSize));
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

// ============ 分配权限 ============

const permDrawerVisible = ref(false);
const assigning = ref(false);
const permTarget = ref<RoleItem | null>(null);

const menuTreeData = ref<MenuRecord[]>([]);
const checkedMenuIds = ref<string[]>([]);
const allApis = ref<ApiResourceItem[]>([]);
const checkedApiIds = ref<string[]>([]);
const apiKeyword = ref('');

const filteredApis = computed(() =>
  allApis.value.filter(
    (a) =>
      !apiKeyword.value ||
      a.path.toLowerCase().includes(apiKeyword.value.toLowerCase()) ||
      a.name.toLowerCase().includes(apiKeyword.value.toLowerCase())
  )
);

const isAllApiChecked = computed(
  () => filteredApis.value.length > 0 && filteredApis.value.every((a) => checkedApiIds.value.includes(a.id))
);
const isApiIndeterminate = computed(
  () =>
    filteredApis.value.some((a) => checkedApiIds.value.includes(a.id)) && !isAllApiChecked.value
);

function onCheckAllApi(e: any) {
  const checked = e.target.checked;
  const ids = filteredApis.value.map((a) => a.id);
  if (checked) {
    checkedApiIds.value = Array.from(new Set([...checkedApiIds.value, ...ids]));
  } else {
    checkedApiIds.value = checkedApiIds.value.filter((id) => !ids.includes(id));
  }
}

function methodColor(method: string): string {
  const map: Record<string, string> = {
    GET: 'blue',
    POST: 'green',
    PUT: 'orange',
    DELETE: 'red',
  };
  return map[method?.toUpperCase()] || 'default';
}

function openPermissionDrawer(record: RoleItem) {
  permTarget.value = record;
  permDrawerVisible.value = true;
  // 加载已有权限
  getRolePermissions(record.id)
    .then((perm) => {
      checkedMenuIds.value = perm.menuIds || [];
      checkedApiIds.value = perm.apiIds || [];
    })
    .catch(() => {
      checkedMenuIds.value = [];
      checkedApiIds.value = [];
    });
}

async function onAssignPermissions() {
  if (!permTarget.value) return;
  assigning.value = true;
  try {
    await assignRolePermissions(permTarget.value.id, {
      menuIds: checkedMenuIds.value,
      apiIds: checkedApiIds.value,
    });
    message.success('权限分配成功');
    permDrawerVisible.value = false;
  } finally {
    assigning.value = false;
  }
}

onMounted(async () => {
  loadData();
  menuTreeData.value = (await getMenuTree().catch(() => [])) || [];
  allApis.value = (await getAllApis().catch(() => [])) || [];
});
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}

.batch-count {
  color: #1677ff;
}
</style>
