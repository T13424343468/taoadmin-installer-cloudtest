import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// mock request 层：按 URL 记录调用并返回预设数据
const calls: Array<{ url: string; method: string; data?: unknown; params?: unknown }> = [];
vi.mock('./request', () => ({
  request: vi.fn(async (cfg: { url: string; method: string; data?: unknown; params?: unknown }) => {
    calls.push(cfg);
    if (cfg.url.includes('captcha')) return { captchaId: 'c1', svg: '<svg>code</svg>' };
    if (cfg.url.includes('audit/settings') && cfg.method === 'get')
      return { loginMaxFails: 5, loginLockMinutes: 15, loginCaptchaRequired: true, captchaAfterFails: 0, logRetentionDays: 90 };
    if (cfg.url.includes('audit/log'))
      return { total: 1, pageIndex: 1, pageSize: 20, items: [{ id: '1', category: 'login', userName: 'admin', success: true, createdAt: '2026-11-18 22:00:00' }] };
    if (cfg.url.includes('audit/cleanup')) return { deleted: 3 };
    if (cfg.url.includes('notify-channel') && cfg.method === 'get')
      return { smtpHost: 'smtp.example.com', smtpPort: '465' };
    return null;
  }),
}));

import { getCaptcha, getLoginSecurity, saveLoginSecurity, getAuditLogs, cleanupAuditLogs, getNotifyChannel, saveNotifyChannel } from './audit';

beforeEach(() => { calls.length = 0; });
afterEach(() => { vi.clearAllMocks(); });

describe('防护审计接口封装', () => {
  it('验证码走匿名 GET /identity/audit/captcha', async () => {
    const c = await getCaptcha();
    expect(c.captchaId).toBe('c1');
    expect(c.svg).toContain('<svg');
    expect(calls[0]).toMatchObject({ url: '/identity/audit/captcha', method: 'get' });
  });

  it('登录安全读 GET /audit/settings，存 PUT 携带整体设置', async () => {
    const s = await getLoginSecurity();
    expect(s.loginMaxFails).toBe(5);
    await saveLoginSecurity({ ...s, loginMaxFails: 8 });
    const save = calls.find((c) => c.method === 'put');
    expect(save?.url).toBe('/identity/audit/settings');
    expect((save?.data as { loginMaxFails: number }).loginMaxFails).toBe(8);
  });

  it('审计日志分页 GET 携带筛选参数；清理 POST 返回删除数', async () => {
    const page = await getAuditLogs({ pageIndex: 2, pageSize: 10, category: 'login', success: 0 });
    expect(page.total).toBe(1);
    expect(calls[0].params).toMatchObject({ pageIndex: 2, pageSize: 10, category: 'login', success: 0 });
    const r = await cleanupAuditLogs();
    expect(r.deleted).toBe(3);
    expect(calls[1].method).toBe('post');
  });

  it('通知渠道读 PUT 全量保存', async () => {
    const c = await getNotifyChannel();
    expect(c.smtpHost).toBe('smtp.example.com');
    await saveNotifyChannel({ smtpHost: 'smtp2.example.com', smtpPort: '587' });
    const save = calls.find((x) => x.method === 'put');
    expect((save?.data as Record<string, string>).smtpHost).toBe('smtp2.example.com');
  });
});
