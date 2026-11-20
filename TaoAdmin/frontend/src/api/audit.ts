import { request } from './request';

/** 登录安全设置（未配置的键后端返回内置默认值） */
export interface LoginSecuritySettings {
  loginMaxFails: number;
  loginLockMinutes: number;
  loginCaptchaRequired: boolean;
  captchaAfterFails: number;
  logRetentionDays: number;
}

/** 登录验证码（SVG） */
export interface CaptchaResult {
  captchaId: string;
  svg: string;
}

/** 登录验证码（匿名获取，登录失败后需刷新重取） */
export function getCaptcha(): Promise<CaptchaResult> {
  return request<CaptchaResult>({
    url: '/identity/audit/captcha',
    method: 'get',
  });
}

/** 读取登录安全设置 */
export function getLoginSecurity(): Promise<LoginSecuritySettings> {
  return request<LoginSecuritySettings>({
    url: '/identity/audit/settings',
    method: 'get',
  });
}

/** 保存登录安全设置（整体覆盖，保存即生效） */
export function saveLoginSecurity(settings: LoginSecuritySettings): Promise<void> {
  return request<void>({
    url: '/identity/audit/settings',
    method: 'put',
    data: settings,
  });
}

/** 审计日志行 */
export interface AuditLogItem {
  id: string;
  category?: string;
  userName?: string;
  action?: string;
  success?: boolean;
  ip?: string;
  detail?: string;
  createdAt: string;
}

/** 分页结果 */
export interface AuditLogPage {
  total: number;
  pageIndex: number;
  pageSize: number;
  items: AuditLogItem[];
}

/** 审计日志分页查询参数 */
export interface AuditLogQuery {
  pageIndex: number;
  pageSize: number;
  category?: string;
  userName?: string;
  success?: number;
  begin?: string;
  end?: string;
}

/** 审计日志分页 */
export function getAuditLogs(params: AuditLogQuery): Promise<AuditLogPage> {
  return request<AuditLogPage>({
    url: '/identity/audit/log',
    method: 'get',
    params,
  });
}

/** 立即执行一轮日志清理（返回删除条数） */
export function cleanupAuditLogs(): Promise<{ deleted: number }> {
  return request<{ deleted: number }>({
    url: '/identity/audit/cleanup',
    method: 'post',
  });
}

/** 通知渠道配置（未配置项返回空串） */
export function getNotifyChannel(): Promise<Record<string, string>> {
  return request<Record<string, string>>({
    url: '/identity/notify-channel',
    method: 'get',
  });
}

/** 保存通知渠道配置（整体覆盖；通道未接线，仅供配置留存） */
export function saveNotifyChannel(configs: Record<string, string>): Promise<string> {
  return request<string>({
    url: '/identity/notify-channel',
    method: 'put',
    data: configs,
  });
}
