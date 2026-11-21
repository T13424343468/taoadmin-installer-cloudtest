import { request } from './request';

// ============ 工作流审批 ============

/** 流程节点（与后端 WorkflowNodeDto 对齐） */
export interface WorkflowNode {
  name: string;
  approverRole: string;
}

/** 流程定义（与后端 WorkflowDefDto 对齐） */
export interface WorkflowDef {
  id: string;
  workflowName: string;
  workflowCode: string;
  description?: string;
  nodes: WorkflowNode[];
  status: number;
}

/** 流程实例（与后端 WorkflowInstanceDto 对齐） */
export interface WorkflowInstance {
  id: string;
  workflowName: string;
  bizTitle: string;
  remark?: string;
  applicantName: string;
  currentNode: string;
  /** 1 进行中 2 已通过 3 已驳回 4 已取消 */
  status: number;
  statusText: string;
  finishedAt?: string;
  createdAt: string;
}

/** 审批任务（与后端 WorkflowTaskDto 对齐） */
export interface WorkflowTask {
  id: string;
  instanceId: string;
  bizTitle: string;
  workflowName: string;
  nodeName: string;
  applicantName: string;
  /** 0 待审批 1 通过 2 驳回 */
  status: number;
  comment?: string;
  handlerName?: string;
  handleTime?: string;
  createdAt: string;
}

/** 流程定义列表（平台模板 + 本租户） */
export function getWorkflowDefinitions(): Promise<WorkflowDef[]> {
  return request<WorkflowDef[]>({ url: '/identity/workflow/definitions', method: 'get' });
}

/** 发起审批（防重由 Idempotency-Key 请求头在后端统一处理） */
export function submitWorkflow(data: {
  workflowCode: string;
  bizTitle: string;
  remark?: string;
}): Promise<{ instanceId: string }> {
  return request<{ instanceId: string }>({ url: '/identity/workflow/instances', method: 'post', data });
}

/** 流程实例分页（超管看全部，普通用户看自己发起的） */
export function getWorkflowInstances(params: {
  pageIndex?: number;
  pageSize?: number;
  status?: number;
}): Promise<{ total: number; items: WorkflowInstance[] }> {
  return request({ url: '/identity/workflow/instances', method: 'get', params });
}

/** 流程实例详情（含任务历史） */
export function getWorkflowDetail(id: string): Promise<{ instance: WorkflowInstance; tasks: WorkflowTask[] }> {
  return request({ url: `/identity/workflow/instances/${id}`, method: 'get' });
}

/** 我的待办任务（拥有对应审批角色的任务；超管看全部） */
export function getMyTasks(): Promise<WorkflowTask[]> {
  return request<WorkflowTask[]>({ url: '/identity/workflow/tasks', method: 'get' });
}

/** 审批：通过/驳回（需拥有任务指定角色） */
export function approveTask(id: string, data: { approved: boolean; comment?: string }): Promise<void> {
  return request<void>({ url: `/identity/workflow/tasks/${id}/approve`, method: 'post', data });
}
