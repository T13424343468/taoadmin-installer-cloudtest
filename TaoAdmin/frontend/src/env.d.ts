/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue';
  const component: DefineComponent<{}, {}, any>;
  export default component;
}

// 统一后端返回结构
export interface ApiResult<T = any> {
  success: boolean;
  code: number;
  message: string;
  data: T;
  timestamp?: string;
}
