import { request } from './request';

/** 登录安全设置（未配置的键后端返回内置默认值） */
export interface LoginSecuritySettings {
  loginMaxFails: number;
  loginLockMinutes: number;
  loginCaptchaRequired: boolean;
  captchaAfterFails: number;
  logRetentionDays: number;
  /** 同账号互踢：开启后同一账号新登录踢掉旧会话 */
  kickOnDuplicateLogin: boolean;
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
  /** 下一页游标（亿级深翻页用；为空表示没有下一页） */
  nextCursor?: string | null;
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
  /** 上一页返回的游标：带上后走游标翻页，深翻页恒定速度 */
  cursor?: string;
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

/** 批量删除审计日志（物理删除） */
export function batchDeleteAuditLogs(ids: string[]): Promise<{ deleted: number }> {
  return request<{ deleted: number }>({
    url: '/identity/audit/log/batch-delete',
    method: 'post',
    data: { ids: ids.map(Number) },
  });
}

/** 按当前筛选条件导出审计日志为 xlsx（⑮；后端上限 5 万条，返回文件流） */
export function exportAuditLogs(params: Omit<AuditLogQuery, 'pageIndex' | 'pageSize' | 'cursor'>): Promise<Blob> {
  return request<Blob>({
    url: '/identity/audit/log/export',
    method: 'get',
    params,
    responseType: 'blob',
  });
}

/** 通知渠道配置（未配置项返回空串） */
export function getNotifyChannel(): Promise<Record<string, string>> {
  return request<Record<string, string>>({
    url: '/identity/notify-channel',
    method: 'get',
  });
}

/** 保存通知渠道配置（整体覆盖，保存即生效；本操作自动留痕） */
export function saveNotifyChannel(configs: Record<string, string>): Promise<string> {
  return request<string>({
    url: '/identity/notify-channel',
    method: 'put',
    data: configs,
  });
}

/** 发送测试消息验证接线（channel=email/dingtalk；结果落发送记录） */
export function sendNotifyTest(channel: 'email' | 'dingtalk'): Promise<string> {
  return request<string>({
    url: '/identity/notify-channel/test',
    method: 'post',
    data: { channel },
  });
}

/** 通知发送记录行（成功/失败留痕） */
export interface NotifyLogItem {
  id: string;
  channel: string;
  target?: string | null;
  subject?: string | null;
  success: boolean;
  error?: string | null;
  createdAt: string;
}

/** 通知发送记录分页（倒序，最新在前） */
export function getNotifyLogs(params: { pageIndex: number; pageSize: number }): Promise<{
  total: number;
  pageIndex: number;
  pageSize: number;
  items: NotifyLogItem[];
}> {
  return request({
    url: '/identity/notify-channel/logs',
    method: 'get',
    params,
  });
}

/** 告警规则（服务下线 / 任务连续失败时按阈值触发通知） */
export interface AlertRuleItem {
  id: string;
  name: string;
  /** 事件类型：SERVICE_DOWN / JOB_FAIL */
  eventType: 'SERVICE_DOWN' | 'JOB_FAIL' | string;
  /** 连续失败阈值（最小 1） */
  threshold: number;
  /** 通知渠道（逗号分隔）：email / dingtalk */
  channels: string;
  enabled: boolean;
  remark?: string | null;
}

export interface AlertRuleSaveParams {
  id?: string;
  name: string;
  eventType: 'SERVICE_DOWN' | 'JOB_FAIL';
  threshold: number;
  channels: string;
  enabled: boolean;
  remark?: string;
}

/** 告警规则清单 */
export function getAlertRules(): Promise<AlertRuleItem[]> {
  return request<AlertRuleItem[]>({ url: '/identity/alert-rules', method: 'get' });
}

/** 新增或更新告警规则（本操作自动留痕） */
export function saveAlertRule(rule: AlertRuleSaveParams): Promise<string> {
  return request<string>({
    url: '/identity/alert-rules',
    method: rule.id ? 'put' : 'post',
    data: rule,
  });
}

/** 删除告警规则（软删除；本操作自动留痕） */
export function deleteAlertRule(id: string): Promise<string> {
  return request<string>({ url: `/identity/alert-rules/${id}`, method: 'delete' });
}
