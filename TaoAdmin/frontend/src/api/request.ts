import axios, { type AxiosInstance, type AxiosRequestConfig, type AxiosResponse } from 'axios';
import { message as antMessage } from 'ant-design-vue';
import router from '@/router';

/** 统一响应结构（与后端 ApiResult 对齐） */
export interface ApiResult<T = any> {
  success: boolean;
  code: number;
  message: string;
  data: T;
  timestamp?: string;
}

/** token 存取 */
const TOKEN_KEY = 'tao_admin_token';
export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}
export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}
export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

/** 是否正在跳转登录，避免重复弹窗/跳转 */
let redirectingToLogin = false;

function redirectToLogin(): void {
  if (redirectingToLogin) return;
  redirectingToLogin = true;
  clearToken();
  antMessage.warning('登录状态已失效，请重新登录');
  const current = router.currentRoute.value;
  const redirect = current && current.path !== '/login' ? `?redirect=${encodeURIComponent(current.fullPath)}` : '';
  router
    .push(`/login${redirect}`)
    .catch(() => undefined)
    .finally(() => {
      redirectingToLogin = false;
    });
}

/** axios 实例 */
const service: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  timeout: 30000,
});

// 请求拦截：携带 token
service.interceptors.request.use((config) => {
  const token = getToken();
  if (token) {
    config.headers = config.headers || {};
    config.headers['Authorization'] = `Bearer ${token}`;
  }
  return config;
});

// 响应拦截：解包 ApiResult，统一错误提示，401 跳登录
service.interceptors.response.use(
  (response: AxiosResponse) => {
    // blob 等非 JSON 响应直接返回
    if (response.config.responseType === 'blob' || response.data instanceof Blob) {
      return response;
    }

    const result = response.data as ApiResult;
    // 后端统一返回 { success, code, message, data }
    if (result && typeof result === 'object' && 'code' in result) {
      if (result.code === 401 || result.code === 403) {
        redirectToLogin();
        return Promise.reject(new Error(result.message || '未授权'));
      }
      if (!result.success) {
        antMessage.error(result.message || '请求失败');
        return Promise.reject(new Error(result.message || '请求失败'));
      }
      // 解包：调用方直接拿到 data 部分
      return result.data as any;
    }
    return response.data as any;
  },
  (error) => {
    const status = error.response?.status;
    const serverMessage: string | undefined = error.response?.data?.message;

    if (status === 401 || status === 403) {
      redirectToLogin();
    } else if (error.response) {
      antMessage.error(serverMessage || `请求失败（${status}）`);
    } else if (error.code === 'ECONNABORTED') {
      antMessage.error('请求超时，请稍后重试');
    } else {
      antMessage.error('网络异常，请检查网络连接');
    }
    return Promise.reject(error);
  }
);

/** 便捷请求方法：返回已解包的 data */
export function request<T = any>(config: AxiosRequestConfig): Promise<T> {
  return service.request(config) as unknown as Promise<T>;
}

export default service;
