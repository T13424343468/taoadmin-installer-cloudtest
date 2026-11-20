<template>
  <a-card title="通知渠道" style="max-width: 640px; margin: 0 auto">
    <template #extra><a-tag color="orange">只埋不接线：配置仅留存，不发真实请求</a-tag></template>
    <a-form layout="vertical" :model="form">
      <a-divider orientation="left">SMTP 邮件</a-divider>
      <a-form-item label="SMTP 服务器"><a-input v-model:value="form.smtpHost" placeholder="如 smtp.example.com" /></a-form-item>
      <a-form-item label="端口"><a-input-number v-model:value="port" :min="1" :max="65535" style="width: 100%" /></a-form-item>
      <a-form-item label="发件账号"><a-input v-model:value="form.smtpUser" /></a-form-item>
      <a-form-item label="发件密码/授权码"><a-input-password v-model:value="form.smtpPass" /></a-form-item>
      <a-divider orientation="left">短信网关</a-divider>
      <a-form-item label="网关地址"><a-input v-model:value="form.smsGateway" placeholder="短信网关 API 地址" /></a-form-item>
      <a-form-item label="签名"><a-input v-model:value="form.smsSign" placeholder="短信签名" /></a-form-item>
      <a-form-item>
        <a-button type="primary" :loading="saving" @click="onSave">保存</a-button>
      </a-form-item>
    </a-form>
  </a-card>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import { getNotifyChannel, saveNotifyChannel } from '@/api/audit';

const KNOWN = ['smtpHost', 'smtpPort', 'smtpUser', 'smtpPass', 'smsGateway', 'smsSign'];

const configs = reactive<Record<string, string>>({});
const form = reactive({
  smtpHost: '', smtpUser: '', smtpPass: '', smsGateway: '', smsSign: '',
});
const saving = ref(false);
const port = computed<number | undefined>({
  get: () => (configs.smtpPort ? Number(configs.smtpPort) : undefined),
  set: (v) => { configs.smtpPort = v == null ? '' : String(v); },
});

onMounted(async () => {
  const c = await getNotifyChannel();
  for (const k of KNOWN) configs[k] = c[k] ?? '';
  form.smtpHost = configs.smtpHost ?? '';
  form.smtpUser = configs.smtpUser ?? '';
  form.smtpPass = configs.smtpPass ?? '';
  form.smsGateway = configs.smsGateway ?? '';
  form.smsSign = configs.smsSign ?? '';
});

async function onSave() {
  saving.value = true;
  try {
    configs.smtpHost = form.smtpHost;
    configs.smtpUser = form.smtpUser;
    configs.smtpPass = form.smtpPass;
    configs.smsGateway = form.smsGateway;
    configs.smsSign = form.smsSign;
    await saveNotifyChannel({ ...configs });
    message.success('保存成功（通道未接线，仅供配置留存）');
  } finally {
    saving.value = false;
  }
}
</script>
