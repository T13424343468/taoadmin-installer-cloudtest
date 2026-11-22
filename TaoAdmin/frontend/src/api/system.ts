import { request } from './request';

// ============ 通用类型 ============

export interface PageQuery {
  pageIndex?: number;
  pageSize?: number;
  [key: string]: any;
}

export interface PageResult<T> {
  items: T[];
  total: number;
  pageIndex: number;
  pageSize: number;
}

// ============ 用户管理 ============

export interface UserItem {
  id: string;
  username: string;
  nickname?: string;
  email?: string;
  phone?: string;
  status: number; // 1 启用 0 禁用
  roleIds?: string[];
  roleNames?: string[];
  createdAt?: string;
}

export interface UserSaveParams {
  id?: string;
  username: string;
  nickname?: string;
  email?: string;
  phone?: string;
  password?: string;
  status?: number;
  roleIds?: string[];
}

/** 分页查询用户 */
export function getUserPage(params: PageQuery): Promise<PageResult<UserItem>> {
  return request<PageResult<UserItem>>({ url: '/identity/users', method: 'get', params });
}

/** 新增/编辑用户 */
export function saveUser(data: UserSaveParams): Promise<void> {
  return request<void>({
    url: data.id ? `/identity/users/${data.id}` : '/identity/users',
    method: data.id ? 'put' : 'post',
    data,
  });
}

/** 删除用户 */
export function deleteUser(id: string): Promise<void> {
  return request<void>({ url: `/identity/users/${id}`, method: 'delete' });
}

/** 批量删除用户（自动跳过当前登录账号） */
export function batchDeleteUsers(ids: string[]): Promise<{ deleted: number; skipped: number }> {
  return request<{ deleted: number; skipped: number }>({
    url: '/identity/users/batch-delete',
    method: 'post',
    data: { ids: ids.map(Number) },
  });
}

/** 批量启用/停用用户（自动跳过当前登录账号） */
export function batchSetUserStatus(ids: string[], status: number): Promise<{ updated: number; skipped: number }> {
  return request<{ updated: number; skipped: number }>({
    url: '/identity/users/batch-status',
    method: 'post',
    data: { ids: ids.map(Number), status },
  });
}

/** 重置密码 */
export function resetUserPassword(id: string, password: string): Promise<void> {
  return request<void>({ url: `/identity/users/${id}/password`, method: 'put', data: { password } });
}

/** 分配角色 */
export function assignUserRoles(id: string, roleIds: string[]): Promise<void> {
  return request<void>({ url: `/identity/users/${id}/roles`, method: 'put', data: { roleIds } });
}

// ============ 角色管理 ============

export interface RoleItem {
  id: string;
  name: string;
  code: string;
  description?: string;
  status: number;
  menuIds?: string[];
  apiIds?: string[];
  createdAt?: string;
}

export function getRolePage(params: PageQuery): Promise<PageResult<RoleItem>> {
  return request<PageResult<RoleItem>>({ url: '/identity/roles', method: 'get', params });
}

export function getAllRoles(): Promise<RoleItem[]> {
  return request<RoleItem[]>({ url: '/identity/roles/all', method: 'get' });
}

export function saveRole(data: Partial<RoleItem>): Promise<void> {
  return request<void>({
    url: data.id ? `/identity/roles/${data.id}` : '/identity/roles',
    method: data.id ? 'put' : 'post',
    data,
  });
}

export function deleteRole(id: string): Promise<void> {
  return request<void>({ url: `/identity/roles/${id}`, method: 'delete' });
}

/** 批量删除角色（连带清理授权关系） */
export function batchDeleteRoles(ids: string[]): Promise<{ deleted: number }> {
  return request<{ deleted: number }>({
    url: '/identity/roles/batch-delete',
    method: 'post',
    data: { ids: ids.map(Number) },
  });
}

/** 分配权限（菜单树 + 接口权限） */
export function assignRolePermissions(
  id: string,
  data: { menuIds: string[]; apiIds: string[] }
): Promise<void> {
  return request<void>({ url: `/identity/roles/${id}/permissions`, method: 'put', data });
}

/** 查询角色已分配的权限 ID */
export function getRolePermissions(id: string): Promise<{ menuIds: string[]; apiIds: string[] }> {
  return request<{ menuIds: string[]; apiIds: string[] }>({
    url: `/identity/roles/${id}/permissions`,
    method: 'get',
  });
}

// ============ 菜单管理 ============

export interface MenuRecord {
  id: string;
  parentId: string | null;
  name: string;
  path?: string;
  component?: string;
  icon?: string;
  type: 'dir' | 'menu' | 'button'; // 目录/菜单/按钮
  permission?: string; // 权限码
  visible: boolean; // 是否在侧边栏显示
  status: number;
  sort: number;
  children?: MenuRecord[];
}

/** 获取菜单树（管理端展示全部） */
export function getMenuTree(): Promise<MenuRecord[]> {
  return request<MenuRecord[]>({ url: '/identity/menus/tree', method: 'get' });
}

export function saveMenu(data: Partial<MenuRecord>): Promise<void> {
  return request<void>({
    url: data.id ? `/identity/menus/${data.id}` : '/identity/menus',
    method: data.id ? 'put' : 'post',
    data,
  });
}

export function deleteMenu(id: string): Promise<void> {
  return request<void>({ url: `/identity/menus/${id}`, method: 'delete' });
}

// ============ 接口资源管理 ============

export interface ApiResourceItem {
  id: string;
  name: string;
  method: string; // GET/POST/PUT/DELETE
  path: string; // 如 /identity/users
  service?: string; // 所属服务
  description?: string;
  status: number;
  createdAt?: string;
}

export function getApiPage(params: PageQuery): Promise<PageResult<ApiResourceItem>> {
  return request<PageResult<ApiResourceItem>>({ url: '/identity/apis', method: 'get', params });
}

export function getAllApis(): Promise<ApiResourceItem[]> {
  return request<ApiResourceItem[]>({ url: '/identity/apis/all', method: 'get' });
}

export function saveApi(data: Partial<ApiResourceItem>): Promise<void> {
  return request<void>({
    url: data.id ? `/identity/apis/${data.id}` : '/identity/apis',
    method: data.id ? 'put' : 'post',
    data,
  });
}

export function deleteApi(id: string): Promise<void> {
  return request<void>({ url: `/identity/apis/${id}`, method: 'delete' });
}

/** 批量删除接口资源（连带清理角色关联） */
export function batchDeleteApis(ids: string[]): Promise<{ deleted: number }> {
  return request<{ deleted: number }>({
    url: '/identity/apis/batch-delete',
    method: 'post',
    data: { ids: ids.map(Number) },
  });
}
