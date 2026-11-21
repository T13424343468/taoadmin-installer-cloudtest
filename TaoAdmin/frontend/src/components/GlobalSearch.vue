<template>
  <!-- 全局搜索浮层：Ctrl+K / Cmd+K / 顶栏按钮唤起；搜菜单（本地）+ 用户/公告（既有接口，不新增后端） -->
  <a-modal
    v-model:open="visible"
    :footer="null"
    :closable="false"
    width="560"
    class="global-search-modal"
    :body-style="{ padding: '12px' }"
    destroy-on-close
    @after-open="onOpened"
  >
    <a-input
      ref="inputRef"
      v-model:value="keyword"
      size="large"
      placeholder="搜索菜单、用户、公告（↑↓ 选择，回车直达，Esc 关闭）"
      allow-clear
      @keydown="onKeydown"
    />

    <div class="gs-groups">
      <!-- 空关键词：快捷入口 -->
      <template v-if="!keyword.trim()">
        <div class="gs-group-title">快捷入口</div>
        <div
          v-for="(q, i) in quickEntries"
          :key="q.path"
          class="gs-item"
          :class="{ active: flatIndex === i }"
          @mouseenter="flatIndex = i"
          @click="go(q.path)"
        >
          <AppstoreOutlined class="gs-icon" />
          <span class="gs-label">{{ q.title }}</span>
        </div>
      </template>

      <template v-else>
        <template v-for="group in groups" :key="group.key">
          <template v-if="group.items.length">
            <div class="gs-group-title">{{ group.title }}</div>
            <div
              v-for="item in group.items"
              :key="group.key + item.key"
              class="gs-item"
              :class="{ active: flatIndex === item.flatIndex }"
              @mouseenter="flatIndex = item.flatIndex"
              @click="go(item.path)"
            >
              <component :is="group.icon" class="gs-icon" />
              <span class="gs-label">{{ item.label }}</span>
              <span v-if="item.sub" class="gs-sub">{{ item.sub }}</span>
            </div>
          </template>
        </template>

        <!-- 空态（负阴抱阳）：无结果给像样的空态，不许白板 -->
        <a-empty v-if="flatAll.length === 0 && !loading" description="未找到相关内容，换个关键词试试" :image-style="{ height: '48px' }" />
        <a-spin v-if="loading" class="gs-spin" />
      </template>
    </div>
  </a-modal>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onBeforeUnmount } from 'vue';
import { useRouter } from 'vue-router';
import { AppstoreOutlined, MenuOutlined, UserOutlined, NotificationOutlined } from '@ant-design/icons-vue';
import { useUserStore } from '@/stores/user';
import { getUserPage, type UserItem } from '@/api/system';
import { getAnnouncementPage, type AnnouncementItem } from '@/api/announcement';
import { buildMenuItems, type RouteLike, type MenuNode } from '@/utils/menuBuilder';

const router = useRouter();
const userStore = useUserStore();

/** v-model:visible：父层（顶栏按钮）可唤起；内部 Esc/选中后关闭 */
const visible = defineModel<boolean>('visible', { required: true });

const keyword = ref('');
const loading = ref(false);

interface GsItem {
  key: string;
  label: string;
  sub?: string;
  path: string;
  flatIndex: number;
}
interface GsGroup {
  key: 'menu' | 'user' | 'notice';
  title: string;
  icon: any;
  items: GsItem[];
}

const quickEntries = [
  { title: '首页', path: '/dashboard' },
  { title: '个人中心', path: '/profile' },
];

/** 菜单全集：单源 buildMenuItems（与侧栏同口径），拉平供搜索 */
const menuFlat = computed<GsItem[]>(() => {
  const allRoutes = [...router.getRoutes().values()] as unknown as RouteLike[];
  const tree = buildMenuItems(allRoutes);
  const out: GsItem[] = [];
  const walk = (nodes: MenuNode[]) => {
    for (const n of nodes) {
      if (n.path) out.push({ key: n.path, label: n.meta?.title || n.path, path: n.path, flatIndex: 0 });
      if (n.children?.length) walk(n.children);
    }
  };
  walk(tree);
  return out;
});

const groups = ref<GsGroup[]>([]);
const flatAll = computed<GsItem[]>(() => groups.value.flatMap((g) => g.items));

function matchMenus(kw: string): GsItem[] {
  const k = kw.toLowerCase();
  return menuFlat.value
    .filter((m) => m.label.toLowerCase().includes(k) || m.path.toLowerCase().includes(k))
    .slice(0, 8);
}

/** 快捷键：Ctrl+K / Cmd+K 唤起，Esc 关闭（焦点不在输入框时也要能关） */
function onGlobalKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault();
    visible.value = !visible.value;
    if (visible.value) void nextTick(() => onOpened());
  } else if (e.key === 'Escape' && visible.value) {
    visible.value = false;
  }
}

onMounted(() => window.addEventListener('keydown', onGlobalKeydown));
onBeforeUnmount(() => window.removeEventListener('keydown', onGlobalKeydown));

function onOpened() {
  // antd Modal 打开时会把焦点交给弹层包裹 div（无障碍聚焦），晚于我们的首次 focus；
  // 用 rAF 重试确认焦点真正落在输入框上，最多 5 次
  void nextTick(() => {
    const focusInput = (tries = 0) => {
      inputRef.value?.focus?.();
      if (document.activeElement?.tagName !== 'INPUT' && tries < 5) {
        requestAnimationFrame(() => focusInput(tries + 1));
      }
    };
    focusInput();
  });
}

const inputRef = ref<{ focus: () => void } | null>(null);

let debounceTimer: ReturnType<typeof setTimeout> | null = null;
let seq = 0; // 竞态丢弃：只认最后一次输入（盲区清单兜底）

watch(keyword, (kw) => {
  if (debounceTimer) clearTimeout(debounceTimer);
  flatIndex.value = 0;
  if (!kw.trim()) {
    groups.value = [];
    loading.value = false;
    return;
  }
  loading.value = true;
  debounceTimer = setTimeout(() => void search(kw.trim()), 300);
});

async function search(kw: string) {
  const mySeq = ++seq;
  const menuItems: GsItem[] = matchMenus(kw).map((m, i) => ({ ...m, flatIndex: i }));
  const gs: GsGroup[] = [{ key: 'menu', title: '菜单与页面', icon: MenuOutlined, items: menuItems }];

  // 用户组：仅管理员/有用户权限时可见（按 userStore 权限门控）
  const canUsers = userStore.hasPermission() && menuFlat.value.some((m) => m.path === '/system/user');
  // 公告组：登录即可见（与 latest 同口径）
  const canNotice = menuFlat.value.some((m) => m.path === '/system/announcement') || true;

  const [users, notices] = await Promise.allSettled([
    canUsers ? getUserPage({ keyword: kw, pageSize: 5 }) : Promise.resolve(null),
    canNotice ? getAnnouncementPage({ keyword: kw, pageSize: 5 }) : Promise.resolve(null),
  ]);

  if (seq !== mySeq) return; // 竞态丢弃

  let base = menuItems.length;
  if (users.status === 'fulfilled' && users.value) {
    const items = (users.value as { items: UserItem[] }).items.map((u, i) => ({
      key: u.id,
      label: u.nickname ? `${u.nickname}（${u.username}）` : u.username,
      sub: u.email || '',
      path: '/system/user',
      flatIndex: base + i,
    }));
    gs.push({ key: 'user', title: '用户', icon: UserOutlined, items });
    base += items.length;
  }
  if (notices.status === 'fulfilled' && notices.value) {
    const items = (notices.value as { items: AnnouncementItem[] }).items.map((a, i) => ({
      key: a.id,
      label: a.title,
      sub: a.publishTime || a.createdAt,
      path: '/system/announcement',
      flatIndex: base + i,
    }));
    gs.push({ key: 'notice', title: '公告', icon: NotificationOutlined, items });
  }
  groups.value = gs;
  loading.value = false;
}

const flatIndex = ref(0);

watch(flatAll, (all) => {
  if (flatIndex.value >= all.length) flatIndex.value = Math.max(0, all.length - 1);
});

function onKeydown(e: KeyboardEvent) {
  const all = flatAll.value;
  if (e.key === 'ArrowDown') {
    e.preventDefault();
    if (all.length) flatIndex.value = (flatIndex.value + 1) % all.length;
  } else if (e.key === 'ArrowUp') {
    e.preventDefault();
    if (all.length) flatIndex.value = (flatIndex.value - 1 + all.length) % all.length;
  } else if (e.key === 'Enter') {
    const target = all[flatIndex.value];
    if (target) {
      e.preventDefault();
      go(target.path);
    }
  } else if (e.key === 'Escape') {
    visible.value = false;
  }
}

function go(path: string) {
  visible.value = false;
  keyword.value = '';
  groups.value = [];
  router.push(path);
}
</script>

<style scoped lang="less">
.gs-groups {
  margin-top: 8px;
  max-height: 50vh;
  overflow-y: auto;
}

.gs-group-title {
  padding: 8px 8px 4px;
  font-size: 12px;
  color: rgba(0, 0, 0, 0.45);
}

.gs-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 14px;

  &:hover,
  &.active {
    background: rgba(0, 0, 0, 0.04);
  }

  .gs-icon {
    color: rgba(0, 0, 0, 0.45);
  }

  .gs-label {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .gs-sub {
    color: rgba(0, 0, 0, 0.45);
    font-size: 12px;
  }
}

.gs-spin {
  display: block;
  text-align: center;
  padding: 12px;
}
</style>

<style lang="less">
/* 锁屏下搜索浮层被 z-index 2000 全局遮罩盖住，语义一致（锁屏态不给搜索） */
</style>
