<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-title">
        <img v-if="siteStore.config.siteLogo" :src="loginLogoUrl" alt="站点 Logo" class="logo-img" />
        <span v-else class="logo-icon">⚙️</span>
        <h2>{{ siteStore.config.siteName }} 管理系统</h2>
        <p>微服务统一管理后台</p>
      </div>

      <a-form
        :model="form"
        :rules="rules"
        layout="vertical"
        @finish="onLogin"
      >
        <a-form-item label="用户名" name="username">
          <a-input
            v-model:value="form.username"
            size="large"
            placeholder="请输入用户名"
            autocomplete="username"
          >
            <template #prefix><UserOutlined /></template>
          </a-input>
        </a-form-item>

        <a-form-item label="密码" name="password">
          <a-input-password
            v-model:value="form.password"
            size="large"
            placeholder="请输入密码"
            autocomplete="current-password"
          >
            <template #prefix><LockOutlined /></template>
          </a-input-password>
        </a-form-item>

        <a-form-item v-if="captcha.svg" label="验证码" name="captchaCode">
          <div class="captcha-row">
            <a-input
              v-model:value="form.captchaCode"
              size="large"
              placeholder="请输入验证码"
              maxlength="6"
              autocomplete="off"
            />
            <span class="captcha-svg" title="点击刷新" @click="refreshCaptcha" v-html="captcha.svg"></span>
          </div>
        </a-form-item>

        <a-form-item>
          <a-button
            type="primary"
            size="large"
            block
            :loading="loading"
            html-type="submit"
          >
            登 录
          </a-button>
        </a-form-item>
      </a-form>

      <div class="login-tip">默认账号：admin / admin123</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { message } from 'ant-design-vue';
import { UserOutlined, LockOutlined } from '@ant-design/icons-vue';
import { login } from '@/api/auth';
import { getCaptcha } from '@/api/audit';
import { useUserStore } from '@/stores/user';
import { useSiteStore } from '@/api/site';
import { buildFileUrl } from '@/api/taoFiles';

const router = useRouter();
const route = useRoute();
const userStore = useUserStore();
const siteStore = useSiteStore();

// 登录页无令牌：站点参数走匿名接口拉取，接口不通时回退内置默认值
onMounted(() => {
  if (!siteStore.loaded) void siteStore.fetchConfig();
  void refreshCaptcha();
});

const loginLogoUrl = computed(() =>
  /^https?:\/\//.test(siteStore.config.siteLogo)
    ? siteStore.config.siteLogo
    : buildFileUrl(siteStore.config.siteLogo)
);

// 验证码：登录页进入时尝试拉取（后端开关关闭时不强求）；失败后带 captchaId/captchaCode 重试
const captcha = reactive<{ captchaId: string; svg: string }>({ captchaId: '', svg: '' });
async function refreshCaptcha() {
  try {
    const c = await getCaptcha();
    captcha.captchaId = c.captchaId;
    captcha.svg = c.svg;
    form.captchaCode = '';
  } catch {
    captcha.svg = ''; // 后端不可达或未开验证码时静默，不挡登录
  }
}

const loading = ref(false);
const form = reactive({
  username: 'admin',
  password: '',
  captchaCode: '',
});

const rules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [
    { required: true, message: '请输入密码', trigger: 'blur' },
    { min: 5, message: '密码至少 5 位', trigger: 'blur' },
  ],
};

async function onLogin() {
  loading.value = true;
  try {
    const result = await login({
      username: form.username,
      password: form.password,
      captchaId: captcha.captchaId || undefined,
      captchaCode: form.captchaCode || undefined,
    });
    userStore.setTokenValue(result.token);
    // 拉取用户信息并生成动态路由
    await userStore.fetchUserInfo();
    await import('@/router').then((m) => m.generateDynamicRoutes());

    message.success('登录成功');
    const redirect = (route.query.redirect as string) || '/dashboard';
    router.replace(redirect);
  } catch (e) {
    // 错误提示已在 request.ts 统一处理；失败后刷新验证码重试
    await refreshCaptcha();
    console.error('[login] 登录失败', e);
  } finally {
    loading.value = false;
  }
}
</script>

<style scoped lang="less">
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100vh;
  background: linear-gradient(135deg, #1f3b73 0%, #16274a 50%, #0d1526 100%);
}

.login-card {
  width: 400px;
  padding: 40px 36px;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.3);
}

.login-title {
  text-align: center;
  margin-bottom: 32px;

  .logo-icon {
    font-size: 36px;
  }

  .logo-img {
    height: 40px;
    max-width: 180px;
    object-fit: contain;
  }

  h2 {
    margin: 12px 0 4px;
    font-size: 22px;
    color: rgba(0, 0, 0, 0.85);
  }

  p {
    margin: 0;
    color: rgba(0, 0, 0, 0.45);
    font-size: 13px;
  }
}

.login-tip {
  margin-top: 8px;
  text-align: center;
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}

.captcha-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.captcha-svg {
  display: inline-flex;
  cursor: pointer;
  border-radius: 4px;
  overflow: hidden;
  background: #f5f5f5;

  :deep(svg) {
    height: 40px;
    width: auto;
  }
}
</style>
