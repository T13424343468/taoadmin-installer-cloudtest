import service from './request';
import { message as antMessage } from 'ant-design-vue';

// ============ 用户表格导入导出（xlsx 下载走 blob，业务提示按统一响应结构处理） ============

/** 用户导入结果（与后端 UserImportResultDto 对齐） */
export interface UserImportResult {
  total: number;
  success: number;
  errors: { row: number; reason: string }[];
}

/** 下载用户导出（xlsx） */
export async function exportUsers(keyword?: string): Promise<void> {
  const res = await service.get('/identity/users/export', {
    params: keyword ? { keyword } : undefined,
    responseType: 'blob',
  });
  saveBlob(res.data as Blob, readFilename(res.headers, '用户导出.xlsx'));
}

/** 下载用户导入模板（xlsx） */
export async function downloadUserTemplate(): Promise<void> {
  const res = await service.get('/identity/users/import-template', { responseType: 'blob' });
  saveBlob(res.data as Blob, readFilename(res.headers, '用户导入模板.xlsx'));
}

/** 导入用户（xlsx 上传；返回逐行校验错误明细） */
export function importUsers(file: File): Promise<UserImportResult> {
  const form = new FormData();
  form.append('file', file);
  return service.post('/identity/users/import', form) as unknown as Promise<UserImportResult>;
}

/** 按 Content-Disposition 还原文件名（缺失时用兜底名） */
function readFilename(headers: any, fallback: string): string {
  const dispo: string = headers?.['content-disposition'] || '';
  const match = dispo.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
  let filename = match ? match[1].replace(/"/g, '') : fallback;
  try {
    if (filename.includes('%')) filename = decodeURIComponent(filename);
  } catch {
    // 保留原名
  }
  return filename;
}

/** 触发浏览器保存文件 */
export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

// 复用统一错误提示（导入失败时 request.ts 已弹错误）
export function notifyImportResult(result: UserImportResult): void {
  if (result.errors.length === 0) {
    antMessage.success(`导入完成：共 ${result.total} 行，成功 ${result.success} 条`);
    return;
  }
  antMessage.warning(`导入完成：成功 ${result.success} 条，失败 ${result.errors.length} 条，详见失败明细`);
}
