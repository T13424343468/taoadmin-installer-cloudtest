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
        @search="onSearch"
      />

      <a-select v-model:value="typeFilter" style="width: 130px" allow-clear placeholder="全部类型" @change="onSearch">
        <a-select-option value="image/">图片</a-select-option>
        <a-select-option value="application/pdf">PDF</a-select-option>
        <a-select-option value="application/zip">压缩包</a-select-option>
        <a-select-option value="text/">文本</a-select-option>
        <a-select-option value="video/">视频</a-select-option>
        <a-select-option value="audio/">音频</a-select-option>
      </a-select>

      <a-range-picker v-model:value="timeRange" show-time value-format="YYYY-MM-DD HH:mm:ss" @change="onSearch" />

      <a-tag v-if="storageStats" class="storage-tag">存储：{{ storageStats.count }} 个 / {{ formatSize(storageStats.totalBytes) }}</a-tag>

      <a-radio-group v-model:value="viewMode" button-style="solid" @change="onViewChange">
        <a-radio-button value="list">文件列表</a-radio-button>
        <a-radio-button value="recycle">回收站</a-radio-button>
      </a-radio-group>

      <div class="spacer" />

      <template v-if="selectedCount > 0">
        <span class="batch-count">已选 {{ selectedCount }} 项</span>
        <a-button danger :loading="batchLoading" @click="onBatchDelete">批量删除</a-button>
        <a @click="clearSelection">清空勾选</a>
      </template>
      <a-button v-else-if="pagination.total > 0" @click="onSelectAll">选中全部 {{ pagination.total }} 个</a-button>

      <a-button @click="addrOpen = true">
        <SettingOutlined /> 文件中心地址
      </a-button>
    </div>

    <!-- 文件表格 -->
    <a-table
      v-show="viewMode === 'list'"
      :data-source="files"
      :columns="columns"
      :loading="loading"
      :pagination="pagination"
      row-key="id"
      size="small"
      :row-selection="{ selectedRowKeys, onChange: onTableSelectChange }"
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
        <template v-else-if="column.key === 'size'">
          {{ formatSize(record.size) }}
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

    <!-- 回收站表格：恢复 / 彻底删除（二次确认） -->
    <a-table
      v-show="viewMode === 'recycle'"
      :data-source="recycleFiles"
      :columns="recycleColumns"
      :loading="recycleLoading"
      :pagination="recyclePagination"
      row-key="id"
      size="small"
      @change="onRecycleTableChange"
    >
      <template #bodyCell="{ column, record }">
        <template v-if="column.key === 'fileName'">
          <div>{{ record.fileName }}</div>
          <div class="sub">{{ formatSize(record.size) }} · 原上传于 {{ (record.createdAt || '').replace('T', ' ') }}</div>
        </template>
        <template v-else-if="column.key === 'deletedAt'">
          {{ (record.deletedAt || '').replace('T', ' ') }}
        </template>
        <template v-else-if="column.key === 'action'">
          <a @click="onRestore(record)">恢复</a>
          <a class="danger" @click="onPurgeConfirm(record)">彻底删除</a>
        </template>
      </template>
      <template #empty>
        <a-empty description="回收站是空的" />
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
import { message, Modal } from 'ant-design-vue';
import type { TableProps } from 'ant-design-vue';
import { useBatchSelection } from '@/composables/useBatchSelection';
import { pageAfterDelete } from '@/utils/listPage';
import { runBatchOneByOne } from '@/utils/batchRun';
import {
  listFiles,
  uploadFile,
  deleteFile,
  listRecycleBin,
  restoreFile,
  purgeFile,
  buildFileUrl,
  getTaoFilesBase,
  setTaoFilesBase,
  pingTaoFiles,
  copyText,
  getFileStats,
  type FileItem,
} from '@/api/taoFiles';

const files = ref<FileItem[]>([]);
const loading = ref(false);
const keyword = ref('');
const previewRecord = ref<FileItem | null>(null);
const previewVisible = ref(true);

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
  // 文件中心单页上限 100 条，按总数循环取齐全部 ID
  const ids: string[] = [];
  const first = await listFiles({ pageIndex: 1, pageSize: 100, keyword: keyword.value || undefined });
  ids.push(...first.list.map((f) => f.id));
  const pages = Math.ceil(first.total / 100);
  for (let p = 2; p <= pages; p++) {
    const next = await listFiles({ pageIndex: p, pageSize: 100, keyword: keyword.value || undefined });
    ids.push(...next.list.map((f) => f.id));
  }
  return ids;
});

async function onSelectAll() {
  await selectAll();
  message.info(`已按当前筛选条件选中全部 ${selectedCount.value} 个`);
}

// 视图切换：文件列表 / 回收站（删除的文件先进回收站，可恢复或彻底删除）
const viewMode = ref<'list' | 'recycle'>('list');
const recycleFiles = ref<FileItem[]>([]);
const recycleLoading = ref(false);
const recycleQuery = reactive({ pageIndex: 1, pageSize: 10 });
const recyclePagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showSizeChanger: true,
  showTotal: (t: number) => `共 ${t} 个文件`,
});
const recycleColumns = [
  { title: '文件名', key: 'fileName', ellipsis: true },
  { title: '删除时间', key: 'deletedAt', width: 160 },
  { title: '操作', key: 'action', width: 160 },
];

const query = reactive({ pageIndex: 1, pageSize: 10 });

// ============ ⑯ 类型/时间筛选 + 存储统计 ============

const typeFilter = ref<string | undefined>();
const timeRange = ref<[string, string] | undefined>();
const storageStats = ref<{ count: number; totalBytes: number } | null>(null);

function filterParams() {
  const r = timeRange.value;
  return {
    contentType: typeFilter.value || undefined,
    ...(r && r[0] && r[1] ? { begin: r[0], end: r[1] } : {}),
  };
}

async function loadStorageStats(): Promise<void> {
  try {
    storageStats.value = await getFileStats();
  } catch {
    storageStats.value = null;
  }
}
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
  { title: '大小', key: 'size', width: 100 },
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

// 游标缓存：记录每一页返回的下一页游标，翻下一页时带上可恒定速度定位（亿级深翻页不衰减）
const cursorMap = new Map<number, string>();

async function loadData(page = query.pageIndex): Promise<void> {
  loading.value = true;
  try {
    query.pageIndex = page;
    // 上一页的游标存在时走游标翻页（顺序翻页场景）；跳页/首次加载回退 OFFSET
    const prevCursor = cursorMap.get(page - 1);
    const data = await listFiles({
      pageIndex: page,
      pageSize: pagination.pageSize,
      keyword: keyword.value || undefined,
      ...filterParams(),
      ...(prevCursor ? { cursor: prevCursor } : {}),
    });
    files.value = data.list || [];
    total.value = data.total;
    pagination.current = page;
    pagination.total = data.total;
    setTotal(data.total);
    if (data.nextCursor) cursorMap.set(page, data.nextCursor);
    else cursorMap.delete(page);
  } catch (e: any) {
    message.error(e.message || '加载失败');
  } finally {
    loading.value = false;
  }
}

/** 搜索条件变化：清空游标缓存与勾选后回到第一页（按当前视图刷新对应列表） */
function onSearch(): void {
  cursorMap.clear();
  clearSelection();
  if (viewMode.value === 'recycle') void loadRecycle(1);
  else void loadData(1);
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
    message.success('已移入回收站，可在回收站恢复或彻底删除');
    cursorMap.clear();
    await loadData(pageAfterDelete(total.value, 1, query.pageIndex, pagination.pageSize));
  } catch (e: any) {
    message.error(e.message || '删除失败');
  }
}

// ============ 批量删除 ============

function onBatchDelete(): void {
  const ids = getSelectedIds();
  if (!ids.length) return;
  Modal.confirm({
    title: '批量删除文件',
    content: `确定删除选中的 ${ids.length} 个文件吗？删除后进入回收站，可恢复或彻底删除。逐条执行，失败项会在完成后汇报。`,
    okText: '删除',
    okType: 'danger',
    async onOk() {
      batchLoading.value = true;
      try {
        const r = await runBatchOneByOne(ids, (id) => deleteFile(id));
        message.success(`删除完成：成功 ${r.okCount} 个${r.failedCount ? `，失败 ${r.failedCount} 个` : ''}`);
        clearSelection();
        cursorMap.clear();
        await loadData(pageAfterDelete(total.value, r.okCount, query.pageIndex, pagination.pageSize));
      } finally {
        batchLoading.value = false;
      }
    },
  });
}

/** 回收站分页加载 */
async function loadRecycle(page = recycleQuery.pageIndex): Promise<void> {
  recycleLoading.value = true;
  try {
    recycleQuery.pageIndex = page;
    const data = await listRecycleBin({
      pageIndex: page,
      pageSize: recyclePagination.pageSize,
      keyword: keyword.value || undefined,
    });
    recycleFiles.value = data.list || [];
    recyclePagination.current = page;
    recyclePagination.total = data.total;
  } catch (e: any) {
    message.error(e.message || '加载失败');
  } finally {
    recycleLoading.value = false;
  }
}

/** 视图切换：进入回收站时现拉最新数据 */
function onViewChange(): void {
  if (viewMode.value === 'recycle') void loadRecycle(1);
  else void loadData(1);
}

/** 恢复：还原记录，磁盘文件从未删除，恢复即重新可用 */
async function onRestore(record: FileItem): Promise<void> {
  try {
    await restoreFile(record.id);
    message.success(`「${record.fileName}」已恢复`);
    await loadRecycle();
  } catch (e: any) {
    message.error(e.message || '恢复失败');
  }
}

/** 彻底删除：物理删记录并清理磁盘文件，不可恢复，二次确认 */
function onPurgeConfirm(record: FileItem): void {
  Modal.confirm({
    title: '彻底删除（不可恢复）',
    content: `「${record.fileName}」的记录与磁盘文件将被永久删除，无法恢复。确定继续吗？`,
    okText: '彻底删除',
    okType: 'danger',
    onOk: async () => {
      try {
        await purgeFile(record.id);
        message.success(`「${record.fileName}」已彻底删除`);
        await loadRecycle();
      } catch (e: any) {
        message.error(e.message || '删除失败');
      }
    },
  });
}

const onRecycleTableChange: TableProps['onChange'] = (pag) => {
  recyclePagination.pageSize = pag.pageSize ?? 10;
  loadRecycle(pag.current ?? 1);
};

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

onMounted(() => {
  void loadData(1);
  void loadStorageStats();
});
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

.batch-count {
  color: #1677ff;
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

.storage-tag {
  font-size: 12px;
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
