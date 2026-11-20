import { defineStore } from 'pinia';

/** 预设主色板（设计定稿：8 色，默认拂晓蓝） */
export const PRIMARY_PRESETS = [
  { key: 'dawn', name: '拂晓蓝', value: '#1677ff' },
  { key: 'purple', name: '极光紫', value: '#722ed1' },
  { key: 'cyan', name: '明青', value: '#13c2c2' },
  { key: 'green', name: '极光绿', value: '#52c41a' },
  { key: 'gold', name: '金盏黄', value: '#faad14' },
  { key: 'red', name: '薄暮红', value: '#f5222d' },
  { key: 'geekblue', name: '极客蓝', value: '#2f54eb' },
  { key: 'magenta', name: '洋红', value: '#eb2f96' },
] as const;

export const DEFAULT_PRIMARY = '#1677ff';
export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'taoadmin-theme';

interface PersistedTheme {
  mode: ThemeMode;
  primary: string;
}

function isValidHex(color: unknown): color is string {
  return typeof color === 'string' && /^#[0-9a-fA-F]{6}$/.test(color);
}

function isValidMode(mode: unknown): mode is ThemeMode {
  return mode === 'light' || mode === 'dark';
}

/** 从 localStorage 读取偏好；非法或缺失一律回退默认（T4-05/T4-16） */
function loadPersisted(): PersistedTheme {
  const fallback: PersistedTheme = { mode: 'light', primary: DEFAULT_PRIMARY };
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return fallback;
    const parsed = JSON.parse(raw) as Partial<PersistedTheme>;
    return {
      mode: isValidMode(parsed.mode) ? parsed.mode : fallback.mode,
      primary: isValidHex(parsed.primary) ? parsed.primary : fallback.primary,
    };
  } catch {
    return fallback;
  }
}

function persist(theme: PersistedTheme): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(theme));
  } catch {
    // 存不进（隐私模式等）只影响记忆偏好，不阻塞换肤
  }
}

export const useThemeStore = defineStore('theme', {
  state: () => {
    const saved = loadPersisted();
    return { mode: saved.mode, primary: saved.primary } as PersistedTheme;
  },
  getters: {
    isDark: (s) => s.mode === 'dark',
  },
  actions: {
    setMode(mode: ThemeMode) {
      this.mode = mode;
      persist({ mode: this.mode, primary: this.primary });
    },
    toggleMode() {
      this.setMode(this.mode === 'light' ? 'dark' : 'light');
    },
    setPrimary(color: string) {
      if (!isValidHex(color)) return;
      this.primary = color;
      persist({ mode: this.mode, primary: this.primary });
    },
  },
});
