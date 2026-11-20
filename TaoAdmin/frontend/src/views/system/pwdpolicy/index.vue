<template>
  <div class="page-container">
    <a-card title="密码策略" size="small" style="max-width: 640px">
      <template #extra>
        <span class="policy-tip">保存即生效，管新建口令与改密，不动存量密码</span>
      </template>

      <a-skeleton v-if="loading" active :paragraph="{ rows: 4 }" />

      <a-form v-else ref="formRef" :model="form" :rules="rules" layout="vertical">
        <a-form-item label="最小长度" name="minLength" extra="6~64 位；新设密码不足该长度将被拒收">
          <a-input-number v-model:value="form.minLength" :min="6" :max="64" style="width: 100%" />
        </a-form-item>

        <a-form-item label="至少字符类数" name="minKinds" extra="小写、大写、数字、特殊字符四类中至少命中的类数（1~4）">
          <a-input-number v-model:value="form.minKinds" :min="1" :max="4" style="width: 100%" />
        </a-form-item>

        <a-form-item label="强制改密天数" name="forceChangeDays" extra="0 表示不强制；超过 N 天未改密，登录后要求立即改密">
          <a-input-number v-model:value="form.forceChangeDays" :min="0" :max="3650" style="width: 100%" />
        </a-form-item>

        <a-form-item>
          <a-space>
            <a-button v-permission="'sys:pwdpolicy:manage'" type="primary" :loading="saving" @click="onSave">
              保存
            </a-button>
            <a-button :disabled="saving" @click="onReset">重置</a-button>
          </a-space>
        </a-form-item>
      </a-form>
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import { getPasswordPolicy, savePasswordPolicy } from '@/api/pwdpolicy';

const formRef = ref();
const loading = ref(true);
const saving = ref(false);
const form = reactive({ minLength: 8, minKinds: 3, forceChangeDays: 0 });
const savedSnapshot = ref({ minLength: 8, minKinds: 3, forceChangeDays: 0 });

const rules = {
  minLength: [{ required: true, message: '请输入最小长度', trigger: 'blur' }],
  minKinds: [{ required: true, message: '请输入字符类数', trigger: 'blur' }],
  forceChangeDays: [{ required: true, message: '请输入强制改密天数', trigger: 'blur' }],
};

onMounted(async () => {
  try {
    const policy = await getPasswordPolicy();
    Object.assign(form, policy);
    savedSnapshot.value = { ...policy };
  } finally {
    loading.value = false;
  }
});

async function onSave() {
  try {
    await formRef.value?.validate();
  } catch {
    return;
  }
  saving.value = true;
  try {
    await savePasswordPolicy({ ...form });
    savedSnapshot.value = { ...form };
    message.success('保存成功，密码策略已生效');
  } catch {
    // 错误提示已由 request.ts 统一处理
  } finally {
    saving.value = false;
  }
}

function onReset() {
  Object.assign(form, savedSnapshot.value);
}
</script>

<style scoped>
.policy-tip {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}
</style>
