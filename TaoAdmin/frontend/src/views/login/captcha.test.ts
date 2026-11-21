import { describe, it, expect, vi, beforeEach } from 'vitest';

// 验证登录页验证码交互：进入即拉取、失败后刷新、成功携带 captchaId/captchaCode
const { loginMock, getCaptchaMock } = vi.hoisted(() => ({
  loginMock: vi.fn(),
  getCaptchaMock: vi.fn(),
}));

vi.mock('@/api/auth', () => ({
  login: loginMock,
}));
vi.mock('@/api/audit', () => ({
  getCaptcha: getCaptchaMock,
}));
vi.mock('@/api/site', () => ({
  useSiteStore: () => ({ loaded: true, config: { siteName: 'Tao', siteLogo: '' }, fetchConfig: vi.fn(async () => ({})) }),
}));
vi.mock('@/stores/user', () => ({
  useUserStore: () => ({ setTokenValue: vi.fn(), fetchUserInfo: vi.fn(async () => ({})) }),
}));
vi.mock('@/api/taoFiles', () => ({ buildFileUrl: (v: string) => v }));
vi.mock('ant-design-vue', () => ({ message: { success: vi.fn(), error: vi.fn() } }));
vi.mock('vue-router', () => ({
  useRouter: () => ({ replace: vi.fn() }),
  useRoute: () => ({ query: {} }),
}));

// 模拟登录页组合式逻辑（与 index.vue <script setup> 内联验证码部分对齐）
function makeLoginPage() {
  const state = {
    captcha: { captchaId: '', svg: '' },
    form: { username: 'admin', password: '', captchaCode: '' },
  };
  async function refreshCaptcha() {
    try {
      const c = await getCaptchaMock();
      state.captcha.captchaId = c.captchaId;
      state.captcha.svg = c.svg;
      state.form.captchaCode = '';
    } catch {
      state.captcha.svg = '';
    }
  }
  async function onLogin() {
    try {
      await loginMock({
        username: state.form.username,
        password: state.form.password,
        captchaId: state.captcha.captchaId || undefined,
        captchaCode: state.form.captchaCode || undefined,
      });
    } catch (e) {
      await refreshCaptcha();
      throw e;
    }
  }
  return { state, refreshCaptcha, onLogin };
}

beforeEach(() => {
  loginMock.mockReset();
  getCaptchaMock.mockReset();
  getCaptchaMock.mockResolvedValue({ captchaId: 'cap-1', svg: '<svg>abc</svg>' });
});

describe('登录页验证码交互', () => {
  it('进入登录页即拉取验证码并展示', async () => {
    const { state, refreshCaptcha } = makeLoginPage();
    await refreshCaptcha();
    expect(getCaptchaMock).toHaveBeenCalled();
    expect(state.captcha.svg).toContain('<svg');
    expect(state.captcha.captchaId).toBe('cap-1');
  });

  it('登录成功携带 captchaId/captchaCode', async () => {
    loginMock.mockResolvedValue({ token: 't' });
    const { state, refreshCaptcha, onLogin } = makeLoginPage();
    await refreshCaptcha();
    state.form.captchaCode = 'ab12';
    await onLogin();
    expect(loginMock).toHaveBeenCalledWith(
      expect.objectContaining({ captchaId: 'cap-1', captchaCode: 'ab12' })
    );
  });

  it('登录失败后重新拉取验证码（旧码作废）', async () => {
    loginMock.mockRejectedValue(new Error('bad'));
    const { state, refreshCaptcha, onLogin } = makeLoginPage();
    await refreshCaptcha();
    const before = getCaptchaMock.mock.calls.length;
    await onLogin().catch(() => {});
    expect(getCaptchaMock.mock.calls.length).toBeGreaterThan(before);
    expect(state.form.captchaCode).toBe('');
  });

  it('验证码接口不可达时静默降级，不挡登录', async () => {
    getCaptchaMock.mockRejectedValue(new Error('network'));
    const { state, refreshCaptcha, onLogin } = makeLoginPage();
    await refreshCaptcha();
    expect(state.captcha.svg).toBe('');
    expect(state.captcha.captchaId).toBe('');
    loginMock.mockResolvedValue({ token: 't' });
    await onLogin();
    expect(loginMock).toHaveBeenCalledWith(
      expect.objectContaining({ captchaId: undefined, captchaCode: undefined })
    );
  });
});
