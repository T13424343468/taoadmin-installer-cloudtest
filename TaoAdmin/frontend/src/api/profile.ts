import { request } from './request';

/** 我的资料（个人中心展示与编辑） */
export interface ProfileInfo {
  userId: string;
  username: string;
  nickname?: string;
  avatar?: string;
  phone?: string;
  email?: string;
  lastLoginTime?: string;
}

/** 获取我的资料 */
export function getMyProfile(): Promise<ProfileInfo> {
  return request<ProfileInfo>({ url: '/identity/profile', method: 'get' });
}

/** 保存我的资料（昵称/头像/手机/邮箱），保存即生效 */
export function saveMyProfile(data: {
  nickname?: string;
  avatar?: string;
  phone?: string;
  email?: string;
}): Promise<void> {
  return request<void>({ url: '/identity/profile', method: 'put', data });
}

/** 修改自己的密码（旧密码必校验；改完全部会话失效，需重新登录） */
export function changeMyPassword(data: { oldPassword: string; newPassword: string }): Promise<void> {
  return request<void>({ url: '/identity/profile/password', method: 'put', data });
}
