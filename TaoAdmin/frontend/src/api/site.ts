import { request } from './request';
import { defineStore } from 'pinia';

// ============ 站点参数 ============

/** 站点参数（后端未配置的键返回内置默认值） */
export interface SiteConfig {
  siteName: string;
  siteLogo: string;
  siteFooter: string;
}

/** 读取站点参数（匿名接口，登录页与主布局门面渲染用） */
export function getSiteConfig(): Promise<SiteConfig> {
  return request<SiteConfig>({ url: '/identity/site-config', method: 'get' });
}

/** 保存站点参数（需 sys:site:manage 权限，保存即生效） */
export function saveSiteConfig(data: Partial<SiteConfig>): Promise<void> {
  return request<void>({ url: '/identity/site-config', method: 'post', data });
}

/** 站点参数兜底默认值：接口不通时页面照常渲染，不白板 */
const DEFAULT_CONFIG: SiteConfig = {
  siteName: 'TaoAdmin',
  siteLogo: '',
  siteFooter: 'TaoAdmin',
};

/** 站点参数全局状态：登录后与门面（登录页/侧栏/页脚/浏览器标题）共用一份 */
export const useSiteStore = defineStore('site', {
  state: () => ({
    config: { ...DEFAULT_CONFIG } as SiteConfig,
    loaded: false,
  }),
  actions: {
    /** 拉取站点参数（失败静默回退默认值，不阻塞页面） */
    async fetchConfig(): Promise<void> {
      try {
        const cfg = await getSiteConfig();
        this.config = { ...DEFAULT_CONFIG, ...cfg };
        this.loaded = true;
      } catch {
        // 接口不通时保持默认值，门面不空白
        this.loaded = true;
      }
    },
    /** 保存后用返回的最新配置就地刷新，前台立即生效 */
    applyLocal(cfg: SiteConfig): void {
      this.config = { ...cfg };
    },
    reset(): void {
      this.config = { ...DEFAULT_CONFIG };
      this.loaded = false;
    },
  },
});
