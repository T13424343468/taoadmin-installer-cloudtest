import { request } from './request';

// ============ 站内消息 ============

/** 消息项（与后端 SysMessage 实体对齐） */
export interface MessageItem {
  id: string;
  title: string;
  content?: string;
  topic: string;
  /** 是否已读 */
  isRead: boolean;
  createdAt: string;
}

/** 队列状态（提供方 + 各主题积压条数） */
export interface QueueStats {
  provider?: string;
  [key: string]: any;
}

/** 消息分页（普通用户看自己收到的，超管看全部） */
export function getMessagePage(params: {
  pageIndex?: number;
  pageSize?: number;
}): Promise<{ total: number; items: MessageItem[] }> {
  return request({ url: '/identity/messages', method: 'get', params });
}

/** 标记已读 */
export function markMessageRead(id: string): Promise<void> {
  return request<void>({ url: `/identity/messages/${id}/read`, method: 'put' });
}

/** 【消息队列演示】向指定主题发布一条测试消息，消费者收到后落库为站内消息 */
export function publishDemoMessage(topic: string, content: string): Promise<void> {
  return request<void>({ url: '/identity/messages/demo/publish', method: 'post', data: { topic, content } });
}

/** 【消息队列演示】队列状态（提供方 + 各主题积压条数） */
export function getQueueStats(): Promise<QueueStats> {
  return request<QueueStats>({ url: '/identity/messages/demo/stats', method: 'get' });
}
