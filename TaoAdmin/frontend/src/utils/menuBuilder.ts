/**
 * 侧栏菜单构建（单一来源，编码规范〇·七.3 归一）。
 * 历史坑（第三班遗留）：layout 里三个来源拼接（Root 动态子路由 / 顶层 dir 路由 /
 * matched[0].children||Root.children），目录页激活时 matched[0]=dir 路由，
 * 其 children 与来源二完全重复，Root 挂载菜单也在静态数组与 getRoutes 各出现一次 → 菜单重复渲染。
 * 修法：只认 router.getRoutes() 一个来源，按 path 去重（T4-06）。
 */

export interface MenuNode {
  path?: string;
  name?: string;
  meta?: { title?: string; icon?: string; hidden?: boolean };
  children?: MenuNode[];
}

export interface RouteLike {
  path: string;
  name?: unknown;
  meta?: { title?: string; icon?: string; hidden?: boolean };
  children?: RouteLike[];
  parent?: { name?: unknown } | null;
}

function toNode(r: RouteLike): MenuNode {
  return {
    path: r.path,
    name: typeof r.name === 'string' ? r.name : undefined,
    meta: r.meta,
    children: r.children?.filter((c) => !c.meta?.hidden).map((c) => toMenuItem(c)),
  };
}

function toMenuItem(r: RouteLike): MenuNode {
  return {
    path: r.path,
    name: typeof r.name === 'string' ? r.name : undefined,
    meta: r.meta,
  };
}

/**
 * 从全部注册路由构建菜单树：
 * ① parent==Root 的非 hidden 路由 → 顶层菜单项（含静态首页与动态挂载菜单）；
 * ② 顶层 dir-* 路由（自带 layout 壳）→ 目录项，子级过滤 hidden。
 * 任何路径只出现一次（按 path 去重，先到先得）。
 */
export function buildMenuItems(routes: RouteLike[]): MenuNode[] {
  const rootMenus: MenuNode[] = [];
  const dirMenus: MenuNode[] = [];
  const seen = new Set<string>();

  for (const r of routes) {
    if (r.meta?.hidden || !r.meta?.title) continue;
    const isDir = typeof r.name === 'string' && r.name.startsWith('dir-');
    const isRootChild = r.parent?.name !== undefined && r.parent.name === 'Root' && !isDir;
    if (!isDir && !isRootChild) continue;

    const node = isDir ? toNode(r) : toMenuItem(r);
    const key = node.path as string;
    if (seen.has(key)) continue;
    seen.add(key);
    (isDir ? dirMenus : rootMenus).push(node);
  }

  return [...rootMenus, ...dirMenus];
}
