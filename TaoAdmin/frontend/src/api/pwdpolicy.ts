import { request } from './request';

// ============ 密码策略（系统管理） ============

/** 密码策略（与后端 PasswordPolicy record 对齐） */
export interface PasswordPolicyDto {
  /** 最小长度（6~64） */
  minLength: number;
  /** 至少字符类数（1~4：小写/大写/数字/特殊字符） */
  minKinds: number;
  /** 强制改密天数（0 = 不强制） */
  forceChangeDays: number;
}

/** 读取当前密码策略（未配置的键返回内置默认值） */
export function getPasswordPolicy(): Promise<PasswordPolicyDto> {
  return request<PasswordPolicyDto>({ url: '/identity/pwd-policy', method: 'get' });
}

/** 保存密码策略（需 sys:pwdpolicy:manage 权限，保存即清缓存立即生效） */
export function savePasswordPolicy(data: PasswordPolicyDto): Promise<void> {
  return request<void>({ url: '/identity/pwd-policy', method: 'put', data });
}
