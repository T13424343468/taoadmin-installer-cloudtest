import { request } from './request';
import type { PageQuery, PageResult } from './system';

// ============ 定时任务管理 ============

export interface JobItem {
  id: string;
  name: string;
  group?: string;
  cron: string;
  handler: string; // 处理器标识，如 cleanExpiredTokensJob
  params?: string;
  status: 'RUNNING' | 'PAUSED'; // 运行中 / 已暂停
  misfirePolicy?: string;
  description?: string;
  lastFireTime?: string;
  nextFireTime?: string;
  lastResult?: string;
  /** 失败自动重试次数（0 表示不重试） */
  retryCount?: number;
  createdAt?: string;
}

export interface JobSaveParams {
  id?: string;
  name: string;
  group?: string;
  cron: string;
  handler: string;
  params?: string;
  misfirePolicy?: string;
  description?: string;
  /** 失败自动重试次数（0 表示不重试） */
  retryCount?: number;
}

/** 任务执行历史行（每次执行含重试各落一条） */
export interface JobLogItem {
  id: string;
  jobId: string;
  jobName: string;
  startTime: string;
  endTime: string;
  durationMs: number;
  success: boolean;
  message?: string | null;
  /** 重试序号：0 为首次执行 */
  retryIndex: number;
}

/** 分页查询任务 */
export function getJobPage(params: PageQuery): Promise<PageResult<JobItem>> {
  return request<PageResult<JobItem>>({ url: '/job/jobs', method: 'get', params });
}

/** 新增/编辑任务 */
export function saveJob(data: JobSaveParams): Promise<void> {
  return request<void>({
    url: data.id ? `/job/jobs/${data.id}` : '/job/jobs',
    method: data.id ? 'put' : 'post',
    data,
  });
}

/** 删除任务 */
export function deleteJob(id: string): Promise<void> {
  return request<void>({ url: `/job/jobs/${id}`, method: 'delete' });
}

/** 暂停任务 */
export function pauseJob(id: string): Promise<void> {
  return request<void>({ url: `/job/jobs/${id}/pause`, method: 'post' });
}

/** 恢复任务 */
export function resumeJob(id: string): Promise<void> {
  return request<void>({ url: `/job/jobs/${id}/resume`, method: 'post' });
}

/** 立即执行一次 */
export function runJobOnce(id: string): Promise<void> {
  return request<void>({ url: `/job/jobs/${id}/run`, method: 'post' });
}

/** 校验 cron 表达式 */
export function validateCron(cron: string): Promise<{ valid: boolean; nextFireTime?: string; message?: string }> {
  return request<{ valid: boolean; nextFireTime?: string; message?: string }>({
    url: '/job/jobs/validate-cron',
    method: 'get',
    params: { cron },
  });
}

/** 任务执行历史分页（倒序，最新在前） */
export function getJobLogs(id: string, params: PageQuery): Promise<PageResult<JobLogItem>> {
  return request<PageResult<JobLogItem>>({ url: `/job/jobs/${id}/logs`, method: 'get', params });
}
