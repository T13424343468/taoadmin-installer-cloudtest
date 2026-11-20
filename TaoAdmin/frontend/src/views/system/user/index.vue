<template>
  <div class="page-container">
    <!-- 搜索区 -->
    <FilterBar :values="query" :fields="filterFields" @search="loadData(1)" @reset="resetQuery" />

    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'sys:user:add'" type="primary" @click="openForm()">
        <template #icon><PlusOutlined /></template>
        新增用户
      </a-button>
      <a-space style="margin-left: 12px">
        <a-button :loading="exporting" @click="onExport">
          <template #icon><DownloadOutlined /></template>
          导出
        </a-button>
        <a-button v-permission="'sys:user:add'" @click="importVisible = true">
          <template #icon><UploadOutlined /></template>
          导入
        </a-button>
      </a-space>
    </div>

    <!-- 表格 -->
    <a-table
      :data-source="data"
      :columns="columns"
      :loading="loading"
      :pagination="pagination"
      :scroll="{ x: 730 }"
      row-key="id"
      size="middle"
      @change="onTableChange"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'status'">
          <a-tag :color="Number(record.status) === 1 ? 'green' : 'red'">
            {{ Number(record.status) === 1 ? '启用' : '禁用' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'roles'">
          <a-tag v-for="r in record.roleNames || []" :key="r" color="blue">{{ r }}</a-tag>
          <span v-if="!record.roleNames?.length" class="text-muted">未分配</span>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a v-permission="'sys:user:edit'" @click="openForm(record)">编辑</a>
            <a v-permission="'sys:user:assign-role'" @click="openRoleDrawer(record)">分配角色</a>
            <a v-permission="'sys:user:reset-pwd'" @click="openResetPwd(record)">重置密码</a>
            <a-popconfirm
              v-permission="'sys:user:delete'"
              title="确定删除该用户吗？"
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
      :title="form.id ? '编辑用户' : '新增用户'"
      :confirm-loading="submitting"
      @ok="onSubmit"
    >
      <a-form :model="form" :rules="formRules" layout="vertical">
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="用户名" name="username">
              <a-input v-model:value="form.username" :disabled="!!form.id" placeholder="登录账号" />
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="昵称" name="nickname">
              <a-input v-model:value="form.nickname" placeholder="显示昵称" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item v-if="!form.id" label="初始密码" name="password">
          <a-input-password v-model:value="form.password" placeholder="初始密码" />
        </a-form-item>
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="邮箱" name="email">
              <a-input v-model:value="form.email" placeholder="邮箱" />
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="手机号" name="phone">
              <a-input v-model:value="form.phone" placeholder="手机号" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="状态" name="status">
          <a-radio-group v-model:value="form.status">
            <a-radio :value="1">启用</a-radio>
            <a-radio :value="0">禁用</a-radio>
          </a-radio-group>
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 分配角色抽屉 -->
    <a-drawer v-model:open="roleDrawerVisible" title="分配角色" width="420" @close="roleDrawerVisible = false">
      <a-checkbox-group v-model:value="selectedRoleIds" style="width: 100%">
        <a-checkbox v-for="role in allRoles" :key="role.id" :value="role.id" style="display: block; margin-bottom: 8px">
          {{ role.name }}（{{ role.code }}）
        </a-checkbox>
      </a-checkbox-group>
      <template #footer>
        <a-button type="primary" :loading="assigning" @click="onAssignRoles">保存</a-button>
        <a-button style="margin-left: 8px" @click="roleDrawerVisible = false">取消</a-button>
      </template>
    </a-drawer>

    <!-- 重置密码弹窗 -->
    <a-modal
      v-model:open="resetPwdVisible"
      title="重置密码"
      :confirm-loading="resetting"
      @ok="onResetPwd"
    >
      <a-form layout="vertical">
        <a-form-item label="用户">
          <a-input :value="resetPwdTarget?.username" disabled />
        </a-form-item>
        <a-form-item label="新密码" required>
          <a-input-password v-model:value="newPassword" placeholder="请输入新密码" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 导入用户弹窗 -->
    <a-modal v-model:open="importVisible" title="导入用户" :footer="null" width="560">
      <a-alert
        type="info"
        show-icon
        message="先下载模板填写，再上传导入；逐行校验，错误行会给出明细，不影响其余行。"
        style="margin-bottom: 12px"
      />
      <a-space style="margin-bottom: 16px">
        <a-button @click="onDownloadTemplate">
          <template #icon><DownloadOutlined /></template>
          下载模板
        </a-button>
        <a-upload
          accept=".xlsx"
          :show-upload-list="false"
          :before-upload="onImportUpload"
          :disabled="importing"
        >
          <a-button type="primary" :loading="importing">
            <template #icon><UploadOutlined /></template>
            上传 xlsx 导入
          </a-button>
        </a-upload>
      </a-space>

      <template v-if="importResult">
        <a-result
          :status="importResult.errors.length ? 'warning' : 'success'"
          :title="`共 ${importResult.total} 行，成功 ${importResult.success} 条`"
          :sub-title="importResult.errors.length ? `失败 ${importResult.errors.length} 条，明细如下` : '全部导入成功'"
          style="padding: 8px 0"
        />
        <a-table
          v-if="importResult.errors.length"
          :data-source="importResult.errors"
          :columns="importErrorColumns"
          row-key="row"
          size="small"
          :pagination="false"
          :scroll="{ y: 200 }"
        />
      </template>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { message } from 'ant-design-vue';
import { PlusOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons-vue';
import FilterBar, { type FilterField } from '@/components/FilterBar/index.vue';
import {
  getUserPage,
  saveUser,
  deleteUser,
  resetUserPassword,
  assignUserRoles,
  getAllRoles,
  type UserItem,
  type RoleItem,
} from '@/api/system';
import { exportUsers, downloadUserTemplate, importUsers, type UserImportResult } from '@/api/userExcel';

const loading = ref(false);
const data = ref<UserItem[]>([]);
const allRoles = ref<RoleItem[]>([]);

const query = reactive<{ username?: string; status?: number; pageIndex: number; pageSize: number }>({
  pageIndex: 1,
  pageSize: 10,
});

// 筛选项：用户名是核心条件默认显示，状态折叠到展开区
const filterFields: FilterField[] = [
  { key: 'username', label: '用户名', type: 'input', primary: true, placeholder: '请输入用户名' },
  { key: 'status', label: '状态', type: 'select', placeholder: '全部', options: [
    { label: '启用', value: 1 },
    { label: '禁用', value: 0 },
  ] },
];

const pagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showQuickJumper: true,
  showTotal: (t: number) => `共 ${t} 条`,
});

const columns = [
  { title: '用户名', dataIndex: 'username', key: 'username', width: 140 },
  { title: '昵称', dataIndex: 'nickname', key: 'nickname', width: 140 },
  { title: '邮箱', dataIndex: 'email', key: 'email', width: 200 },
  { title: '手机号', dataIndex: 'phone', key: 'phone', width: 140 },
  { title: '角色', key: 'roles', width: 200 },
  { title: '状态', key: 'status', width: 80 },
  { title: '创建时间', dataIndex: 'createdAt', key: 'createdAt', width: 170 },
  { title: '操作', key: 'action', width: 260, fixed: 'right' as const },
];

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) {
      query.pageIndex = pageIndex;
      pagination.current = pageIndex;
    }
    const res = await getUserPage({ ...query });
    data.value = res.items || [];
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
  query.username = undefined;
  query.status = undefined;
  loadData(1);
}

// ============ 新增/编辑 ============

const formVisible = ref(false);
const submitting = ref(false);
const form = reactive<{
  id?: string;
  username: string;
  nickname?: string;
  email?: string;
  phone?: string;
  password?: string;
  status: number;
}>({ username: '', status: 1 });

const formRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入初始密码', trigger: 'blur' }],
};

function openForm(record?: UserItem) {
  if (record) {
    Object.assign(form, {
      id: record.id,
      username: record.username,
      nickname: record.nickname,
      email: record.email,
      phone: record.phone,
      status: Number(record.status) || 0,
    });
  } else {
    Object.assign(form, { id: undefined, username: '', nickname: '', email: '', phone: '', password: '', status: 1 });
  }
  formVisible.value = true;
}

async function onSubmit() {
  submitting.value = true;
  try {
    await saveUser({ ...form });
    message.success(form.id ? '更新成功' : '新增成功');
    formVisible.value = false;
    loadData();
  } finally {
    submitting.value = false;
  }
}

async function onDelete(record: UserItem) {
  await deleteUser(record.id);
  message.success('删除成功');
  loadData();
}

// ============ 分配角色 ============

const roleDrawerVisible = ref(false);
const assigning = ref(false);
const selectedRoleIds = ref<string[]>([]);
const roleTarget = ref<UserItem | null>(null);

function openRoleDrawer(record: UserItem) {
  roleTarget.value = record;
  selectedRoleIds.value = record.roleIds ? [...record.roleIds] : [];
  roleDrawerVisible.value = true;
}

async function onAssignRoles() {
  if (!roleTarget.value) return;
  assigning.value = true;
  try {
    await assignUserRoles(roleTarget.value.id, selectedRoleIds.value);
    message.success('角色分配成功');
    roleDrawerVisible.value = false;
    loadData();
  } finally {
    assigning.value = false;
  }
}

// ============ 重置密码 ============

const resetPwdVisible = ref(false);
const resetting = ref(false);
const resetPwdTarget = ref<UserItem | null>(null);
const newPassword = ref('');

function openResetPwd(record: UserItem) {
  resetPwdTarget.value = record;
  newPassword.value = '';
  resetPwdVisible.value = true;
}

async function onResetPwd() {
  if (!resetPwdTarget.value || !newPassword.value) {
    message.warning('请输入新密码');
    return;
  }
  resetting.value = true;
  try {
    await resetUserPassword(resetPwdTarget.value.id, newPassword.value);
    message.success('密码重置成功');
    resetPwdVisible.value = false;
  } finally {
    resetting.value = false;
  }
}

// ============ 导入导出 ============

const exporting = ref(false);

async function onExport() {
  exporting.value = true;
  try {
    await exportUsers(query.username);
    message.success('导出成功，浏览器已开始下载');
  } catch {
    // 错误提示由封装统一给出
  } finally {
    exporting.value = false;
  }
}

const importVisible = ref(false);
const importing = ref(false);
const importResult = ref<UserImportResult | null>(null);

const importErrorColumns = [
  { title: '行号', dataIndex: 'row', key: 'row', width: 70 },
  { title: '失败原因', dataIndex: 'reason', key: 'reason' },
];

async function onDownloadTemplate() {
  try {
    await downloadUserTemplate();
    message.success('模板已开始下载');
  } catch {
    // 错误提示由封装统一给出
  }
}

/** a-upload before-upload：接管上传，返回 false 阻断默认行为 */
async function onImportUpload(file: File): Promise<boolean> {
  if (!file.name.toLowerCase().endsWith('.xlsx')) {
    message.error('请选择 .xlsx 文件（模板即 xlsx 格式）');
    return false;
  }
  if (file.size > 5 * 1024 * 1024) {
    message.error('导入文件不超过 5MB');
    return false;
  }
  importing.value = true;
  importResult.value = null;
  try {
    importResult.value = await importUsers(file);
    loadData();
  } catch {
    // 错误提示由封装统一给出
  } finally {
    importing.value = false;
  }
  return false;
}

onMounted(async () => {
  loadData();
  allRoles.value = (await getAllRoles().catch(() => [])) || [];
});
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}
</style>
