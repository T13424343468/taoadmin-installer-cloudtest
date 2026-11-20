import { defineStore } from 'pinia';

export interface TabItem {
  path: string;
  title: string;
  /** 固定页签：不可关闭（仪表盘） */
  affix?: boolean;
}

const STORAGE_KEY = 'taoadmin-tabs';
export const HOME_TAB: TabItem = { path: '/dashboard', title: '首页', affix: true };

function isTabItem(v: unknown): v is TabItem {
  if (!v || typeof v !== 'object') return false;
  const t = v as Partial<TabItem>;
  return typeof t.path === 'string' && t.path.length > 0 && typeof t.title === 'string';
}

/** 从 sessionStorage 恢复；快照缺失/损坏回退为仅固定页签（T4-17） */
function loadSnapshot(): { tabs: TabItem[]; active: string } {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return { tabs: [{ ...HOME_TAB }], active: HOME_TAB.path };
    const parsed = JSON.parse(raw) as { tabs?: unknown; active?: unknown };
    const tabs = Array.isArray(parsed.tabs) ? parsed.tabs.filter(isTabItem) : [];
    if (!tabs.some((t) => t.affix)) tabs.unshift({ ...HOME_TAB });
    const active = typeof parsed.active === 'string' && tabs.some((t) => t.path === parsed.active)
      ? parsed.active
      : HOME_TAB.path;
    return { tabs, active };
  } catch {
    return { tabs: [{ ...HOME_TAB }], active: HOME_TAB.path };
  }
}

function persist(tabs: TabItem[], active: string): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ tabs, active }));
  } catch {
    // 存不进只影响刷新恢复，不阻塞页签使用
  }
}

export const useTabsStore = defineStore('tabs', {
  state: () => {
    const snap = loadSnapshot();
    return { tabs: snap.tabs, active: snap.active } as { tabs: TabItem[]; active: string };
  },
  actions: {
    /** 路由进入时调用：新页面入栈，已存在只激活（T4-01） */
    openTab(path: string, title: string) {
      if (path === HOME_TAB.path) {
        this.active = path;
        persist(this.tabs, this.active);
        return;
      }
      const existing = this.tabs.find((t) => t.path === path);
      if (!existing) this.tabs.push({ path, title });
      this.active = path;
      persist(this.tabs, this.active);
    },
    /** 关闭页签：固定页签为空操作；关当前页自动激活相邻（右邻优先，无右邻取左邻）（T4-02/T4-03） */
    closeTab(path: string) {
      const idx = this.tabs.findIndex((t) => t.path === path);
      if (idx < 0) return;
      if (this.tabs[idx].affix) return;
      this.tabs.splice(idx, 1);
      if (this.active === path) {
        const next = this.tabs[idx] ?? this.tabs[idx - 1];
        this.active = next.path;
      }
      persist(this.tabs, this.active);
    },
    /** 点击页签切换：只改激活，路由跳转由调用方完成 */
    activate(path: string) {
      if (this.tabs.some((t) => t.path === path)) {
        this.active = path;
        persist(this.tabs, this.active);
      }
    },
  },
});
