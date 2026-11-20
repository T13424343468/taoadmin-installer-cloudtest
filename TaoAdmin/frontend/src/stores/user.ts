import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import type { UserInfo, MenuItem } from '@/api/auth';
import { getUserInfo, logout as logoutApi } from '@/api/auth';
import { getToken, setToken, clearToken } from '@/api/request';

/**
 * 用户状态：token、用户信息、权限码集合
 */
export const useUserStore = defineStore('user', () => {
  const token = ref<string>(getToken() || '');
  const userInfo = ref<UserInfo | null>(null);

  /** 权限码集合（Set，方便 O(1) 判断） */
  const permissions = ref<Set<string>>(new Set());
  /** 菜单树（来自后端，用于动态路由和侧边栏） */
  const menuTree = ref<MenuItem[]>([]);

  const nickname = computed(() => userInfo.value?.nickname || userInfo.value?.username || '未知用户');
  const avatar = computed(() => userInfo.value?.avatar || '');
  const roles = computed(() => userInfo.value?.roles || []);

  /** 设置 token（登录成功后调用） */
  function setTokenValue(value: string) {
    token.value = value;
    setToken(value);
  }

  /** 拉取用户信息、权限码与菜单树 */
  async function fetchUserInfo(): Promise<UserInfo> {
    const info = await getUserInfo();
    userInfo.value = info;
    permissions.value = new Set(info.permissions || []);
    menuTree.value = info.menus || [];
    return info;
  }

  /** 是否拥有指定权限码；超级管理员角色直接放行 */
  function hasPermission(code?: string | string[]): boolean {
    if (!code || (Array.isArray(code) && code.length === 0)) return true;
    if (roles.value.includes('admin') || roles.value.includes('super-admin')) return true;
    const codes = Array.isArray(code) ? code : [code];
    return codes.some((c) => permissions.value.has(c));
  }

  /** 退出登录：调用后端并清理本地状态 */
  async function logout() {
    try {
      await logoutApi();
    } catch {
      // 后端登出失败不阻塞本地清理
    }
    resetState();
  }

  /** 清空本地登录状态（401 时也会调用） */
  function resetState() {
    token.value = '';
    userInfo.value = null;
    permissions.value = new Set();
    menuTree.value = [];
    clearToken();
  }

  return {
    token,
    userInfo,
    permissions,
    menuTree,
    nickname,
    avatar,
    roles,
    setTokenValue,
    fetchUserInfo,
    hasPermission,
    logout,
    resetState,
  };
});
