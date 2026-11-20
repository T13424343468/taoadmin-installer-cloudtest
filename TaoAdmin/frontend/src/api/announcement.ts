import { request } from './request';

// ============ 公告通知 ============

/** 公告项（与后端 AnnouncementItemDto 对齐） */
export interface AnnouncementItem {
  id: string;
  title: string;
  content?: string;
  /** 0 已下线，1 已发布 */
  status: number;
  publisherName?: string;
  publishTime?: string;
  createdAt: string;
}

/** 分页查询公告（管理端） */
export function getAnnouncementPage(params: {
  pageIndex?: number;
  pageSize?: number;
  keyword?: string;
}): Promise<{ total: number; items: AnnouncementItem[] }> {
  return request({ url: '/identity/announcements', method: 'get', params });
}

/** 发布新公告（创建即上线） */
export function publishAnnouncement(data: { title: string; content?: string }): Promise<void> {
  return request({ url: '/identity/announcements', method: 'post', data });
}

/** 上/下线切换 */
export function setAnnouncementStatus(id: string, online: boolean): Promise<void> {
  return request({ url: `/identity/announcements/${id}/status`, method: 'put', data: { online } });
}

/** 删除公告（软删） */
export function deleteAnnouncement(id: string): Promise<void> {
  return request({ url: `/identity/announcements/${id}`, method: 'delete' });
}

/** 登录用户可见的最新公告（顶栏通知入口） */
export function getLatestAnnouncements(): Promise<AnnouncementItem[]> {
  return request({ url: '/identity/announcements/latest', method: 'get' });
}
