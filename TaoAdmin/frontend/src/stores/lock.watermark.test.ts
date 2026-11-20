import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useLockStore } from '@/stores/lock';
import { useWatermarkStore } from '@/stores/watermark';

/** 解锁鉴权通道（组件层注入，落点=login API；测试侧独立 mock，同契约） */
function makeLoginChannel(impl: (username: string, password: string) => Promise<void>) {
  const channel = vi.fn(impl);
  return channel;
}

const LOCK_KEY = 'taoadmin-lock';
const TOKEN_KEY = 'tao_admin_token';

describe('锁屏 store（L-01~L-04）', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    sessionStorage.clear();
    localStorage.removeItem(TOKEN_KEY);
  });

  it('L-01 锁定置真并持久化 sessionStorage；新实例恢复锁态', () => {
    const s = useLockStore();
    expect(s.isLocked).toBe(false);
    s.lock();
    expect(s.isLocked).toBe(true);
    expect(sessionStorage.getItem(LOCK_KEY)).toBe('1');
    setActivePinia(createPinia());
    const s2 = useLockStore();
    s2.restore();
    expect(s2.isLocked).toBe(true);
  });

  it('L-02 解锁成功置假、清尝试账；组件层成功回调落新 token', async () => {
    const channel = makeLoginChannel(async () => {
      localStorage.setItem(TOKEN_KEY, 'tok-new');
    });
    const s = useLockStore();
    s.lock();
    await s.unlock(channel, 'admin', 'Admin@123');
    expect(s.isLocked).toBe(false);
    expect(s.throttledUntil).toBeNull();
    expect(sessionStorage.getItem(LOCK_KEY)).toBeNull();
    expect(localStorage.getItem(TOKEN_KEY)).toBe('tok-new');
    expect(channel).toHaveBeenCalledWith('admin', 'Admin@123');
  });

  it('L-03 连续失败 5 次进入节流，节流期内不再发起解锁', async () => {
    let calls = 0;
    const channel = makeLoginChannel(async () => {
      calls += 1;
      throw new Error('bad');
    });
    const s = useLockStore();
    s.lock();
    for (let i = 0; i < 5; i++) {
      await s.unlock(channel, 'admin', 'wrong');
    }
    expect(s.throttleLeft()).toBeGreaterThan(0);
    await s.unlock(channel, 'admin', 'wrong');
    expect(calls).toBe(5); // 节流期内未再发起
  });

  it('L-04 节流到期（模拟时间拨前）后可重试，成功后账清零', async () => {
    const channel = makeLoginChannel(async () => {
      localStorage.setItem(TOKEN_KEY, 't');
    });
    const s = useLockStore();
    s.lock();
    await s.unlock(makeLoginChannel(async () => { throw new Error('bad'); }), 'admin', 'x');
    await s.unlock(makeLoginChannel(async () => { throw new Error('bad'); }), 'admin', 'x');
    // 节流期内拨到过去，模拟 30s 走完
    (s as any).throttledUntil = Date.now() - 1000;
    await s.unlock(channel, 'admin', 'Admin@123');
    expect(s.isLocked).toBe(false);
    expect(s.throttleLeft()).toBe(0);
    expect(s.throttledUntil).toBeNull();
    expect(localStorage.getItem(TOKEN_KEY)).toBe('t');
  });
});

describe('水印 store（W-01~W-03）', () => {
  const WM_KEY = 'taoadmin-watermark';

  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
  });

  it('W-01 默认开启；关闭持久化，新实例恢复为关', () => {
    const s = useWatermarkStore();
    expect(s.enabled).toBe(true);
    s.setEnabled(false);
    expect(localStorage.getItem(WM_KEY)).toBe('0');
    setActivePinia(createPinia());
    const s2 = useWatermarkStore();
    expect(s2.enabled).toBe(false);
  });

  it('W-02 非法值回退 true 且不抛', () => {
    localStorage.setItem(WM_KEY, 'bogus');
    expect(() => {
      setActivePinia(createPinia());
      const s = useWatermarkStore();
      expect(s.enabled).toBe(true);
    }).not.toThrow();
    localStorage.setItem(WM_KEY, '{broken');
    expect(() => {
      setActivePinia(createPinia());
      useWatermarkStore();
    }).not.toThrow();
  });

  it('W-03 开关与持久化一致', () => {
    const s = useWatermarkStore();
    s.setEnabled(false);
    expect(s.enabled).toBe(false);
    s.setEnabled(true);
    expect(s.enabled).toBe(true);
    expect(localStorage.getItem(WM_KEY)).toBe('1');
  });
});
