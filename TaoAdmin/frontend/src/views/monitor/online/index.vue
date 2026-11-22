<template>
  <div class="page-container">
    <div class="table-operator">
      <a-button type="primary" :loading="loading" @click="loadData">
        <template #icon><ReloadOutlined /></template>
        刷新
      </a-button>
      <span class="online-total">共 {{ data.length }} 个在线会话</span>
      <a-space v-if="selectedCount > 0" style="margin-left: 12px">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button v-permission="'monitor:online:kick'" danger :loading="batchLoading" @click="onBatchKick">批量强制下线</a-button>
        <a @click="clearSelection">清空勾选</a>
      </a-space>
    </div>

    <a-table
      :data-source="data"
      :columns="columns"
      :loading="loading"
      :pagination="false"
      row-key="sessionId"
      size="middle"
      :row-selection="{ selectedRowKeys, onChange: onTableSelectChange }"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'userName'">
          <a-space>
            <a-badge status="processing" />
            <span>{{ record.userName }}</span>
          </a-space>
        </template>
        <template v-else-if="column.key === 'userAgent'">
          <a-tooltip :title="record.userAgent">
            <span class="ua-cell">{{ record.userAgent || '-' }}</span>
          </a-tooltip>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-popconfirm
            v-permission="'monitor:online:kick'"
            title="确定强制下线该会话吗？下线后即刻生效。"
            @confirm="onKick(record)"
          >
            <a class="danger-link">强制下线</a>
          </a-popconfirm>
        </template>
      </template>
    </a-table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { ReloadOutlined } from '@ant-design/icons-vue';
import { useBatchSelection } from '@/composables/useBatchSelection';
import { getOnlineUsers, kickOnlineUser, batchKickOnlineUsers, type OnlineUserItem } from '@/api/monitor';

const loading = ref(false);
const data = ref<OnlineUserItem[]>([]);

// ============ 批量选择（在线会话不分页，表头全选即全量） ============

const batchLoading = ref(false);
const {
  selectedRowKeys,
  selectedCount,
  onTableSelectChange,
  clearSelection,
  getSelectedIds,
} = useBatchSelection(async () => data.value.map((d) => d.sessionId));

const columns = [
  { title: '用户名', key: 'userName', dataIndex: 'userName', width: 120 },
  { title: '昵称', dataIndex: 'nickName', key: 'nickName', width: 120 },
  { title: '登录时间', dataIndex: 'loginTime', key: 'loginTime', width: 170 },
  { title: '最近活跃', dataIndex: 'lastActiveTime', key: 'lastActiveTime', width: 170 },
  { title: '来源 IP', dataIndex: 'ip', key: 'ip', width: 130 },
  { title: '客户端', key: 'userAgent', ellipsis: true },
  { title: '操作', key: 'action', width: 110 },
];

async function loadData() {
  loading.value = true;
  try {
    data.value = (await getOnlineUsers()) || [];
    clearSelection();
  } finally {
    loading.value = false;
  }
}

async function onKick(record: OnlineUserItem) {
  await kickOnlineUser(record.sessionId);
  message.success('该会话已被强制下线');
  loadData();
}

function onBatchKick() {
  const sessionIds = getSelectedIds();
  if (!sessionIds.length) return;
  Modal.confirm({
    title: '批量强制下线',
    content: `确定将选中的 ${sessionIds.length} 个会话强制下线吗？下线后即刻生效。`,
    okText: '强制下线',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const r = await batchKickOnlineUsers(sessionIds);
        message.success(`批量强制下线完成：成功 ${r.kicked} 个会话`);
        clearSelection();
        await loadData();
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

onMounted(loadData);
</script>

<style scoped>
.online-total {
  margin-left: 12px;
  color: rgba(0, 0, 0, 0.45);
}
.batch-count {
  color: #1677ff;
}
.ua-cell {
  color: rgba(0, 0, 0, 0.45);
}
.danger-link {
  color: #ff4d4f;
}
</style>
