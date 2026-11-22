<template>
  <div class="page-container">
    <!-- 操作区：消息队列演示 -->
    <div class="table-operator">
      <a-space>
        <a-button type="primary" @click="openPublish">
          <template #icon><PlusOutlined /></template>
          发布演示消息
        </a-button>
        <a-button @click="loadStats">队列状态</a-button>
      </a-space>
      <a-space v-if="selectedCount > 0" style="margin-left: 12px">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button :loading="batchLoading" @click="onBatchRead">批量标已读</a-button>
        <a-button danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
        <a @click="clearSelection">清空勾选</a>
      </a-space>
      <a-button
        v-else-if="pagination.total > 0"
        style="margin-left: 12px"
        @click="onSelectAll"
      >选中全部 {{ pagination.total }} 条</a-button>
      <span v-if="statsText" class="stats-text">{{ statsText }}</span>
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
        <template v-if="column.key === 'title'">
          <a-badge v-if="!record.isRead" status="processing" title="未读" />
          <span :class="{ 'unread-title': !record.isRead }">{{ record.title }}</span>
        </template>
        <template v-else-if="column.key === 'isRead'">
          <a-tag :color="record.isRead ? 'default' : 'blue'">
            {{ record.isRead ? '已读' : '未读' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a @click="openDetail(record)">详情</a>
            <a v-if="!record.isRead" @click="onMarkRead(record)">标记已读</a>
          </a-space>
        </template>
      </template>
    </a-table>

    <!-- 发布演示消息弹窗 -->
    <a-modal v-model:open="publishVisible" title="发布演示消息" :confirm-loading="publishing" @ok="onPublish">
      <a-form :model="publishForm" :rules="publishRules" layout="vertical">
        <a-form-item label="主题" name="topic" extra="消费者按主题订阅，默认 system.notice 有消费者在线">
          <a-input v-model:value="publishForm.topic" :maxlength="100" placeholder="如：system.notice" />
        </a-form-item>
        <a-form-item label="内容" name="content">
          <a-textarea v-model:value="publishForm.content" :rows="4" :maxlength="500" placeholder="消息内容" />
        </a-form-item>
      </a-form>
      <div class="publish-tip">发布后由消息队列消费者异步落库为站内消息，稍后可在列表中查看。</div>
    </a-modal>

    <!-- 详情弹窗 -->
    <a-modal v-model:open="detailVisible" :title="detailRecord?.title || '消息详情'" :footer="null" width="560">
      <div class="detail-meta">
        <a-tag>{{ detailRecord?.topic }}</a-tag>
        <span class="detail-time">{{ detailRecord?.createdAt }}</span>
      </div>
      <div class="detail-content">{{ detailRecord?.content || '（无内容）' }}</div>
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
  getMessagePage,
  markMessageRead,
  batchReadMessages,
  batchDeleteMessages,
  publishDemoMessage,
  getQueueStats,
  type MessageItem,
} from '@/api/message';

const loading = ref(false);
const data = ref<MessageItem[]>([]);

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
  const res = await getMessagePage({ pageIndex: 1, pageSize: 10000 });
  return (res.items || []).map((m) => m.id);
});

async function onSelectAll() {
  await selectAll();
  message.info(`已选中全部 ${selectedCount.value} 条消息`);
}

const query = reactive({ pageIndex: 1, pageSize: 10 });

const pagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showQuickJumper: true,
  showTotal: (t: number) => `共 ${t} 条`,
});

const columns = [
  { title: '标题', key: 'title' },
  { title: '主题', dataIndex: 'topic', key: 'topic', width: 150 },
  { title: '时间', dataIndex: 'createdAt', key: 'createdAt', width: 170 },
  { title: '状态', key: 'isRead', width: 90 },
  { title: '操作', key: 'action', width: 160, fixed: 'right' as const },
];

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) {
      query.pageIndex = pageIndex;
      pagination.current = pageIndex;
    }
    const res = await getMessagePage({ ...query });
    data.value = res.items || [];
    pagination.total = Number(res.total) || 0;
    setTotal(Number(res.total) || 0);
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

onMounted(() => loadData());

// ============ 标记已读 ============

async function onMarkRead(record: MessageItem) {
  await markMessageRead(record.id);
  message.success('已标记已读');
  loadData();
}

// ============ 批量操作 ============

function onBatchRead() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量标已读',
    content: `确定把选中的 ${ids.length} 条消息标记为已读吗？`,
    async onOk() {
      batchLoading.value = true;
      try {
        const res = await batchReadMessages(ids);
        message.success(`批量标已读完成：更新 ${res.updated} 条`);
        clearSelection();
        loadData();
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

function onBatchDelete() {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除消息',
    content: `确定删除选中的 ${ids.length} 条消息吗？`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const res = await batchDeleteMessages(ids);
        message.success(`删除完成：成功 ${res.deleted} 条`);
        clearSelection();
        loadData(pageAfterDelete(pagination.total, res.deleted, pagination.current, pagination.pageSize));
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

// ============ 消息队列演示 ============

const publishVisible = ref(false);
const publishing = ref(false);
const publishForm = reactive({ topic: 'system.notice', content: '' });

const publishRules = {
  content: [
    { required: true, message: '请输入消息内容', trigger: 'blur' },
    { max: 500, message: '消息内容不超过 500 个字符', trigger: 'blur' },
  ],
};

function openPublish() {
  publishForm.content = '';
  publishVisible.value = true;
}

async function onPublish() {
  publishing.value = true;
  try {
    await publishDemoMessage(publishForm.topic || 'system.notice', publishForm.content);
    message.success('消息已发布，消费者处理完成后可在消息列表查看');
    publishVisible.value = false;
    loadData(1);
  } finally {
    publishing.value = false;
  }
}

const statsText = ref('');

async function loadStats() {
  const stats = await getQueueStats();
  // 后端返回 { provider, backlogs: { 主题: 积压条数 } } 之类的松散结构，按展示拼文本
  if (stats && typeof stats === 'object') {
    const entries = Object.entries(stats).filter(([k]) => k !== 'provider');
    const backlogPart = entries
      .map(([k, v]) => (typeof v === 'object' && v !== null ? `${k} ${JSON.stringify(v)}` : `${k} ${v}`))
      .join('，');
    const provider = (stats as any).provider ? `提供方 ${(stats as any).provider}` : '';
    statsText.value = [provider, backlogPart].filter(Boolean).join(' · ');
  }
}

// ============ 详情 ============

const detailVisible = ref(false);
const detailRecord = ref<MessageItem | null>(null);

async function openDetail(record: MessageItem) {
  detailRecord.value = record;
  detailVisible.value = true;
  if (!record.isRead) {
    // 详情即已读：与列表口径一致
    await markMessageRead(record.id);
    record.isRead = true;
  }
}
</script>

<style scoped>
.stats-text {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}

.batch-count {
  color: #1677ff;
}

.unread-title {
  font-weight: 600;
}

.publish-tip {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}

.detail-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.detail-time {
  color: rgba(0, 0, 0, 0.45);
  font-size: 13px;
}

.detail-content {
  white-space: pre-wrap;
  line-height: 1.7;
  max-height: 50vh;
  overflow-y: auto;
}
</style>
