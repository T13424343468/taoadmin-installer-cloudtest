<template>
  <div class="page-container">
    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'sys:menu:add'" type="primary" @click="openForm(null)">
        <template #icon><PlusOutlined /></template>
        新增菜单
      </a-button>
      <a-button style="margin-left: 8px" @click="expandedAll = !expandedAll">
        {{ expandedAll ? '折叠全部' : '展开全部' }}
      </a-button>
    </div>

    <!-- 树形表格：菜单 / 按钮 -->
    <a-table
      :data-source="menuTree"
      :columns="columns"
      :loading="loading"
      :pagination="false"
      row-key="id"
      size="middle"
      :default-expand-all-rows="false"
      :expanded-row-keys="expandedAll ? allRowKeys : expandedKeys"
      @expanded-rows-change="(keys: any) => (expandedKeys = keys)"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'name'">
          <component :is="record.icon || 'AppstoreOutlined'" v-if="record.icon" style="margin-right: 6px" />
          {{ record.name }}
        </template>
        <template v-else-if="column.key === 'type'">
          <a-tag :color="typeColor(record.type)">{{ typeText(record.type) }}</a-tag>
        </template>
        <template v-else-if="column.key === 'permission'">
          <code v-if="record.permission">{{ record.permission }}</code>
          <span v-else class="text-muted">-</span>
        </template>
        <template v-else-if="column.key === 'visible'">
          <!-- Visible=false 的菜单：不在侧边栏显示，但仍可通过 URL 访问（见 router 注释） -->
          <a-tag v-if="record.type !== 'button'" :color="record.visible ? 'green' : 'orange'">
            {{ record.visible ? '显示' : '隐藏' }}
          </a-tag>
          <span v-else class="text-muted">-</span>
        </template>
        <template v-else-if="column.key === 'status'">
          <a-tag :color="Number(record.status) === 1 ? 'green' : 'red'">
            {{ Number(record.status) === 1 ? '启用' : '禁用' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a v-permission="'sys:menu:add'" @click="openForm(null, record.id)">添加子项</a>
            <a v-permission="'sys:menu:edit'" @click="openForm(record)">编辑</a>
            <a-popconfirm
              v-permission="'sys:menu:delete'"
              title="删除后子项将一并失效，确定删除吗？"
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
      :title="form.id ? '编辑菜单' : '新增菜单'"
      :confirm-loading="submitting"
      @ok="onSubmit"
    >
      <a-form :model="form" :rules="formRules" layout="vertical">
        <a-form-item label="上级菜单">
          <a-tree-select
            v-model:value="form.parentId"
            :tree-data="parentOptions"
            placeholder="无（作为顶级）"
            allow-clear
            style="width: 100%"
            :field-names="{ label: 'name', value: 'id', children: 'children' }"
          />
        </a-form-item>
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="类型" name="type">
              <a-radio-group v-model:value="form.type" :disabled="!!form.id">
                <a-radio-button value="dir">目录</a-radio-button>
                <a-radio-button value="menu">菜单</a-radio-button>
                <a-radio-button value="button">按钮</a-radio-button>
              </a-radio-group>
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="排序" name="sort">
              <a-input-number v-model:value="form.sort" :min="0" style="width: 100%" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="名称" name="name">
          <a-input v-model:value="form.name" placeholder="如：用户管理 / 新增用户" />
        </a-form-item>
        <template v-if="form.type !== 'button'">
          <a-row :gutter="16">
            <a-col :span="12">
              <a-form-item label="路由路径" name="path">
                <a-input v-model:value="form.path" placeholder="如 /system/user" />
              </a-form-item>
            </a-col>
            <a-col :span="12">
              <a-form-item label="组件路径" name="component">
                <a-input v-model:value="form.component" placeholder="如 system/user/index" />
              </a-form-item>
            </a-col>
          </a-row>
          <a-row :gutter="16">
            <a-col :span="12">
              <a-form-item label="图标">
                <a-input v-model:value="form.icon" placeholder="Ant Design 图标名，如 UserOutlined" />
              </a-form-item>
            </a-col>
            <a-col :span="12">
              <a-form-item label="是否显示" name="visible">
                <a-switch v-model:checked="visibleChecked" checked-children="显示" un-checked-children="隐藏" />
              </a-form-item>
            </a-col>
          </a-row>
        </template>
        <a-form-item v-if="form.type === 'button'" label="权限码" name="permission">
          <a-input v-model:value="form.permission" placeholder="如 sys:user:add" />
        </a-form-item>
        <a-form-item v-else label="权限码（可选）" name="permission">
          <a-input v-model:value="form.permission" placeholder="如 sys:menu:list" />
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
import { ref, reactive, computed, onMounted } from 'vue';
import { message } from 'ant-design-vue';
import { PlusOutlined, AppstoreOutlined } from '@ant-design/icons-vue';
import { getMenuTree, saveMenu, deleteMenu, type MenuRecord } from '@/api/system';

const loading = ref(false);
const menuTree = ref<MenuRecord[]>([]);
const expandedKeys = ref<string[]>([]);
const expandedAll = ref(true);

const columns = [
  { title: '名称', key: 'name', width: 220 },
  { title: '类型', key: 'type', width: 90 },
  { title: '路由路径', dataIndex: 'path', key: 'path', width: 180 },
  { title: '组件路径', dataIndex: 'component', key: 'component', width: 200 },
  { title: '权限码', key: 'permission', width: 180 },
  { title: '显示', key: 'visible', width: 80 },
  { title: '状态', key: 'status', width: 80 },
  { title: '排序', dataIndex: 'sort', key: 'sort', width: 70 },
  { title: '操作', key: 'action', width: 220, fixed: 'right' as const },
];

/** 收集全部节点 key，用于"展开全部" */
function collectKeys(nodes: MenuRecord[]): string[] {
  return nodes.flatMap((n) => [n.id, ...(n.children ? collectKeys(n.children) : [])]);
}
const allRowKeys = computed(() => collectKeys(menuTree.value));

function typeText(type: string): string {
  return type === 'dir' ? '目录' : type === 'menu' ? '菜单' : '按钮';
}
function typeColor(type: string): string {
  return type === 'dir' ? 'purple' : type === 'menu' ? 'blue' : 'orange';
}

async function loadTree() {
  loading.value = true;
  try {
    menuTree.value = (await getMenuTree()) || [];
  } finally {
    loading.value = false;
  }
}

// ============ 新增/编辑 ============

const formVisible = ref(false);
const submitting = ref(false);
const form = reactive<{
  id?: string;
  parentId?: string | null;
  name: string;
  type: 'dir' | 'menu' | 'button';
  path?: string;
  component?: string;
  icon?: string;
  permission?: string;
  visible: boolean;
  status: number;
  sort: number;
}>({
  name: '',
  type: 'menu',
  visible: true,
  status: 1,
  sort: 0,
});

const visibleChecked = computed({
  get: () => form.visible,
  set: (v: boolean) => (form.visible = v),
});

const formRules = {
  name: [{ required: true, message: '请输入名称', trigger: 'blur' }],
  type: [{ required: true, message: '请选择类型' }],
};

/** 上级菜单选项（排除自身及子树，避免环） */
const parentOptions = computed(() => {
  const excludeId = form.id;
  function filterNodes(nodes: MenuRecord[]): MenuRecord[] {
    return nodes
      .filter((n) => n.id !== excludeId)
      .map((n) => ({ ...n, children: n.children ? filterNodes(n.children) : undefined }));
  }
  return filterNodes(menuTree.value);
});

function openForm(record: MenuRecord | null, parentId?: string) {
  if (record) {
    Object.assign(form, {
      id: record.id,
      parentId: record.parentId,
      name: record.name,
      type: record.type,
      path: record.path,
      component: record.component,
      icon: record.icon,
      permission: record.permission,
      visible: record.visible,
      status: Number(record.status) || 0,
      sort: record.sort,
    });
  } else {
    Object.assign(form, {
      id: undefined,
      parentId: parentId || null,
      name: '',
      type: parentId?.length ? 'button' : 'menu',
      path: '',
      component: '',
      icon: '',
      permission: '',
      visible: true,
      status: 1,
      sort: 0,
    });
  }
  formVisible.value = true;
}

async function onSubmit() {
  submitting.value = true;
  try {
    await saveMenu({ ...form });
    message.success(form.id ? '更新成功' : '新增成功');
    formVisible.value = false;
    loadTree();
  } finally {
    submitting.value = false;
  }
}

async function onDelete(record: MenuRecord) {
  await deleteMenu(record.id);
  message.success('删除成功');
  loadTree();
}

onMounted(loadTree);
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}
</style>
