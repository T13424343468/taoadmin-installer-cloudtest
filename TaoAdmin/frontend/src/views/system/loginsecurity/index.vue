<template>
  <a-card title="登录安全" style="max-width: 640px; margin: 0 auto">
    <template #extra><span style="color: #999">保存即生效，无需重启</span></template>
    <a-form layout="vertical" :model="form">
      <a-form-item label="连续失败锁定次数（1~99 次）">
        <a-input-number v-model:value="form.loginMaxFails" :min="1" :max="99" style="width: 100%" />
      </a-form-item>
      <a-form-item label="锁定时长（1~1440 分钟）">
        <a-input-number v-model:value="form.loginLockMinutes" :min="1" :max="1440" style="width: 100%" />
      </a-form-item>
      <a-form-item label="登录验证码">
        <a-switch v-model:checked="form.loginCaptchaRequired" checked-children="开" un-checked-children="关" />
      </a-form-item>
      <a-form-item label="验证码触发阈值（0 表示每次都要，0~99）">
        <a-input-number v-model:value="form.captchaAfterFails" :min="0" :max="99" style="width: 100%" :disabled="!form.loginCaptchaRequired" />
      </a-form-item>
      <a-form-item label="日志保留天数（0 表示永久保留，0~3650）">
        <a-input-number v-model:value="form.logRetentionDays" :min="0" :max="3650" style="width: 100%" />
      </a-form-item>
      <a-form-item label="同账号互踢">
        <a-switch v-model:checked="form.kickOnDuplicateLogin" checked-children="开" un-checked-children="关" />
        <span style="margin-left: 8px; color: #999">开启后同一账号新登录会踢掉旧会话，旧页面需重新登录</span>
      </a-form-item>
      <a-form-item>
        <a-button type="primary" :loading="saving" @click="onSave">保存</a-button>
      </a-form-item>
    </a-form>
  </a-card>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import { getLoginSecurity, saveLoginSecurity, type LoginSecuritySettings } from '@/api/audit';

const form = reactive<LoginSecuritySettings>({
  loginMaxFails: 5,
  loginLockMinutes: 15,
  loginCaptchaRequired: true,
  captchaAfterFails: 0,
  logRetentionDays: 90,
  kickOnDuplicateLogin: false,
});
const saving = ref(false);

onMounted(async () => {
  const s = await getLoginSecurity();
  Object.assign(form, s);
});

async function onSave() {
  saving.value = true;
  try {
    await saveLoginSecurity({ ...form });
    message.success('保存成功，已立即生效');
  } finally {
    saving.value = false;
  }
}
</script>
