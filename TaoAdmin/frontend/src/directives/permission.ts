import type { App, Directive, DirectiveBinding } from 'vue';
import { useUserStore } from '@/stores/user';

/**
 * v-permission 权限指令
 *
 * 用法：
 *   <a-button v-permission="'sys:user:add'">新增</a-button>
 *   <a-button v-permission="['sys:user:add', 'sys:user:edit']">操作</a-button> // 任一满足即可
 *   <a-button v-permission.delete="'sys:user:del'">删除</a-button>
 *
 * 模式配置（通过 modifier 或全局配置）：
 *   - 默认：无权限时隐藏元素（display: none）
 *   - .disable 修饰符：无权限时禁用按钮（disabled + 降低透明度），适用于 a-button
 *   - 可通过 modifyDirectiveMode('disable') 切换全局默认模式
 *
 * 权限码来源于 user store 的 permissions 集合，超级管理员（admin/super-admin 角色）直接放行。
 */

type PermissionMode = 'hide' | 'disable';

/** 全局默认模式，默认隐藏 */
let globalMode: PermissionMode = 'hide';

/** 允许外部（如系统设置页）切换默认模式 */
export function modifyDirectiveMode(mode: PermissionMode) {
  globalMode = mode;
}

function checkPermission(value: string | string[]): boolean {
  const userStore = useUserStore();
  return userStore.hasPermission(value);
}

const permissionDirective: Directive<HTMLElement, string | string[]> = {
  mounted(el: HTMLElement, binding: DirectiveBinding<string | string[]>) {
    const value = binding.value;
    // modifier 指定 disable 模式，否则用全局默认
    const mode: PermissionMode = binding.modifiers?.disable ? 'disable' : globalMode;

    if (!checkPermission(value)) {
      if (mode === 'disable') {
        // 禁用模式：设置 disabled 并阻止点击
        el.setAttribute('ant-disabled', 'true');
        (el as HTMLButtonElement).disabled = true;
        el.style.opacity = '0.5';
        el.style.cursor = 'not-allowed';
        el.style.pointerEvents = 'none';
      } else {
        // 隐藏模式（默认）：直接从 DOM 移除，避免留白
        el.style.display = 'none';
        el.parentNode?.removeChild(el);
      }
    }
  },
  // 组件更新时重新校验（权限可能在运行中变化）
  updated(el: HTMLElement, binding: DirectiveBinding<string | string[]>) {
    const value = binding.value;
    const mode: PermissionMode = binding.modifiers?.disable ? 'disable' : globalMode;
    const allowed = checkPermission(value);

    if (!allowed && mode === 'disable') {
      (el as HTMLButtonElement).disabled = true;
      el.style.opacity = '0.5';
      el.style.pointerEvents = 'none';
    } else if (allowed && mode === 'disable') {
      (el as HTMLButtonElement).disabled = false;
      el.style.opacity = '';
      el.style.pointerEvents = '';
    }
    // 隐藏模式在 mounted 时已经移除节点，updated 不再处理
  },
};

/** 注册指令 */
export function setupPermissionDirective(app: App) {
  app.directive('permission', permissionDirective);
}

export default permissionDirective;
