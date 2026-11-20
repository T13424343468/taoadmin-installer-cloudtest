<template>
  <div class="page-container">
    <!-- 搜索区 -->
    <FilterBar :values="query" :fields="filterFields" @search="loadData(1)" @reset="resetQuery" />

    <!-- 操作区 -->
    <div class="table-operator">
      <a-button v-permission="'sys:notice:manage'" type="primary" @click="openPublish">
        <template #icon><PlusOutlined /></template>
        发布公告
      </a-button>
    </div>

    <!-- 表格 -->
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
          <a-tag :color="record.status === 1 ? 'green' : 'red'">
            {{ record.status === 1 ? '已发布' : '已下线' }}
          </a-tag>
        </template>
        <template v-else-if="column.key === 'action'">
          <a-space>
            <a @click="openDetail(record)">详情</a>
            <a-popconfirm
              v-permission="'sys:notice:manage'"
              :title="record.status === 1 ? '下线后登录用户将不可见，确定下线吗？' : '重新上线后登录用户可见，确定上线吗？'"
              @confirm="onToggleStatus(record)"
            >
              <a v-permission="'sys:notice:manage'">
                {{ record.status === 1 ? '下线' : '重新上线' }}
              </a>
            </a-popconfirm>
            <a-popconfirm
              v-permission="'sys:notice:manage'"
              title="确定删除该公告吗？"
              @confirm="onDelete(record)"
            >
              <a class="danger-link" v-permission="'sys:notice:manage'">删除</a>
            </a-popconfirm>
          </a-space>
        </template>
      </template>
    </a-table>

    <!-- 发布弹窗 -->
    <a-modal
      v-model:open="publishVisible"
      title="发布公告"
      :confirm-loading="publishing"
      @ok="onPublish"
    >
      <a-form :model="publishForm" :rules="publishRules" layout="vertical">
        <a-form-item label="公告标题" name="title">
          <a-input v-model:value="publishForm.title" :maxlength="100" show-count placeholder="公告标题" />
        </a-form-item>
        <a-form-item label="公告内容" name="content">
          <a-textarea v-model:value="publishForm.content" :rows="6" :maxlength="20000" placeholder="公告正文（支持换行）" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- 详情弹窗 -->
    <a-modal v-model:open="detailVisible" :title="detailRecord?.title || '公告详情'" :footer="null" width="560">
      <div class="detail-meta">
        <a-tag :color="detailRecord?.status === 1 ? 'green' : 'red'">
          {{ detailRecord?.status === 1 ? '已发布' : '已下线' }}
        </a-tag>
        <span class="detail-publisher">{{ detailRecord?.publisherName || '系统' }}</span>
        <span class="detail-time">{{ detailRecord?.publishTime || detailRecord?.createdAt }}</span>
      </div>
      <div class="detail-content">{{ detailRecord?.content || '（无正文）' }}</div>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { message } from 'ant-design-vue';
import { PlusOutlined } from '@ant-design/icons-vue';
import FilterBar, { type FilterField } from '@/components/FilterBar/index.vue';
import {
  getAnnouncementPage,
  publishAnnouncement,
  setAnnouncementStatus,
  deleteAnnouncement,
  type AnnouncementItem,
} from '@/api/announcement';

const loading = ref(false);
const data = ref<AnnouncementItem[]>([]);

const query = reactive<{ keyword?: string; pageIndex: number; pageSize: number }>({
  pageIndex: 1,
  pageSize: 10,
});

const filterFields: FilterField[] = [
  { key: 'keyword', label: '标题', type: 'input', primary: true, placeholder: '请输入公告标题' },
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
  { title: '公告标题', dataIndex: 'title', key: 'title' },
  { title: '发布人', dataIndex: 'publisherName', key: 'publisherName', width: 120 },
  { title: '发布时间', dataIndex: 'publishTime', key: 'publishTime', width: 170 },
  { title: '状态', key: 'status', width: 90 },
  { title: '操作', key: 'action', width: 220, fixed: 'right' as const },
];

async function loadData(pageIndex?: number) {
  loading.value = true;
  try {
    if (pageIndex) {
      query.pageIndex = pageIndex;
      pagination.current = pageIndex;
    }
    const res = await getAnnouncementPage({ ...query });
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
  query.keyword = undefined;
  loadData(1);
}

// ============ 发布 ============

const publishVisible = ref(false);
const publishing = ref(false);
const publishForm = reactive<{ title: string; content?: string }>({ title: '', content: '' });

const publishRules = {
  title: [
    { required: true, message: '请输入公告标题', trigger: 'blur' },
    { max: 100, message: '公告标题不超过 100 个字符', trigger: 'blur' },
  ],
};

function openPublish() {
  publishForm.title = '';
  publishForm.content = '';
  publishVisible.value = true;
}

async function onPublish() {
  publishing.value = true;
  try {
    await publishAnnouncement({ ...publishForm });
    message.success('发布公告成功，登录用户即可见');
    publishVisible.value = false;
    loadData(1);
  } finally {
    publishing.value = false;
  }
}

// ============ 上下线 / 删除 / 详情 ============

async function onToggleStatus(record: AnnouncementItem) {
  await setAnnouncementStatus(record.id, record.status !== 1);
  message.success(record.status === 1 ? '公告已下线' : '公告已上线');
  loadData();
}

async function onDelete(record: AnnouncementItem) {
  await deleteAnnouncement(record.id);
  message.success('删除成功');
  loadData();
}

const detailVisible = ref(false);
const detailRecord = ref<AnnouncementItem | null>(null);

function openDetail(record: AnnouncementItem) {
  detailRecord.value = record;
  detailVisible.value = true;
}

onMounted(() => loadData());
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}

.detail-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.detail-publisher {
  color: rgba(0, 0, 0, 0.65);
  font-size: 13px;
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
