import { request } from './request';

// ============ 代码生成器（⑲） ============

/** 单个字段设计 */
export interface CodegenFieldDesign {
  name: string;
  label: string;
  dataType: 'string' | 'int' | 'long' | 'decimal' | 'datetime' | 'bool' | 'text';
  length?: number;
  precision?: number;
  nullable: boolean;
  defaultValue?: string;
  isPrimary: boolean;
  isUnique: boolean;
  isIndex: boolean;
  showInList: boolean;
  control: 'text' | 'number' | 'date' | 'textarea' | 'dict' | 'switch';
  dictCode?: string;
  queryType: 'none' | 'eq' | 'like' | 'range';
  rules?: string;
}

/** 表设计 */
export interface CodegenDesign {
  id: string;
  tableName: string;
  displayName: string;
  fieldsJson?: string;
  remark?: string;
  lastSyncAt?: string;
  createdAt: string;
}

/** 表设计保存请求 */
export interface CodegenDesignSave {
  id?: string;
  tableName: string;
  displayName: string;
  fields: CodegenFieldDesign[];
  remark?: string;
}

/** 同步建库结果 */
export interface CodegenSyncResult {
  executed: boolean;
  actions: string[];
  destructivePending: string[];
}

/** 生成出的单个文件 */
export interface CodegenFile {
  path: string;
  content: string;
}

/** 一键生成结果 */
export interface CodegenGenerateResult {
  recordId: string;
  tableName: string;
  files: CodegenFile[];
  elapsedMs: number;
}

/** 生成记录 */
export interface CodegenRecord {
  id: string;
  tableName: string;
  mode: string;
  fileCount: number;
  elapsedMs: number;
  createdAt: string;
}

/** 表设计分页 */
export function getCodegenDesigns(params: { pageIndex?: number; pageSize?: number; keyword?: string }): Promise<{
  total: number;
  items: CodegenDesign[];
}> {
  return request({ url: '/identity/codegen/designs', method: 'get', params });
}

/** 保存表设计（新建或更新） */
export function saveCodegenDesign(data: CodegenDesignSave): Promise<string> {
  return request({ url: '/identity/codegen/designs', method: 'post', data });
}

/** 表设计详情 */
export function getCodegenDesign(id: string): Promise<CodegenDesign> {
  return request({ url: `/identity/codegen/designs/${id}`, method: 'get' });
}

/** 删除表设计（软删，不动已生成的库表与代码） */
export function deleteCodegenDesign(id: string): Promise<void> {
  return request({ url: `/identity/codegen/designs/${id}`, method: 'delete' });
}

/** 同步到数据库：建表/加列自动执行；删列等破坏性变更仅返回语句待确认 */
export function syncCodegenDesign(id: string): Promise<CodegenSyncResult> {
  return request({ url: `/identity/codegen/designs/${id}/sync`, method: 'post' });
}

/** 执行经确认的破坏性变更语句（仅允许单条 ALTER TABLE） */
export function executeDestructiveSql(sql: string): Promise<void> {
  return request({ url: '/identity/codegen/destructive', method: 'post', data: { sql } });
}

/** 一键生成全套增删改查前后端代码（落生成记录） */
export function generateCodegen(id: string, mode?: string): Promise<CodegenGenerateResult> {
  return request({ url: `/identity/codegen/designs/${id}/generate`, method: 'post', params: { mode } });
}

/** 生成记录分页 */
export function getCodegenRecords(params: { pageIndex?: number; pageSize?: number; keyword?: string }): Promise<{
  total: number;
  items: CodegenRecord[];
}> {
  return request({ url: '/identity/codegen/records', method: 'get', params });
}

/** 生成记录详情（含全部生成文件内容） */
export function getCodegenRecord(id: string): Promise<Record<string, any>> {
  return request({ url: `/identity/codegen/records/${id}`, method: 'get' });
}
