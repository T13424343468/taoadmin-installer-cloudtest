import {
  createRouter,
  createWebHistory,
  type RouteRecordRaw,
} from 'vue-router';
import { useUserStore } from '@/stores/user';
import { useSiteStore } from '@/api/site';
import { getToken } from '@/api/request';

/**
 * 静态路由：登录页、404、布局壳
 * 动态路由在登录后根据后端菜单树 addRoute 生成（见 generateDynamicRoutes）
 */
const staticRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/login/index.vue'),
    meta: { title: '登录', hidden: true },
  },
  {
    path: '/',
    name: 'Root',
    component: () => import('@/layout/index.vue'),
    redirect: '/dashboard',
    children: [
      {
        path: 'dashboard',
        name: 'Dashboard',
        component: () => import('@/views/dashboard/index.vue'),
        meta: { title: '首页', icon: 'DashboardOutlined' },
      },
      {
        // 个人中心：静态路由，不走后端菜单（任何登录用户可用），入口在顶栏头像下拉
        path: 'profile',
        name: 'Profile',
        component: () => import('@/views/profile/index.vue'),
        meta: { title: '个人中心', hidden: true },
      },
    ],
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'NotFound',
    component: () => import('@/views/error/404.vue'),
    meta: { title: '404', hidden: true },
  },
];

const router = createRouter({
  history: createWebHistory(),
  routes: staticRoutes,
});

/** 是否已生成动态路由（模块级标记，刷新后重新生成） */
let dynamicRoutesAdded = false;

/**
 * 根据后端菜单树递归生成路由
 * - 菜单 component 字段对应 views 下组件路径，如 "system/user/index"
 * - 目录（dir）不生成路由，只递归子级
 * - Visible=false 的菜单仍会生成路由（可通过 URL 访问），
 *   但不会出现在侧边栏（侧边栏渲染时按 visible 过滤），代码注释说明该约定。
 */
function mapMenuToRoute(menu: any, parentPath = ''): RouteRecordRaw | null {
  if (menu.type === 'dir') {
    // 目录节点：直接挂到父布局下作为空路由，仅用于聚合子路由
    const children = (menu.children || [])
      .map((c: any) => mapMenuToRoute(c, menu.path))
      .filter(Boolean);
    if (children.length === 0) return null;
    return {
      path: menu.path,
      name: `dir-${menu.id}`,
      component: () => import('@/layout/index.vue'),
      meta: { title: menu.name, icon: menu.icon, hidden: menu.visible === false },
      children,
    } as RouteRecordRaw;
  }

  if (menu.type === 'menu') {
    const fullPath = menu.path.startsWith('/') ? menu.path : `${parentPath}/${menu.path}`;
    const componentPath = menu.component || '';
    return {
      path: fullPath,
      name: `menu-${menu.id}`,
      component: loadComponent(componentPath),
      meta: {
        title: menu.name,
        icon: menu.icon,
        hidden: menu.visible === false, // Visible=false：可访问但不显示在侧边栏
      },
    } as RouteRecordRaw;
  }

  // 按钮类型不生成路由
  return null;
}

/** 动态加载视图组件（views 目录约定结构） */
function loadComponent(componentPath: string) {
  // 通过 import.meta.glob 懒加载所有视图
  const modules = import.meta.glob('@/views/**/*.vue');
  const key = `/src/views/${componentPath}.vue`;
  if (modules[key]) {
    return modules[key];
  }
  // 兜底：找不到组件时加载 404
  console.warn(`[router] 未找到组件: ${componentPath} -> ${key}`);
  return () => import('@/views/error/404.vue');
}

/** 登录后 / 刷新后生成动态路由 */
export async function generateDynamicRoutes() {
  if (dynamicRoutesAdded) return;
  const userStore = useUserStore();
  if (!userStore.menuTree.length) {
    await userStore.fetchUserInfo();
  }

  // 挂载到根布局下
  const rootRoute = router.getRoutes().find((r) => r.name === 'Root');
  const rootChildren = router.options.routes?.find((r) => r.name === 'Root')?.children || [];

  userStore.menuTree.forEach((menu) => {
    const route = mapMenuToRoute(menu, '');
    if (route) {
      // 目录节点自带 layout 壳，直接 addRoute；菜单节点挂到 Root 下
      if (route.children && route.children.length) {
        router.addRoute(route);
      } else {
        rootChildren.push(route);
        router.addRoute('Root', route as RouteRecordRaw);
      }
    }
  });
  void rootRoute;
  dynamicRoutesAdded = true;
}

/** 退出登录时清除动态路由标记（简单做法：刷新页面重建） */
export function resetDynamicRoutes() {
  dynamicRoutesAdded = false;
}

// ============ 全局守卫 ============

router.beforeEach(async (to, _from, next) => {
  const token = getToken();
  if (to.path === '/login') {
    next();
    return;
  }

  if (!token) {
    next(`/login?redirect=${encodeURIComponent(to.fullPath)}`);
    return;
  }

  // 已登录但尚未生成动态路由（首次进入或刷新）
  const userStore = useUserStore();
  if (!userStore.userInfo) {
    try {
      await generateDynamicRoutes();
      // 只带路径/查询/锚点重新进入，避免带入刷新前解析出的 NotFound 路由名
      next({ path: to.path, query: to.query, hash: to.hash, replace: true });
      return;
    } catch (e) {
      // 拉取用户信息失败（token 失效等），回登录页
      console.error('[router] 生成动态路由失败', e);
      userStore.resetState();
      next(`/login?redirect=${encodeURIComponent(to.fullPath)}`);
      return;
    }
  }

  next();
});

/** 浏览器标签页标题同步：路由切换与站点参数保存后统一走这里（单一出口，两处共用） */
export function applyDocumentTitle(routeTitle = '') {
  const title = routeTitle || '';
  // 浏览器标签页标题跟随站点参数（store 未初始化时退回默认）
  let siteName = 'TaoAdmin';
  try {
    siteName = useSiteStore().config.siteName || siteName;
  } catch {
    // pinia 尚未激活时使用默认站点名
  }
  document.title = title ? `${title} - ${siteName} 管理系统` : `${siteName} 管理系统`;
}

router.afterEach((to) => {
  applyDocumentTitle((to.meta?.title as string) || '');
});

export default router;
