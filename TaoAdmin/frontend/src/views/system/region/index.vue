<template>
  <div class="region-page">
    <!-- 数据版本与更新（老板令：页面显示当前数据版本、抓取时间、条数；更新后台执行+进度可见） -->
    <a-card :bordered="false" style="margin-bottom: 12px">
      <template #title>数据版本</template>
      <template #extra>
        <a-space>
          <a-button
            v-permission="'sys:region:manage'"
            type="primary"
            :loading="running"
            :disabled="running"
            @click="onStartUpdate"
          >
            {{ running ? '更新执行中…' : '检查并更新到最新' }}
          </a-button>
          <a-button v-permission="'sys:region:manage'" :disabled="running" @click="onRestoreBuiltin">
            从内置数据包恢复
          </a-button>
        </a-space>
      </template>

      <a-descriptions :column="4" size="small" style="margin-bottom: 4px">
        <a-descriptions-item label="当前版本">{{ status?.version || '—' }}</a-descriptions-item>
        <a-descriptions-item label="抓取时间">{{ formatTime(status?.fetchedAt) }}</a-descriptions-item>
        <a-descriptions-item label="数据来源">{{ status?.source || '—' }}</a-descriptions-item>
        <a-descriptions-item label="总条数">{{ status?.totalCount ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="省级">{{ status?.provinceCount ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="地级市">{{ status?.cityCount ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="区县">{{ status?.countyCount ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="街道（乡镇）">{{ status?.townCount ?? '—' }}</a-descriptions-item>
      </a-descriptions>

      <!-- 更新进度：连接数据源 → 已抓条数 → 校验 → 写入（百分比）→ 完成/失败 -->
      <div v-if="progress && progress.status !== 'Idle'" class="update-progress">
        <a-alert
          :type="progress.status === 'Success' ? 'success' : progress.status === 'Failed' ? 'error' : 'info'"
          show-icon
          style="margin-bottom: 8px"
          :message="progressSummary"
        />
        <a-progress
          v-if="progress.status === 'Running'"
          :percent="progress.percent"
          :status="'active'"
        />
        <template v-if="progress.status === 'Success'">
          <a-descriptions :column="3" size="small">
            <a-descriptions-item label="更新前条数">{{ progress.beforeCount }}</a-descriptions-item>
            <a-descriptions-item label="更新后条数">{{ progress.afterCount }}</a-descriptions-item>
            <a-descriptions-item label="耗时">{{ progress.durationSeconds }} 秒</a-descriptions-item>
          </a-descriptions>
        </template>
        <a-typography-text v-if="progress.status === 'Failed'" type="danger">
          {{ progress.error }}
        </a-typography-text>
      </div>
    </a-card>

    <a-card :bordered="false">
      <template #title>行政区划（省 / 市 / 区县 / 街道）</template>
      <template #extra>
        <a-input-search
          v-model:value="keyword"
          placeholder="搜索名称或路径，如：朝阳 / 东华门街道"
          style="width: 320px"
          allow-clear
          @search="onSearch"
        />
      </template>

      <a-alert
        v-if="searched"
        type="info"
        show-icon
        closable
        style="margin-bottom: 12px"
        :message="`搜索「${searchedKeyword}」命中 ${searchResults.length} 条（最多显示 50 条），清空搜索可查看完整层级`"
        @close="clearSearch"
      />

      <a-table
        :columns="columns"
        :data-source="viewData"
        :loading="loading"
        :pagination="false"
        row-key="code"
        size="middle"
        @expand="onExpand"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'name'">
            <span v-if="searched">{{ (record as TreeNode).fullPath }}</span>
            <span v-else>{{ (record as TreeNode).name }}</span>
          </template>
        </template>
      </a-table>
    </a-card>
  </div>
</template>

<script setup lang="ts">
/**
 * 行政区划查看页（老板 2026-11-22 令⑪地址库）：
 * 只读——树形查看（省→市→区县→街道懒加载）+ 关键词搜索（命中带全路径）；
 * 数据版本展示 + 一键更新（后台执行不卡屏，进度实时轮询，刷新页面进度仍在）；
 * 不做逐条新增/编辑/删除（对齐老板口径：除更新外只读）。
 */
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { Modal, message } from 'ant-design-vue';
import {
  getRegionChildren,
  getRegionStatus,
  getRegionUpdateProgress,
  restoreRegionBuiltin,
  searchRegions,
  startRegionUpdate,
  type RegionItem,
  type RegionStatus,
  type RegionUpdateProgress,
} from '@/api/region';

const keyword = ref('');
const searched = ref(false);
const searchedKeyword = ref('');
const searchResults = ref<RegionItem[]>([]);
const treeData = ref<TreeNode[]>([]);
const loading = ref(false);

const status = ref<RegionStatus | null>(null);
const progress = ref<RegionUpdateProgress | null>(null);
const running = computed(() => progress.value?.status === 'Running');

const columns = [
  { title: '名称', key: 'name', dataIndex: 'name' },
  { title: '区划代码', key: 'code', dataIndex: 'code', width: 160 },
  { title: '层级', key: 'levelText', dataIndex: 'levelText', width: 120 },
];

const levelText = (lv: number) =>
  lv === 1 ? '省' : lv === 2 ? '地级市' : lv === 3 ? '区县' : '街道（乡镇）';

/** 进度摘要文案：执行中显示阶段与已抓条数；成功/失败显示结论 */
const progressSummary = computed(() => {
  const p = progress.value;
  if (!p) return '';
  if (p.status === 'Running') {
    const extra = p.fetchedCount > 0 ? `，已抓取 ${p.fetchedCount} 条` : '';
    return `更新执行中：${p.stage}${extra}（任务在后台运行，离开本页面不受影响）`;
  }
  if (p.status === 'Success') return `更新完成：${p.version || ''}`;
  if (p.status === 'Failed') return `更新失败：卡在「${p.stage}」，本地数据未改动`;
  return '';
});

/** 表格树节点：区划条目 + 懒加载 children */
interface TreeNode extends RegionItem {
  levelText: string;
  isLeaf: boolean;
  children?: TreeNode[];
}

function toTreeNode(r: RegionItem): TreeNode {
  return { ...r, levelText: levelText(r.level), isLeaf: r.level >= 4 };
}

/** a-table 树形展开时懒加载子级 */
async function onExpand(isExpanded: boolean, record: TreeNode): Promise<void> {
  if (!isExpanded || record.children?.length) return;
  const children = await getRegionChildren(record.code);
  record.children = children.map(toTreeNode);
}

onMounted(async () => {
  loading.value = true;
  try {
    const [provinces, st] = await Promise.all([getRegionChildren(''), getRegionStatus()]);
    treeData.value = provinces.map(toTreeNode);
    status.value = st;
    // 刷新页面后进度仍在：进入页面即拉一次最近任务状态，执行中则继续轮询
    progress.value = st.update;
    if (st.update.status === 'Running') startPolling();
  } finally {
    loading.value = false;
  }
});

async function onSearch(): Promise<void> {
  const kw = keyword.value.trim();
  if (!kw) {
    clearSearch();
    return;
  }
  loading.value = true;
  try {
    searchResults.value = await searchRegions(kw);
    searched.value = true;
    searchedKeyword.value = kw;
    if (searchResults.value.length === 0) message.info('未命中任何区划');
  } finally {
    loading.value = false;
  }
}

function clearSearch(): void {
  searched.value = false;
  searchResults.value = [];
  keyword.value = '';
}

/** 搜索态展示命中列表（含全路径），否则展示省级树 */
const viewData = computed<TreeNode[]>(() =>
  searched.value ? searchResults.value.map(toTreeNode) : treeData.value
);

// ==================== 后台更新与进度轮询 ====================

let pollTimer: ReturnType<typeof setInterval> | null = null;

function startPolling(): void {
  if (pollTimer) return;
  pollTimer = setInterval(async () => {
    try {
      progress.value = await getRegionUpdateProgress();
      if (progress.value.status !== 'Running') stopPolling();
      if (progress.value.status === 'Success') {
        message.success(`更新完成：${progress.value.version || ''}`);
        void refreshStatusAndTree();
      } else if (progress.value.status === 'Failed') {
        message.error(progress.value.error || '更新失败');
      }
    } catch {
      // 轮询失败不中断：下个周期重试
    }
  }, 2000);
}

function stopPolling(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

onUnmounted(stopPolling);

async function refreshStatusAndTree(): Promise<void> {
  const [provinces, st] = await Promise.all([getRegionChildren(''), getRegionStatus()]);
  treeData.value = provinces.map(toTreeNode);
  status.value = st;
}

function onStartUpdate(): void {
  Modal.confirm({
    title: '更新行政区划数据',
    content: '将从民政部官方渠道抓取最新四级区划数据，校验通过后整包替换。任务在后台执行，期间可离开本页面。',
    okText: '开始更新',
    cancelText: '取消',
    onOk: async () => {
      try {
        await startRegionUpdate();
        message.info('更新任务已启动，进度将在下方实时展示');
        progress.value = await getRegionUpdateProgress();
        startPolling();
      } catch {
        // 重复点拦截等失败提示由请求层统一弹出
      }
    },
  });
}

function onRestoreBuiltin(): void {
  Modal.confirm({
    title: '从内置数据包恢复',
    content: '将用系统内置的行政区划数据包整包替换当前数据（在线更新失败时的兜底手段）。',
    okText: '开始恢复',
    cancelText: '取消',
    onOk: async () => {
      try {
        await restoreRegionBuiltin();
        message.info('恢复任务已启动，进度将在下方实时展示');
        progress.value = await getRegionUpdateProgress();
        startPolling();
      } catch {
        // 失败提示由请求层统一弹出
      }
    },
  });
}

function formatTime(t?: string | null): string {
  if (!t) return '—';
  return t.replace('T', ' ').slice(0, 19);
}
</script>

<style scoped>
.update-progress {
  margin-top: 8px;
}
</style>
