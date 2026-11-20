import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTabsStore, HOME_TAB } from '@/stores/tabs';
import { useThemeStore, DEFAULT_PRIMARY } from '@/stores/theme';
import { buildMenuItems, type RouteLike } from '@/utils/menuBuilder';

describe('页签 store（T4-01~04, T4-17, T4-18）', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    sessionStorage.clear();
  });

  it('T4-01 新路由入栈且激活；重复打开不产生重复条目', () => {
    const s = useTabsStore();
    s.openTab('/system/user', '用户管理');
    expect(s.tabs.map((t) => t.path)).toEqual(['/dashboard', '/system/user']);
    expect(s.active).toBe('/system/user');
    s.openTab('/system/user', '用户管理');
    expect(s.tabs.length).toBe(2);
    expect(s.active).toBe('/system/user');
  });

  it('T4-02 关当前页签激活右邻，无右邻取左邻；关非当前页签不改变激活', () => {
    const s = useTabsStore();
    s.openTab('/a', 'A');
    s.openTab('/b', 'B');
    s.openTab('/c', 'C');
    // 当前 /c，关 /b（非当前）→ 激活仍是 /c
    s.closeTab('/b');
    expect(s.active).toBe('/c');
    // 当前 /c，关 /c → 右邻无，取左邻 /a
    s.closeTab('/c');
    expect(s.active).toBe('/a');
  });

  it('T4-03 固定页签不可关', () => {
    const s = useTabsStore();
    s.closeTab(HOME_TAB.path);
    expect(s.tabs.some((t) => t.path === HOME_TAB.path)).toBe(true);
  });

  it('T4-04 持久化与恢复', () => {
    const s = useTabsStore();
    s.openTab('/system/role', '角色管理');
    const snap = JSON.parse(sessionStorage.getItem('taoadmin-tabs') || '{}');
    expect(snap.tabs.length).toBe(2);
    expect(snap.active).toBe('/system/role');
    // 新 store 实例（模拟刷新后）从快照还原
    setActivePinia(createPinia());
    const s2 = useTabsStore();
    expect(s2.tabs.map((t) => t.path)).toEqual(['/dashboard', '/system/role']);
    expect(s2.active).toBe('/system/role');
  });

  it('T4-17 快照损坏回退为仅固定页签', () => {
    sessionStorage.setItem('taoadmin-tabs', '{not-json');
    setActivePinia(createPinia());
    const s = useTabsStore();
    expect(s.tabs.length).toBe(1);
    expect(s.tabs[0].path).toBe(HOME_TAB.path);
    expect(s.active).toBe(HOME_TAB.path);
  });

  it('T4-18 hidden 路由不进页签（由调用方过滤，store 侧校验非法条目被丢弃）', () => {
    sessionStorage.setItem('taoadmin-tabs', JSON.stringify({ tabs: [{ path: '/x', title: 'X' }, 'junk', null], active: '/x' }));
    setActivePinia(createPinia());
    const s = useTabsStore();
    expect(s.tabs.map((t) => t.path)).toEqual(['/dashboard', '/x']);
  });
});

describe('主题 store（T4-05, T4-16）', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
  });

  it('T4-05 默认值、切换与持久化、恢复', () => {
    const s = useThemeStore();
    expect(s.mode).toBe('light');
    expect(s.primary).toBe(DEFAULT_PRIMARY);
    s.setMode('dark');
    s.setPrimary('#722ed1');
    expect(localStorage.getItem('taoadmin-theme')).toContain('dark');
    setActivePinia(createPinia());
    const s2 = useThemeStore();
    expect(s2.mode).toBe('dark');
    expect(s2.primary).toBe('#722ed1');
  });

  it('T4-16 非法偏好回退默认且不抛', () => {
    localStorage.setItem('taoadmin-theme', '{"mode":123,"primary":"not-a-color"}');
    expect(() => {
      setActivePinia(createPinia());
      const s = useThemeStore();
      expect(s.mode).toBe('light');
      expect(s.primary).toBe(DEFAULT_PRIMARY);
    }).not.toThrow();
    localStorage.setItem('taoadmin-theme', '{broken');
    expect(() => {
      setActivePinia(createPinia());
      useThemeStore();
    }).not.toThrow();
  });
});

describe('菜单构建（T4-06 单源去重）', () => {
  const routes: RouteLike[] = [
    { path: '/dashboard', name: 'Dashboard', parent: { name: 'Root' }, meta: { title: '首页' } },
    { path: '/profile', name: 'Profile', parent: { name: 'Root' }, meta: { title: '个人中心', hidden: true } },
    { path: '/system/user', name: 'menu-11', parent: { name: 'Root' }, meta: { title: '用户管理' } },
    {
      path: '/system',
      name: 'dir-1',
      meta: { title: '系统管理' },
      children: [
        { path: '/system/user', name: 'menu-11', meta: { title: '用户管理' } },
        { path: '/system/role', name: 'menu-12', meta: { title: '角色管理' } },
        { path: '/system/x', name: 'menu-13', meta: { title: '隐藏页', hidden: true } },
      ],
    },
  ];

  it('T4-06 同一路径只渲染一次，hidden 不出现', () => {
    const items = buildMenuItems(routes);
    const paths = items.map((i) => i.path);
    expect(paths).toEqual(['/dashboard', '/system/user', '/system']);
    const dir = items.find((i) => i.path === '/system');
    expect(dir?.children?.map((c) => c.path)).toEqual(['/system/user', '/system/role']);
  });
});
