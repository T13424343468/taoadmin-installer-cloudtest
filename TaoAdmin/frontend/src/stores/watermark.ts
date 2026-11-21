import { defineStore } from 'pinia';

/** 水印开关偏好（第五班路一）。默认开启；localStorage 持久化，非法值回退 true。 */
const WM_KEY = 'taoadmin-watermark';

function loadEnabled(): boolean {
  try {
    const raw = localStorage.getItem(WM_KEY);
    if (raw === null) return true;
    // 只有显式 '0' 才关；非法值（含损坏数据）一律回退默认开（W-02）
    return raw !== '0';
  } catch {
    return true;
  }
}

export const useWatermarkStore = defineStore('watermark', {
  state: () => ({ enabled: loadEnabled() }),
  actions: {
    setEnabled(value: boolean) {
      this.enabled = value;
      try {
        localStorage.setItem(WM_KEY, value ? '1' : '0');
      } catch {
        // 存不进只影响记住偏好，不阻塞开关
      }
    },
  },
});
