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

/** 获取当前登录用户信息 + 权限码 + 菜单树 */
export function getUserInfo(): Promise<UserInfo> {
  return request<UserInfo>({
    url: '/identity/auth/userinfo',
    method: 'get',
  });
}

/** 退出登录 */
export function logout(): Promise<void> {
  return request<void>({
    url: '/identity/auth/logout',
    method: 'post',
  });
}
