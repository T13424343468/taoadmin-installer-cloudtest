import { defineStore } from 'pinia';

/**
 * 锁屏状态（第五班路一）。
 * 锁态真源=本机会话（sessionStorage，会话级）：离开工位暂离，新标签页/重开浏览器不带锁。
 * 解锁走真实鉴权（/identity/auth/login，复用登录接口，不放宽任何校验）；
 * 连续失败 5 次节流 30s，防暴力试密（尝试账留内存，锁态留会话）。
 */
export const LOCK_STORAGE_KEY = 'taoadmin-lock';
export const MAX_ATTEMPTS = 5;
export const THROTTLE_SECONDS = 30;

export const useLockStore = defineStore('lock', {
  state: () => ({
    isLocked: false as boolean,
    attempts: 0,
    throttledUntil: null as number | null,
  }),
  actions: {
    /** 恢复会话锁态（layout mounted 时调用） */
    restore() {
      try {
        this.isLocked = sessionStorage.getItem(LOCK_STORAGE_KEY) === '1';
      } catch {
        this.isLocked = false;
      }
    },
    lock() {
      this.isLocked = true;
      this.attempts = 0;
      this.throttledUntil = null;
      try {
        sessionStorage.setItem(LOCK_STORAGE_KEY, '1');
      } catch {
        // 存不进只影响刷新恢复，不阻塞锁屏
      }
    },
    /** 节流剩余秒数；未节流返回 0 */
    throttleLeft(): number {
      if (this.throttledUntil === null) return 0;
      const left = Math.ceil((this.throttledUntil - Date.now()) / 1000);
      return left > 0 ? left : 0;
    },
    /**
     * 解锁：真实登录鉴权。成功落新 token（后端重发会话），失败累计尝试并按账节流。
     * login 由调用方（组件层）注入返回 Promise<void>，避免 store 直接耦合 API 模块
     * ——测试侧 mock 同一通道（L-02/L-03/L-04 断言先行的落点）。
     */
    async unlock(doLogin: (username: string, password: string) => Promise<void>, username: string, password: string) {
      if (this.throttleLeft() > 0) return;
      try {
        await doLogin(username, password);
        this.isLocked = false;
        this.attempts = 0;
        this.throttledUntil = null;
        try {
          sessionStorage.removeItem(LOCK_STORAGE_KEY);
        } catch {
          // 忽略
        }
      } catch {
        this.attempts += 1;
        if (this.attempts >= MAX_ATTEMPTS) {
          this.throttledUntil = Date.now() + THROTTLE_SECONDS * 1000;
          this.attempts = 0;
        }
      }
    },
  },
});
