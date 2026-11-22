<template>
  <!-- 锁屏遮罩：fixed 全屏 z-index 2000，盖住一切交互；输对密码解锁回原页面 -->
  <div v-if="lock.isLocked" class="lock-screen" @click.stop>
    <div class="lock-card">
      <a-avatar :size="72" :src="avatarUrl" class="lock-avatar">
        {{ userStore.nickname.charAt(0) }}
      </a-avatar>
      <div class="lock-name">{{ userStore.nickname }}</div>
      <div class="lock-hint">{{ throttleLeft > 0 ? `输入错误次数过多，${throttleLeft} 秒后可重试` : '屏幕已锁定，请输入密码解锁' }}</div>
      <a-input-password
        ref="passwordInput"
        v-model:value="password"
        class="lock-input"
        placeholder="请输入当前账号密码"
        :disabled="throttleLeft > 0"
        @press-enter="onUnlock"
      />
      <a-button type="primary" block :loading="unlocking" :disabled="throttleLeft > 0 || !password" class="lock-btn" @click="onUnlock">
        解 锁
      </a-button>
      <div v-if="errorMessage" class="lock-error">{{ errorMessage }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onBeforeUnmount } from 'vue';
import { useLockStore } from '@/stores/lock';
import { useUserStore } from '@/stores/user';
import { verifyPassword } from '@/api/auth';
import { buildFileUrl } from '@/api/taoFiles';

const lock = useLockStore();
const userStore = useUserStore();

const password = ref('');
const passwordInput = ref();
const errorMessage = ref('');
const unlocking = ref(false);
let timer: ReturnType<typeof setInterval> | null = null;

const throttleLeft = ref(0);

// 锁屏弹出后光标自动进密码框，解锁少一次点击
watch(
  () => lock.isLocked,
  async (locked) => {
    if (locked) {
      await nextTick();
      passwordInput.value?.focus?.();
    }
  },
);

const avatarUrl = computed(() => {
  const a = userStore.avatar;
  if (!a) return '';
  return /^https:\/\//.test(a) || /^http:\/\//.test(a) ? a : buildFileUrl(a);
});

/** 节流倒计时：每秒刷剩余秒数，到 0 解禁（L-03 落地） */
function tick() {
  throttleLeft.value = lock.throttleLeft();
}

onMounted(() => {
  lock.restore();
  timer = setInterval(tick, 1000);
  tick();
});

onBeforeUnmount(() => {
  if (timer) clearInterval(timer);
});

async function onUnlock() {
  if (throttleLeft.value > 0 || !password.value) return;
  errorMessage.value = '';
  unlocking.value = true;
  const beforeThrottle = lock.throttleLeft() > 0;
  try {
    await lock.unlock(async (_username: string, pwd: string) => {
      // 真实鉴权：复核当前账号登录密码（不走验证码；失败照常进后端锁定账本）
      await verifyPassword(pwd);
    }, userStore.userInfo?.username || '', password.value);
    if (!lock.isLocked) {
      password.value = '';
      errorMessage.value = '';
    } else if (!beforeThrottle && lock.throttleLeft() > 0) {
      errorMessage.value = '输入错误次数过多，请稍后重试';
    } else {
      errorMessage.value = lock.lastError || '密码不正确，请重试';
    }
  } catch {
    errorMessage.value = '网络异常，请稍后重试';
  } finally {
    unlocking.value = false;
    password.value = '';
    tick();
  }
}
</script>

<style scoped lang="less">
.lock-screen {
  position: fixed;
  inset: 0;
  z-index: 2000;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.55);
  backdrop-filter: blur(8px);
}

.lock-card {
  width: 320px;
  padding: 32px;
  border-radius: 8px;
  background: #fff;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.25);
  text-align: center;

  .lock-avatar {
    margin-bottom: 12px;
  }

  .lock-name {
    font-size: 18px;
    font-weight: 600;
    margin-bottom: 4px;
  }

  .lock-hint {
    font-size: 12px;
    color: rgba(0, 0, 0, 0.45);
    margin-bottom: 16px;
  }

  .lock-input {
    margin-bottom: 12px;
  }

  .lock-btn {
    margin-top: 4px;
  }

  .lock-error {
    margin-top: 8px;
    color: #f5222d;
    font-size: 12px;
  }
}
</style>
