import { request } from './request';

/** 行政区划条目（只读，民政部国家地名信息库口径数据） */
export interface RegionItem {
  code: string;
  name: string;
  parentCode: string;
  level: number; // 1 省 2 地级市 3 区县 4 街道（乡镇）
  fullPath: string;
}

/** 更新任务进度（后台执行，轮询查询） */
export interface RegionUpdateProgress {
  status: 'Idle' | 'Running' | 'Success' | 'Failed';
  stage: string; // 连接数据源 / 抓取数据 / 校验 / 写入 / 完成
  percent: number;
  fetchedCount: number;
  error?: string | null;
  version?: string | null;
  beforeCount?: number | null;
  afterCount?: number | null;
  durationSeconds?: number | null;
  startedAt?: string | null;
  endedAt?: string | null;
  sourceKind: 'online' | 'builtin' | '';
}

/** 数据版本与更新状态 */
export interface RegionStatus {
  version: string;
  source: string;
  fetchedAt?: string | null;
  provinceCount: number;
  cityCount: number;
  countyCount: number;
  townCount: number;
  totalCount: number;
  update: RegionUpdateProgress;
}

/** 按父级代码查子级（parentCode 传空或不传查省级；直辖市下直接是区县） */
export function getRegionChildren(parentCode = ''): Promise<RegionItem[]> {
  return request<RegionItem[]>({ url: '/identity/regions/children', method: 'get', params: { parentCode } });
}

/** 按关键词搜索（名称或全路径），最多 50 条 */
export function searchRegions(keyword: string): Promise<RegionItem[]> {
  return request<RegionItem[]>({ url: '/identity/regions/search', method: 'get', params: { keyword } });
}

/** 按区划代码查详情 */
export function getRegionByCode(code: string): Promise<RegionItem | null> {
  return request<RegionItem | null>({ url: `/identity/regions/${code}`, method: 'get' });
}

/** 数据版本与更新状态（页面展示当前版本、抓取时间、条数与最近一次任务进度） */
export function getRegionStatus(): Promise<RegionStatus> {
  return request<RegionStatus>({ url: '/identity/regions/status', method: 'get' });
}

/** 启动在线更新（后台执行立即返回；已有任务在跑时后端拒绝并提示） */
export function startRegionUpdate(): Promise<Record<string, unknown>> {
  return request({ url: '/identity/regions/update', method: 'post' });
}

/** 从内置数据包恢复（在线更新失败时的兜底，同样后台执行） */
export function restoreRegionBuiltin(): Promise<Record<string, unknown>> {
  return request({ url: '/identity/regions/update/builtin', method: 'post' });
}

/** 查询更新进度（轮询入口；刷新页面进度仍在） */
export function getRegionUpdateProgress(): Promise<RegionUpdateProgress> {
  return request<RegionUpdateProgress>({ url: '/identity/regions/update/progress', method: 'get' });
}
