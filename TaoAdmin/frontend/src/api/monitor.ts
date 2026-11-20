import { request } from './request';

// ============ 服务监控 ============

export interface ServiceInstance {
  instanceId: string;
  host: string;
  port: number;
  status: 'UP' | 'DOWN' | 'STARTING' | 'OUT_OF_SERVICE';
  lastHeartbeat?: string;
}

export interface ServiceItem {
  serviceName: string;
  displayName?: string;
  instances: number;
  status: 'UP' | 'DOWN' | 'PARTIAL';
  lastHealthCheck?: string;
  version?: string;
  description?: string;
  instanceList?: ServiceInstance[];
}

/** 服务列表（走网关） */
export function getServices(): Promise<ServiceItem[]> {
  return request<ServiceItem[]>({ url: '/monitor/services', method: 'get' });
}

/** 单个服务健康详情 */
export function getServiceDetail(serviceName: string): Promise<ServiceItem> {
  return request<ServiceItem>({ url: `/monitor/services/${serviceName}`, method: 'get' });
}

// ============ 日志查询 ============

export interface LogQuery {
  pageIndex?: number;
  pageSize?: number;
  startTime?: string;
  endTime?: string;
  level?: string; // DEBUG/INFO/WARN/ERROR/FATAL
  service?: string;
  keyword?: string;
  traceId?: string;
}

export interface LogItem {
  id: string;
  timestamp: string;
  level: string;
  service: string;
  instance?: string;
  traceId?: string;
  thread?: string;
  logger?: string;
  message: string;
  exception?: string;
}

export interface LogPageResult {
  items: LogItem[];
  total: number;
  pageIndex: number;
  pageSize: number;
}

/** 分页查询日志 */
export function getLogs(params: LogQuery): Promise<LogPageResult> {
  return request<LogPageResult>({ url: '/monitor/logs', method: 'get', params });
}

/** 查询单条日志详情（含堆栈） */
export function getLogDetail(id: string): Promise<LogItem> {
  return request<LogItem>({ url: `/monitor/logs/${id}`, method: 'get' });
}

// ============ 在线用户 ============

export interface OnlineUserItem {
  sessionId: string;
  userId: string;
  userName: string;
  nickName?: string;
  ip: string;
  userAgent?: string;
  loginTime: string;
  lastActiveTime: string;
}

/** 当前在线用户名单 */
export function getOnlineUsers(): Promise<OnlineUserItem[]> {
  return request<OnlineUserItem[]>({ url: '/identity/online-users', method: 'get' });
}

/** 强制下线指定会话（该令牌即刻失效） */
export function kickOnlineUser(sessionId: string): Promise<void> {
  return request<void>({ url: `/identity/online-users/${encodeURIComponent(sessionId)}`, method: 'delete' });
}
