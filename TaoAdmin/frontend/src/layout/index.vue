<template>
  <a-layout class="app-layout">
    <!-- 侧边栏：窄屏（<768px）收进抽屉，避免挤压内容区（响应式零变形令） -->
    <a-layout-sider
      v-if="!isNarrow"
      v-model:collapsed="collapsed"
      :trigger="null"
      collapsible
      width="220"
      theme="dark"
      class="app-sider"
    >
      <div class="logo">
        <img v-if="siteStore.config.siteLogo" :src="siderLogoUrl" alt="站点 Logo" class="logo-img" />
        <span v-else class="logo-icon">⚙️</span>
        <span v-if="!collapsed" class="logo-text">{{ siteStore.config.siteName }}</span>
      </div>
      <a-menu
        v-model:selectedKeys="selectedKeys"
        v-model:openKeys="openKeys"
        mode="inline"
        theme="dark"
        :items="menuItems"
        @click="onMenuClick"
      />
    </a-layout-sider>
    <a-drawer
      v-else
      :open="siderDrawerOpen"
      placement="left"
      :width="264"
      :closable="false"
      class="sider-drawer"
      :body-style="{ padding: 0, 'overflow-y': 'auto' }"
      @close="siderDrawerOpen = false"
    >
      <div class="logo">
        <img v-if="siteStore.config.siteLogo" :src="siderLogoUrl" alt="站点 Logo" class="logo-img" />
        <span v-else class="logo-icon">⚙️</span>
        <span class="logo-text">{{ siteStore.config.siteName }}</span>
      </div>
      <a-menu
        v-model:selectedKeys="selectedKeys"
        v-model:openKeys="openKeys"
        mode="inline"
        theme="dark"
        :items="menuItems"
        @click="onMenuClick"
      />
    </a-drawer>

    <a-layout class="app-main">
      <!-- 顶栏 -->
      <a-layout-header class="app-header" :class="{ 'app-header-dark': themeStore.isDark }">
        <div class="header-left">
          <a-button type="text" @click="isNarrow ? (siderDrawerOpen = !siderDrawerOpen) : (collapsed = !collapsed)">
            <template #icon>
              <MenuUnfoldOutlined v-if="collapsed" />
              <MenuFoldOutlined v-else />
            </template>
          </a-button>
          <!-- 面包屑 -->
          <a-breadcrumb>
            <a-breadcrumb-item>
              <router-link to="/dashboard">首页</router-link>
            </a-breadcrumb-item>
            <a-breadcrumb-item v-for="item in breadcrumbs" :key="item.path">
              {{ item.title }}
            </a-breadcrumb-item>
          </a-breadcrumb>
        </div>
        <div class="header-right">
          <!-- 全局搜索入口：按钮 + Ctrl+K 快捷键（第五班路一） -->
          <a-tooltip title="搜索（Ctrl+K）">
            <button class="theme-toggle" type="button" @click="searchVisible = true">
              <SearchOutlined class="search-trigger-icon" />
            </button>
          </a-tooltip>

          <!-- 一键换肤：明暗切换（偏好存 theme store / localStorage） -->
          <a-tooltip :title="themeStore.isDark ? '切换亮色' : '切换暗色'">
            <button class="theme-toggle" type="button" @click="themeStore.toggleMode()">
              <span class="theme-toggle-icon">{{ themeStore.isDark ? '☀️' : '🌙' }}</span>
            </button>
          </a-tooltip>

          <!-- 一键换肤：主色可选（色板点选全局生效） -->
          <a-popover trigger="click" placement="bottomRight">
            <template #content>
              <div class="primary-swatches">
                <span class="primary-swatches-title">主题色</span>
                <div class="primary-swatches-row">
                  <button
                    v-for="preset in PRIMARY_PRESETS"
                    :key="preset.key"
                    type="button"
                    class="swatch"
                    :class="{ active: themeStore.primary === preset.value }"
                    :style="{ background: preset.value }"
                    :title="preset.name"
                    @click="themeStore.setPrimary(preset.value)"
                  />
                </div>
              </div>
            </template>
            <button class="theme-toggle" type="button" title="主题色">
              <span class="theme-toggle-swatch" :style="{ background: themeStore.primary }" />
            </button>
          </a-popover>

          <!-- 公告通知入口：登录后可见最新公告，铃铛角标显示条数 -->
          <a-popover v-model:open="noticeOpen" trigger="click" placement="bottomRight" class="notice-pop">
            <template #content>
              <div class="notice-panel">
                <div class="notice-title">最新公告</div>
                <a-spin :spinning="noticeLoading">
                  <template v-if="announcements.length">
                    <div
                      v-for="a in announcements"
                      :key="a.id"
                      class="notice-item"
                      @click="openNoticeDetail(a)"
                    >
                      <div class="notice-item-title">
                        <a-tag v-if="isToday(a.publishTime || a.createdAt)" color="red" class="notice-new">新</a-tag>
                        {{ a.title }}
                      </div>
                      <div class="notice-item-time">{{ a.publishTime || a.createdAt }}</div>
                    </div>
                  </template>
                  <a-empty v-else description="暂无公告" :image-style="{ height: '48px' }" />
                </a-spin>
              </div>
            </template>
            <a-badge :count="announcements.length" :offset="[-2, 2]" size="small">
              <BellOutlined class="notice-bell" />
            </a-badge>
          </a-popover>

          <a-dropdown>
            <div class="user-info">
              <a-avatar size="small" :src="headerAvatarUrl">
                {{ userStore.nickname.charAt(0) }}
              </a-avatar>
              <span class="user-name">{{ userStore.nickname }}</span>
            </div>
            <template #overlay>
              <a-menu>
                <a-menu-item key="profile" @click="router.push('/profile')">
                  <UserOutlined /> 个人中心
                </a-menu-item>
                <a-menu-item key="lock" @click="onLockScreen">
                  <LockOutlined /> 锁定屏幕
                </a-menu-item>
                <a-menu-divider />
                <a-menu-item key="logout" @click="onLogout">
                  <LogoutOutlined /> 退出登录
                </a-menu-item>
              </a-menu>
            </template>
          </a-dropdown>
        </div>
      </a-layout-header>

      <!-- 内容区：多页签导航 + keep-alive 缓存已访问页面；页签支持右键批量关闭 -->
      <div class="tab-bar" :class="{ 'tab-bar-dark': themeStore.isDark }">
        <a-dropdown v-for="tab in tabsStore.tabs" :key="tab.path" :trigger="['contextMenu']">
          <div
            class="tab-item"
            :class="{ active: tab.path === tabsStore.active }"
            @click="onTabClick(tab.path)"
          >
            <span class="tab-title">{{ tab.title }}</span>
            <close-outlined
              v-if="!tab.affix"
              class="tab-close"
              @click.stop="onTabClose(tab.path)"
            />
          </div>
          <template #overlay>
            <a-menu @click="onTabMenuClick($event.key, tab.path)">
              <a-menu-item key="closeLeft" :disabled="closableLeftCount(tab.path) === 0">关闭左侧</a-menu-item>
              <a-menu-item key="closeRight" :disabled="closableRightCount(tab.path) === 0">关闭右侧</a-menu-item>
              <a-menu-item key="closeOthers" :disabled="closableOthersCount(tab.path) === 0">关闭其他</a-menu-item>
              <a-menu-item key="closeAll" :disabled="closableAllCount() === 0">全部关闭</a-menu-item>
            </a-menu>
          </template>
        </a-dropdown>
      </div>
      <a-layout-content class="app-content" :class="{ 'app-content-dark': themeStore.isDark }">
        <router-view v-slot="{ Component, route: viewRoute }">
          <keep-alive>
            <component :is="Component" :key="viewRoute.path" />
          </keep-alive>
        </router-view>
      </a-layout-content>

      <a-layout-footer class="app-footer" v-if="siteStore.config.siteFooter">
        {{ siteStore.config.siteFooter }}
      </a-layout-footer>
    </a-layout>

    <!-- 公告详情弹窗（顶栏通知点击展开） -->
    <a-modal v-model:open="noticeDetailVisible" :title="noticeDetail?.title || '公告详情'" :footer="null" width="560">
      <div class="notice-detail-meta">
        <span class="notice-detail-publisher">{{ noticeDetail?.publisherName || '系统' }}</span>
        <span class="notice-detail-time">{{ noticeDetail?.publishTime || noticeDetail?.createdAt }}</span>
      </div>
      <div class="notice-detail-content">{{ noticeDetail?.content || '（无正文）' }}</div>
    </a-modal>

    <!-- 第五班路一：全局搜索 / 锁屏遮罩 / 页面水印 -->
    <GlobalSearch v-model:visible="searchVisible" />
    <LockScreen />
    <WatermarkLayer />

    <!-- 强制改密弹窗：密码策略判定过期后登录即弹，改完重登，期间不可绕过 -->
    <a-modal
      :open="mustChangePwd"
      title="密码已过期，请修改密码"
      :width="460"
      :closable="false"
      :mask-closable="false"
      :keyboard="false"
      :footer="null"
    >
      <a-alert
        message="为保障账号安全，请先修改密码再继续使用系统"
        type="warning"
        show-icon
        style="margin-bottom: 16px"
      />
      <a-form ref="forcePwdFormRef" :model="forcePwdForm" :rules="forcePwdRules" layout="vertical">
        <a-form-item label="当前密码" name="oldPassword">
          <a-input-password v-model:value="forcePwdForm.oldPassword" placeholder="输入当前密码" />
        </a-form-item>
        <a-form-item label="新密码" name="newPassword" extra="按密码策略设置：足够长度且包含多类字符">
          <a-input-password v-model:value="forcePwdForm.newPassword" placeholder="输入新密码" />
        </a-form-item>
        <a-form-item label="确认新密码" name="confirm">
          <a-input-password v-model:value="forcePwdForm.confirm" placeholder="再次输入新密码" />
        </a-form-item>
      </a-form>
      <a-button type="primary" block :loading="forcePwdSaving" @click="onForceChangePwd">
        修改密码并重新登录
      </a-button>
    </a-modal>
  </a-layout>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch, h, onMounted, onBeforeUnmount } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { message } from 'ant-design-vue';
import type { ItemType } from 'ant-design-vue';
import * as AntIcons from '@ant-design/icons-vue';
import {
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  UserOutlined,
  LogoutOutlined,
  BellOutlined,
  CloseOutlined,
  SearchOutlined,
  LockOutlined,
} from '@ant-design/icons-vue';
import { useUserStore } from '@/stores/user';
import { useSiteStore } from '@/api/site';
import { useThemeStore, PRIMARY_PRESETS } from '@/stores/theme';
import { useTabsStore } from '@/stores/tabs';
import { useLockStore } from '@/stores/lock';
import { buildMenuItems, type RouteLike, type MenuNode } from '@/utils/menuBuilder';
import { buildFileUrl } from '@/api/taoFiles';
import { getLatestAnnouncements, type AnnouncementItem } from '@/api/announcement';
import GlobalSearch from '@/components/GlobalSearch.vue';
import LockScreen from '@/components/LockScreen.vue';
import WatermarkLayer from '@/components/WatermarkLayer.vue';
import { changeMyPassword } from '@/api/profile';

const route = useRoute();
const router = useRouter();
const userStore = useUserStore();
const siteStore = useSiteStore();
const themeStore = useThemeStore();
const tabsStore = useTabsStore();
const lockStore = useLockStore();

// ============ 全局搜索 / 锁屏入口（第五班路一） ============
const searchVisible = ref(false);

function onLockScreen() {
  lockStore.lock();
}

// 主布局门面用站点参数；刷新直达时这里也兜底拉一次
onMounted(() => {
  if (!siteStore.loaded) void siteStore.fetchConfig();
  void loadAnnouncements();
});

// ============ 公告通知入口 ============

const noticeOpen = ref(false);
const noticeLoading = ref(false);
const announcements = ref<AnnouncementItem[]>([]);

async function loadAnnouncements() {
  noticeLoading.value = true;
  try {
    announcements.value = (await getLatestAnnouncements()) || [];
  } catch {
    // 公告拉取失败不阻塞主界面，铃铛角标按 0 处理
  } finally {
    noticeLoading.value = false;
  }
}

const noticeDetailVisible = ref(false);
const noticeDetail = ref<AnnouncementItem | null>(null);

function openNoticeDetail(a: AnnouncementItem) {
  noticeDetail.value = a;
  noticeDetailVisible.value = true;
}

function isToday(time?: string): boolean {
  if (!time) return false;
  const d = new Date(time);
  return !Number.isNaN(d.getTime()) && d.toDateString() === new Date().toDateString();
}

const siderLogoUrl = computed(() =>
  /^https?:\/\//.test(siteStore.config.siteLogo)
    ? siteStore.config.siteLogo
    : buildFileUrl(siteStore.config.siteLogo)
);

/** 顶栏头像地址：完整 URL 直接用，相对路径拼文件中心地址 */
const headerAvatarUrl = computed(() => {
  const a = userStore.avatar;
  if (!a) return '';
  return /^https?:\/\//.test(a) ? a : buildFileUrl(a);
});

const collapsed = ref(false);
/** 窄屏（<768px）判定：侧栏转抽屉（响应式零变形令，2026-11-18） */
const isNarrow = ref(false);
const siderDrawerOpen = ref(false);
let mediaQuery: MediaQueryList | null = null;
function syncNarrow() {
  isNarrow.value = window.matchMedia('(max-width: 767px)').matches;
  if (!isNarrow.value) siderDrawerOpen.value = false;
}
syncNarrow();
mediaQuery = window.matchMedia('(max-width: 767px)');
mediaQuery.addEventListener('change', syncNarrow);
onBeforeUnmount(() => mediaQuery?.removeEventListener('change', syncNarrow));
const selectedKeys = ref<string[]>([route.path]);
const openKeys = ref<string[]>([]);

/**
 * 图标扩展表：菜单图标名 → 图标库实际组件名。
 * 图标库没有的语义名（如 Timer）在这里登记一行即可生效，无需改菜单数据——
 * 图标解析支持扩展，新增映射走这张表（老板令 2026-11-22）。
 */
const ICON_EXTENSIONS: Record<string, string> = {
  Timer: 'ClockCircleOutlined',
};

function renderIcon(name?: string) {
  if (!name) return undefined;
  // 菜单图标库存的是语义名（如 Setting/FolderOpen），图标组件实际导出 XxxOutlined；
  // 库里查不到时按扩展表换算，仍无则不渲染（不报错）
  const icon =
    (AntIcons as any)[name] ||
    (AntIcons as any)[`${name}Outlined`] ||
    (ICON_EXTENSIONS[name] ? (AntIcons as any)[ICON_EXTENSIONS[name]] : undefined);
  return icon ? () => h(icon) : undefined;
}

/**
 * vue-router 5 的 RouteRecordNormalized 不再暴露 parent 字段（升级回归根因，
 * 曾导致所有挂 Root 的一级菜单——含首页/文件管理——被 menuBuilder 过滤掉）。
 * 这里用 resolve 反查 matched 推父级，交回 menuBuilder 原有判定，不改其契约。
 */
function parentNameOf(path: string): unknown {
  try {
    const matched = router.resolve(path).matched;
    if (matched.length >= 2) return matched[matched.length - 2]?.name;
  } catch {
    // 解析失败按无父级处理（菜单不渲染，不影响路由本身）
  }
  return undefined;
}

const menuItems = computed<ItemType[]>(() => {
  // 单一来源：router.getRoutes()（菜单重复修复，见 utils/menuBuilder.ts 注释）
  const allRoutes = ([...router.getRoutes().values()] as unknown as RouteLike[]).map((r) => ({
    ...r,
    parent: { name: parentNameOf(r.path) },
  }));
  const menuTree = buildMenuItems(allRoutes);
  return toMenuItems(menuTree);
});

function toMenuItems(nodes: MenuNode[]): ItemType[] {
  return nodes
    .filter((n) => !n.meta?.hidden && n.meta?.title)
    .map((n) => ({
      key: n.path as string,
      icon: renderIcon(n.meta?.icon),
      label: n.meta?.title,
      children: n.children?.length ? toMenuItems(n.children) : undefined,
    }));
}

// ============ 多页签导航 ============

/** 路由进入时同步页签：除登录/404 外，布局内页面统一开页签（hidden 只管侧边栏显隐，老板反馈③统一补齐） */
watch(
  () => route.fullPath,
  () => {
    if (route.name === 'Login' || route.name === 'NotFound') return;
    const title = route.meta?.title as string | undefined;
    if (title) tabsStore.openTab(route.path, title);
  },
  { immediate: true }
);

function onTabClick(path: string) {
  if (path !== route.path) router.push(path);
  tabsStore.activate(path);
}

function onTabClose(path: string) {
  const wasActive = tabsStore.active === path;
  tabsStore.closeTab(path);
  if (wasActive) router.push(tabsStore.active);
}

// ============ 页签右键批量关闭（老板反馈②） ============

/** 锚点左侧可关闭页签数 */
function closableLeftCount(path: string): number {
  const idx = tabsStore.tabs.findIndex((t) => t.path === path);
  return tabsStore.tabs.slice(0, Math.max(idx, 0)).filter((t) => !t.affix).length;
}

/** 锚点右侧可关闭页签数 */
function closableRightCount(path: string): number {
  const idx = tabsStore.tabs.findIndex((t) => t.path === path);
  return idx < 0 ? 0 : tabsStore.tabs.slice(idx + 1).filter((t) => !t.affix).length;
}

/** 除锚点与固定页签外可关闭页签数 */
function closableOthersCount(path: string): number {
  return tabsStore.tabs.filter((t) => !t.affix && t.path !== path).length;
}

/** 全部可关闭页签数 */
function closableAllCount(): number {
  return tabsStore.tabs.filter((t) => !t.affix).length;
}

function onTabMenuClick(action: string, path: string) {
  switch (action) {
    case 'closeLeft':
      tabsStore.closeLeft(path);
      break;
    case 'closeRight':
      tabsStore.closeRight(path);
      break;
    case 'closeOthers':
      tabsStore.closeOthers(path);
      break;
    case 'closeAll':
      tabsStore.closeAll();
      break;
    default:
      return;
  }
  // 批量关闭后激活页签可能变化，路由跟着走
  if (route.path !== tabsStore.active) router.push(tabsStore.active);
}

function onMenuClick({ key }: { key: string }) {
  router.push(key as string);
}

/** 面包屑：基于 matched 路由 */
const breadcrumbs = computed(() =>
  route.matched
    .filter((r) => r.meta?.title && r.path !== '/')
    .map((r) => ({ path: r.path, title: r.meta?.title as string }))
);

// 同步选中菜单
watch(
  () => route.path,
  (path) => {
    selectedKeys.value = [path];
    // 展开父级目录
    route.matched.forEach((r) => {
      if (r.path && r.children?.length && !openKeys.value.includes(r.path)) {
        openKeys.value = [...openKeys.value, r.path];
      }
    });
  },
  { immediate: true }
);

async function onLogout() {
  await userStore.logout();
  router.push('/login');
}

// ============ 强制改密（密码策略过期） ============
const forcePwdFormRef = ref();
const forcePwdSaving = ref(false);
const forcePwdForm = reactive({ oldPassword: '', newPassword: '', confirm: '' });

/** 后端登录/取用户信息均带 mustChangePassword：过期即弹，改完重登前不可绕过 */
const mustChangePwd = computed(() => userStore.userInfo?.mustChangePassword === true);

const forcePwdRules = {
  oldPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 6, message: '新密码至少 6 位（具体以密码策略为准）', trigger: 'blur' },
  ],
  confirm: [
    { required: true, message: '请再次输入新密码', trigger: 'blur' },
    {
      validator: (_rule: any, value: string) =>
        value === forcePwdForm.newPassword
          ? Promise.resolve()
          : Promise.reject(new Error('两次输入的新密码不一致')),
      trigger: 'blur',
    },
  ],
};

async function onForceChangePwd() {
  try {
    await forcePwdFormRef.value?.validate();
  } catch {
    return;
  }
  forcePwdSaving.value = true;
  try {
    await changeMyPassword({ oldPassword: forcePwdForm.oldPassword, newPassword: forcePwdForm.newPassword });
    message.success('密码修改成功，请用新密码重新登录');
    await userStore.logout();
    router.push('/login');
  } catch {
    // 策略拒收等错误提示已由 request.ts 统一给出
  } finally {
    forcePwdSaving.value = false;
  }
}
</script>

<style scoped lang="less">
/* 全局原则（老板反馈①④）：整页固定一屏高，任何情况不出最外层滚动条；
   侧边栏与内容区各自内部滚动 */
.app-layout {
  height: 100vh;
  overflow: hidden;
}

/* 主区（顶栏+页签栏+内容+页脚）纵向排布，内容区弹性占满并内部滚动 */
.app-main {
  height: 100vh;
  overflow: hidden;

  > .app-header,
  > .tab-bar {
    flex-shrink: 0;
  }
}

.app-sider {
  /* 侧边栏固定一屏高，菜单超出内部滚动 */
  :deep(.ant-layout-sider-children) {
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
  }

  .logo {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 48px;
    color: #fff;
    font-size: 18px;
    font-weight: 600;
    background: rgba(255, 255, 255, 0.08);
    margin-bottom: 4px;

    .logo-icon {
      margin-right: 8px;
    }

    .logo-img {
      height: 28px;
      max-width: 120px;
      object-fit: contain;
      margin-right: 8px;
    }
  }
}

.app-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 21, 41, 0.08);
  z-index: 10;

  &.app-header-dark {
    background: #141414;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
  }

  .header-left {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .header-right {
    display: flex;
    align-items: center;
    gap: 16px;

    .notice-pop {
      display: flex;
      align-items: center;
    }

    .notice-bell {
      font-size: 18px;
      color: rgba(0, 0, 0, 0.65);
      cursor: pointer;

      &:hover {
        color: #1677ff;
      }
    }

    .user-info {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      padding: 0 8px;

      .user-name {
        font-size: 14px;
      }
    }

    /* 极窄屏（≤420px）：收敛非关键顶栏内容，防横向溢出（响应式零变形令） */
    @media (max-width: 420px) {
      gap: 8px;

      .user-info {
        padding: 0 4px;

        .user-name {
          display: none;
        }
      }
    }

    .theme-toggle {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      border: none;
      border-radius: 6px;
      background: transparent;
      cursor: pointer;

      &:hover {
        background: rgba(0, 0, 0, 0.06);
      }

      .theme-toggle-icon {
        font-size: 16px;
        line-height: 1;
      }

      .theme-toggle-swatch {
        width: 16px;
        height: 16px;
        border-radius: 4px;
        display: block;
      }
    }
  }
}

.app-content {
  margin: 12px 16px 16px;
  /* 内容区固定一屏内的高度，超出内部滚动；分页列表再自滚（老板反馈①） */
  flex: 1;
  min-height: 0;
  overflow: auto;

  > * {
    background: #fff;
    border-radius: 4px;
  }

  &.app-content-dark > * {
    background: transparent;
  }
}

// ============ 多页签导航栏 ============
.primary-swatches {
  .primary-swatches-title {
    display: block;
    font-weight: 600;
    margin-bottom: 8px;
  }

  .primary-swatches-row {
    display: flex;
    gap: 8px;
  }

  .swatch {
    width: 24px;
    height: 24px;
    border-radius: 6px;
    border: 2px solid transparent;
    cursor: pointer;

    &.active {
      border-color: rgba(0, 0, 0, 0.45);
      box-shadow: 0 0 0 2px rgba(0, 0, 0, 0.08);
    }
  }
}

.tab-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px 0;
  background: #fff;
  box-shadow: 0 1px 4px rgba(0, 21, 41, 0.08);
  overflow-x: auto;

  &.tab-bar-dark {
    background: #141414;

    .tab-item {
      background: rgba(255, 255, 255, 0.06);
      color: rgba(255, 255, 255, 0.65);

      &.active {
        background: rgba(255, 255, 255, 0.15);
        color: #fff;
      }
    }
  }

  .tab-item {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 5px 12px;
    border-radius: 4px 4px 0 0;
    background: rgba(0, 0, 0, 0.04);
    font-size: 13px;
    color: rgba(0, 0, 0, 0.65);
    cursor: pointer;
    white-space: nowrap;
    user-select: none;

    &:hover .tab-close {
      opacity: 1;
    }

    &.active {
      background: rgba(0, 0, 0, 0.08);
      color: rgba(0, 0, 0, 0.88);
      font-weight: 500;
    }

    .tab-close {
      font-size: 10px;
      opacity: 0.45;
      transition: opacity 0.2s;

      &:hover {
        opacity: 1;
      }
    }
  }
}

// 公告通知面板与详情
.notice-panel {
  width: 320px;
  max-height: 380px;
  overflow-y: auto;

  .notice-title {
    font-weight: 600;
    margin-bottom: 8px;
  }

  .notice-item {
    padding: 8px 4px;
    border-bottom: 1px solid rgba(0, 0, 0, 0.06);
    cursor: pointer;

    &:hover {
      background: rgba(0, 0, 0, 0.03);
    }

    .notice-item-title {
      font-size: 14px;
      color: rgba(0, 0, 0, 0.85);
    }

    .notice-item-time {
      margin-top: 2px;
      font-size: 12px;
      color: rgba(0, 0, 0, 0.45);
    }

    .notice-new {
      margin-right: 6px;
    }
  }
}

.notice-detail-meta {
  margin-bottom: 12px;

  .notice-detail-publisher {
    margin-right: 12px;
    color: rgba(0, 0, 0, 0.65);
    font-size: 13px;
  }

  .notice-detail-time {
    color: rgba(0, 0, 0, 0.45);
    font-size: 13px;
  }
}

.notice-detail-content {
  white-space: pre-wrap;
  line-height: 1.7;
  max-height: 50vh;
  overflow-y: auto;
}

.app-footer {
  text-align: center;
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}
</style>
