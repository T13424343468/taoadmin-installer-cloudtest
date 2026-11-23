import { request } from './request';

/** 登录参数（captchaId/captchaCode 为可选：安全设置要求验证码时必传） */
export interface LoginParams {
  username: string;
  password: string;
  captchaId?: string;
  captchaCode?: string;
}

/** 用户信息（含菜单树与权限码） */
export interface UserInfo {
  userId: string;
  username: string;
  nickname?: string;
  avatar?: string;
  roles: string[];
  permissions: string[];
  menus: MenuItem[];
  /** 所属租户 ID（0 = 平台层） */
  tenantId?: string;
  /** 所属租户名称 */
  tenantName?: string;
  /** 按密码策略判定是否需要强制改密 */
  mustChangePassword?: boolean;
}

/** 菜单树节点 */
export interface MenuItem {
  id: string;
  parentId: string | null;
  name: string;
  path: string;
  component?: string;
  icon?: string;
  type: 'dir' | 'menu' | 'button';
  visible?: boolean;
  sort?: number;
  children?: MenuItem[];
}

/** 登录响应 */
export interface LoginResult {
  token: string;
  userInfo?: UserInfo;
}

/** 账号密码登录 */
export function login(params: LoginParams): Promise<LoginResult> {
  return request<LoginResult>({
    url: '/identity/auth/login',
    method: 'post',
    data: params,
  });
}

/** 锁屏解锁：复核当前账号登录密码（已登录会话内调用，不重发令牌） */
export function verifyPassword(password: string): Promise<void> {
  return request<void>({
    url: '/identity/auth/verify-password',
    method: 'post',
    data: { password },
  });
}

/** 获取当前登录用户信息 + 权限码 + 菜单树 */
export function getUserInfo(): Promise<UserInfo> {
  return request<UserInfo>({
    url: '/identity/auth/userinfo',
    method: 'get',
  });
}

/** 忘记密码：申请重置令牌（发到账号绑定邮箱；后端统一提示，不泄露账号是否存在） */
export function forgotPassword(username: string): Promise<void> {
  return request<void>({
    url: '/identity/auth/forgot-password',
    method: 'post',
    data: { username },
  });
}

/** 重置密码：凭邮箱收到的令牌设置新密码（令牌一次性、15 分钟内有效） */
export function resetPassword(username: string, token: string, newPassword: string): Promise<void> {
  return request<void>({
    url: '/identity/auth/reset-password',
    method: 'post',
    data: { username, token, newPassword },
  });
}

/** 退出登录 */
export function logout(): Promise<void> {
  return request<void>({
    url: '/identity/auth/logout',
    method: 'post',
  });
}
