import axios from 'axios';
import { message } from 'ant-design-vue';

/** 文件中心服务地址（存 localStorage，可在页面右上角修改） */
const FC_BASE_KEY = 'tao_files_base_url';
const DEFAULT_BASE = import.meta.env.VITE_TAO_FILES_URL || 'http://localhost:5003';

export function getTaoFilesBase(): string {
  return localStorage.getItem(FC_BASE_KEY) || DEFAULT_BASE;
}

export function setTaoFilesBase(url: string): void {
  localStorage.setItem(FC_BASE_KEY, url.replace(/\/+$/, ''));
}

/** 拼接文件访问地址：服务地址 + 相对路径 */
export function buildFileUrl(relativePath: string): string {
  return `${getTaoFilesBase()}${relativePath}`;
}

interface TaoFilesResult<T> {
  code: number;
  msg: string;
  data: T;
}

async function fcRequest<T>(config: { method: string; url: string; data?: any; params?: any }): Promise<T> {
  try {
    const res = await axios.request<TaoFilesResult<T>>({
      baseURL: getTaoFilesBase(),
      timeout: 30000,
      ...config,
    });
    const body = res.data;
    if (body.code !== 200) throw new Error(body.msg || '请求失败');
    return body.data;
  } catch (e: any) {
    // 接不通时给出人话提示（服务未启动 / 地址配错 / 跨域未放行）
    if (e.code === 'ERR_NETWORK' || e.message === 'Network Error') {
      throw new Error(`连不上文件中心（${getTaoFilesBase()}），请确认服务已启动、地址已配对`);
    }
    throw e;
  }
}

export interface FileItem {
  id: string;
  fileName: string;
  fileUrl: string;
  size: number;
  contentType: string | null;
  md5: string;
  uploadBy: string | null;
  source: string | null;
  createdAt: string;
}

export interface FilePage {
  total: number;
  list: FileItem[];
  /** 下一页游标（亿级深翻页用；为空表示没有下一页） */
  nextCursor?: string | null;
}

export async function listFiles(params: {
  pageIndex: number;
  pageSize: number;
  keyword?: string;
  /** 上一页返回的游标：带上后走游标翻页，深翻页恒定速度 */
  cursor?: string;
}): Promise<FilePage> {
  return fcRequest<FilePage>({ method: 'GET', url: '/api/files', params });
}

export interface UploadResult extends FileItem {
  /** 相同内容已存在时直接复用（秒传） */
  reused: boolean;
}

export async function uploadFile(file: File): Promise<UploadResult> {
  const fd = new FormData();
  fd.append('file', file);
  const data = await fcRequest<UploadResult>({ method: 'POST', url: '/api/files/upload', data: fd });
  return { ...data, fileName: data.fileName || file.name };
}

export async function deleteFile(id: string, physicalDelete = false): Promise<void> {
  await fcRequest({ method: 'DELETE', url: `/api/files/${id}`, params: physicalDelete ? { physicalDelete: true } : undefined });
}

export async function pingTaoFiles(): Promise<string> {
  const res = await axios.get(`${getTaoFilesBase()}/health`, { timeout: 5000 });
  return res.data?.status === 'UP' ? '连接正常' : `异常状态：${res.data?.status ?? '未知'}`;
}

/** 复制文本到剪贴板（http 环境降级用临时输入框） */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    message.success('相对路径已复制，业务系统存这个就行');
    return true;
  } catch {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(ta);
    if (ok) message.success('相对路径已复制，业务系统存这个就行');
    else message.error('复制失败，请手动复制');
    return ok;
  }
}
