<template>
  <div class="page-container">
    <div class="table-operator">
      <a-button type="primary" :loading="loading" @click="loadData">
        <template #icon><ReloadOutlined /></template>
        刷新
      </a-button>
      <span class="online-total">共 {{ data.length }} 个在线会话</span>
    </div>

    <a-table
      :data-source="data"
      :columns="columns"
      :loading="loading"
      :pagination="false"
      row-key="sessionId"
      size="middle"
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
import { message } from 'ant-design-vue';
import { ReloadOutlined } from '@ant-design/icons-vue';
import { getOnlineUsers, kickOnlineUser, type OnlineUserItem } from '@/api/monitor';

const loading = ref(false);
const data = ref<OnlineUserItem[]>([]);

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
  } finally {
    loading.value = false;
  }
}

async function onKick(record: OnlineUserItem) {
  await kickOnlineUser(record.sessionId);
  message.success('该会话已被强制下线');
  loadData();
}

onMounted(loadData);
</script>

<style scoped>
.online-total {
  margin-left: 12px;
  color: rgba(0, 0, 0, 0.45);
}
.ua-cell {
  color: rgba(0, 0, 0, 0.45);
}
.danger-link {
  color: #ff4d4f;
}
</style>
