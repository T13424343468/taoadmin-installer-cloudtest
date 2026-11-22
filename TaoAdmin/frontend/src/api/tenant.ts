import { request } from './request';

// ============ 多租户管理（系统管理） ============

/** 租户项（与后端 SysTenant 实体对齐） */
export interface TenantItem {
  id: string;
  tenantName: string;
  tenantCode: string;
  /** 1 启用 / 0 停用 */
  status: number;
  contact?: string;
  remark?: string;
  createdAt: string;
}

/** 租户新增/编辑请求（与后端 TenantSaveRequest 对齐） */
export interface TenantSaveParams {
  id?: string;
  tenantName: string;
  tenantCode: string;
  contact?: string;
  remark?: string;
  status: number;
}

/** 租户列表（平台管理员可见全部） */
export function getTenantList(): Promise<TenantItem[]> {
  return request<TenantItem[]>({ url: '/identity/tenants', method: 'get' });
}

/** 新增/修改租户（id 为空即新增；需 sys:tenant:manage 权限） */
export function saveTenant(data: TenantSaveParams): Promise<void> {
  return request<void>({
    url: data.id ? `/identity/tenants/${data.id}` : '/identity/tenants',
    method: data.id ? 'put' : 'post',
    data,
  });
}

/** 删除租户（软删） */
export function deleteTenant(id: string): Promise<void> {
  return request<void>({ url: `/identity/tenants/${id}`, method: 'delete' });
}

/** 批量删除租户（软删） */
export function batchDeleteTenants(ids: string[]): Promise<{ deleted: number }> {
  return request<{ deleted: number }>({
    url: '/identity/tenants/batch-delete',
    method: 'post',
    data: { ids: ids.map(Number) },
  });
}
