<template>
  <div class="page-container">
    <!-- 工具条：上传 / 搜索 / 服务地址 -->
    <div class="toolbar">
      <a-upload
        :show-upload-list="false"
        :before-upload="onBeforeUpload"
        multiple
      >
        <a-button type="primary">
          <PlusOutlined /> 上传文件
        </a-button>
      </a-upload>

      <a-input-search
        v-model:value="keyword"
        placeholder="按文件名搜索"
        style="width: 260px"
        allow-clear
        @search="loadData(1)"
      />

      <div class="spacer" />

      <a-button @click="addrOpen = true">
        <SettingOutlined /> 文件中心地址
      </a-button>
    </div>

    <!-- 文件表格 -->
    <a-table
      :data-source="files"
      :columns="columns"
      :loading="loading"
      :pagination="pagination"
      row-key="id"
      size="small"
      @change="onTableChange"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'preview'">
          <img
            v-if="isImage(record)"
            class="thumb"
            :src="buildFileUrl(record.fileUrl)"
            loading="lazy"
            @click="previewRecord = record"
          />
          <span v-else class="ext-badge">{{ extOf(record.fileName) }}</span>
        </template>
        <template v-else-if="column.key === 'fileName'">
          <div>{{ record.fileName }}</div>
          <div class="sub">{{ formatSize(record.size) }} · {{ record.source || '手动上传' }} · {{ record.md5.slice(0, 8) }}…</div>
        </template>
        <template v-else-if="column.key === 'fileUrl'">
          <a class="path-link" @click="onCopy(record)">{{ record.fileUrl }}</a>
        </template>
        <template v-else-if="column.key === 'createdAt'">
          {{ (record.createdAt || '').replace('T', ' ') }}
        </template>
        <template v-else-if="column.key === 'action'">
          <a @click="onCopy(record)">复制路径</a>
          <a-popconfirm title="确定删除该文件吗？" @confirm="onDelete(record)">
            <a class="danger">删除</a>
          </a-popconfirm>
        </template>
      </template>
      <template #empty>
        <a-empty description="还没有文件，点上面「上传文件」试试" />
      </template>
    </a-table>

    <!-- 图片预览 -->
    <a-image
      v-if="previewRecord"
      :src="buildFileUrl(previewRecord.fileUrl)"
      :alt="previewRecord.fileName"
      style="display: none"
      :preview="{ visible: previewVisible, onVisibleChange: onPreviewClose }"
    />

    <!-- 服务地址设置 -->
    <a-modal v-model:open="addrOpen" title="文件中心服务地址" @ok="onSaveAddr">
      <p class="addr-tip">
        本页面通过接口调用独立的文件中心服务管理文件，改完地址会保存到本机。
      </p>
      <a-input v-model:value="addrInput" placeholder="例如 http://localhost:5003" />
      <a-button style="margin-top: 8px" :loading="pinging" @click="onPing">测试连接</a-button>
      <span v-if="pingResult" style="margin-left: 8px">{{ pingResult }}</span>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { PlusOutlined, SettingOutlined } from '@ant-design/icons-vue';
import { message } from 'ant-design-vue';
import type { TableProps } from 'ant-design-vue';
import {
  listFiles,
  uploadFile,
  deleteFile,
  buildFileUrl,
  getTaoFilesBase,
  setTaoFilesBase,
  pingTaoFiles,
  copyText,
  type FileItem,
} from '@/api/taoFiles';

const files = ref<FileItem[]>([]);
const loading = ref(false);
const keyword = ref('');
const previewRecord = ref<FileItem | null>(null);
const previewVisible = ref(true);

const query = reactive({ pageIndex: 1, pageSize: 10 });
const total = ref(0);

const addrOpen = ref(false);
const addrInput = ref(getTaoFilesBase());
const pinging = ref(false);
const pingResult = ref('');

const pagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showQuickJumper: true,
  showTotal: (t: number) => `共 ${t} 个文件`,
});

const columns = [
  { title: '预览', key: 'preview', width: 72 },
  { title: '文件名', key: 'fileName', ellipsis: true },
  { title: '相对路径', key: 'fileUrl', ellipsis: true },
  { title: '上传时间', key: 'createdAt', width: 160 },
  { title: '操作', key: 'action', width: 140 },
];

const IMG_EXTS = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp'];

function extOf(name: string): string {
  const i = name.lastIndexOf('.');
  return i >= 0 ? name.slice(i + 1, i + 5).toUpperCase() : '文件';
}

function isImage(f: FileItem): boolean {
  const i = f.fileName.lastIndexOf('.');
  if (i < 0) return false;
  return IMG_EXTS.includes(f.fileName.slice(i + 1).toLowerCase());
}

function formatSize(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / 1024 / 1024).toFixed(2)} MB`;
}

async function loadData(page = query.pageIndex): Promise<void> {
  loading.value = true;
  try {
    query.pageIndex = page;
    const data = await listFiles({
      pageIndex: page,
      pageSize: pagination.pageSize,
      keyword: keyword.value || undefined,
    });
    files.value = data.list || [];
    total.value = data.total;
    pagination.current = page;
    pagination.total = data.total;
  } catch (e: any) {
    message.error(e.message || '加载失败');
  } finally {
    loading.value = false;
  }
}

/** 拦截默认上传，改走文件中心接口 */
function onBeforeUpload(file: File): boolean {
  void doUpload(file);
  return false;
}

async function doUpload(file: File): Promise<void> {
  try {
    const r = await uploadFile(file);
    if (r.reused) message.info(`「${r.fileName}」已存在，直接复用（秒传）`);
    else message.success(`「${r.fileName}」上传成功`);
    await loadData(1);
  } catch (e: any) {
    message.error(`「${file.name}」上传失败：${e.message || '未知错误'}`);
  }
}

async function onCopy(record: FileItem): Promise<void> {
  await copyText(record.fileUrl);
}

async function onDelete(record: FileItem): Promise<void> {
  try {
    await deleteFile(record.id);
    message.success('已删除');
    if (files.value.length === 1 && query.pageIndex > 1) await loadData(query.pageIndex - 1);
    else await loadData();
  } catch (e: any) {
    message.error(e.message || '删除失败');
  }
}

function onPreviewClose(v: boolean): void {
  previewVisible.value = v;
  if (!v) previewRecord.value = null;
}

const onTableChange: TableProps['onChange'] = (pag) => {
  pagination.pageSize = pag.pageSize ?? 10;
  loadData(pag.current ?? 1);
};

async function onPing(): Promise<void> {
  pinging.value = true;
  pingResult.value = '';
  try {
    pingResult.value = await pingTaoFiles();
  } catch {
    pingResult.value = '连不上，检查服务是否启动';
  } finally {
    pinging.value = false;
  }
}

function onSaveAddr(): void {
  const url = addrInput.value.trim().replace(/\/+$/, '');
  if (!/^https?:\/\//.test(url)) {
    message.warning('地址要以 http:// 或 https:// 开头');
    return;
  }
  setTaoFilesBase(url);
  addrOpen.value = false;
  message.success('地址已保存');
  loadData(1);
}

onMounted(() => loadData(1));
</script>

<style scoped lang="less">
.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;

  .spacer {
    flex: 1;
  }
}

.thumb {
  width: 40px;
  height: 40px;
  object-fit: cover;
  border-radius: 4px;
  border: 1px solid #f0f0f0;
  cursor: zoom-in;
}

.ext-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 4px;
  background: #f5f5f5;
  color: #1677ff;
  font-size: 11px;
  font-weight: 600;
}

.sub {
  font-size: 12px;
  color: rgba(0, 0, 0, 0.45);
}

.path-link {
  font-family: monospace;
  font-size: 12px;
}

.danger {
  color: #ff4d4f;
  margin-left: 12px;
}

.addr-tip {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}
</style>
