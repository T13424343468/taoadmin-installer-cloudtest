<template>
  <div class="page-container">
    <a-row :gutter="16">
      <!-- 基本资料 -->
      <a-col :span="12">
        <a-card title="基本资料" :bordered="false">
          <template #extra>
            <span class="text-muted">账号：{{ profile.username }}</span>
          </template>
          <a-spin :spinning="loading">
            <a-form :model="form" layout="vertical">
              <!-- 头像：走文件中心现有上传，保存资料后生效 -->
              <a-form-item label="头像">
                <div class="avatar-block">
                  <a-upload
                    accept="image/*"
                    :show-upload-list="false"
                    :custom-request="onAvatarUpload"
                  >
                    <a-avatar :size="72" :src="avatarUrl" class="avatar-box">
                      {{ (form.nickname || profile.username || '用').charAt(0) }}
                    </a-avatar>
                  </a-upload>
                  <div class="avatar-hint">点击头像更换</div>
                </div>
              </a-form-item>
              <a-form-item label="昵称">
                <a-input v-model:value="form.nickname" placeholder="请输入昵称" :maxlength="50" />
              </a-form-item>
              <a-form-item label="手机号">
                <a-input v-model:value="form.phone" placeholder="请输入手机号" :maxlength="20" />
              </a-form-item>
              <a-form-item label="邮箱">
                <a-input v-model:value="form.email" placeholder="请输入邮箱" :maxlength="100" />
              </a-form-item>
              <a-form-item v-if="profile.lastLoginTime" label="上次登录">
                <span>{{ profile.lastLoginTime }}</span>
              </a-form-item>
              <a-button type="primary" :loading="saving" @click="onSaveProfile">保存资料</a-button>
            </a-form>
          </a-spin>
        </a-card>
      </a-col>

      <!-- 修改密码 -->
      <a-col :span="12">
        <a-card title="修改密码" :bordered="false">
          <a-form ref="pwdFormRef" :model="pwdForm" :rules="pwdRules" layout="vertical">
            <a-form-item label="旧密码" name="oldPassword">
              <a-input-password v-model:value="pwdForm.oldPassword" placeholder="请输入当前密码" />
            </a-form-item>
            <a-form-item label="新密码" name="newPassword">
              <a-input-password v-model:value="pwdForm.newPassword" placeholder="不少于 6 位" />
            </a-form-item>
            <a-form-item label="确认新密码" name="confirmPassword">
              <a-input-password v-model:value="pwdForm.confirmPassword" placeholder="再输入一次新密码" />
            </a-form-item>
            <a-button type="primary" :loading="changing" @click="onChangePassword">修改密码</a-button>
            <p class="pwd-hint">修改成功后所有登录会话将立即失效，需使用新密码重新登录。</p>
          </a-form>
        </a-card>

        <!-- 偏好设置（第五班路一：水印开关，记住偏好） -->
        <a-card title="偏好设置" :bordered="false" style="margin-top: 16px">
          <div class="pref-row">
            <div>
              <div class="pref-title">页面水印</div>
              <div class="pref-desc">在页面平铺当前登录人姓名水印，防截屏外泄、便于溯源</div>
            </div>
            <a-switch :checked="watermarkStore.enabled" @change="(v: any) => watermarkStore.setEnabled(!!v)" />
          </div>
        </a-card>
      </a-col>
    </a-row>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue';
import { message } from 'ant-design-vue';
import type { FormInstance } from 'ant-design-vue';
import { useRouter } from 'vue-router';
import { getMyProfile, saveMyProfile, changeMyPassword, type ProfileInfo } from '@/api/profile';
import { uploadFile, buildFileUrl } from '@/api/taoFiles';
import { useUserStore } from '@/stores/user';
import { useWatermarkStore } from '@/stores/watermark';

/** 头像上传参数（对齐 a-upload customRequest 入参，仅取用到的文件字段） */
interface AvatarUploadOption {
  file: File | Blob | string;
}

const router = useRouter();
const userStore = useUserStore();
const watermarkStore = useWatermarkStore();

const loading = ref(false);
const saving = ref(false);
const changing = ref(false);
const profile = ref<ProfileInfo>({ userId: '', username: '' });

const form = reactive<{ nickname?: string; avatar?: string; phone?: string; email?: string }>({});
const pwdFormRef = ref<FormInstance>();
const pwdForm = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' });

const pwdRules = {
  oldPassword: [{ required: true, message: '请输入旧密码', trigger: 'blur' }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 6, message: '新密码长度不能少于 6 位', trigger: 'blur' },
  ],
  confirmPassword: [{ required: true, message: '请再次输入新密码', trigger: 'blur' }],
};

/** 头像地址：完整 URL 直接用，相对路径拼文件中心地址 */
const avatarUrl = computed(() => {
  const a = form.avatar;
  if (!a) return '';
  return /^https?:\/\//.test(a) ? a : buildFileUrl(a);
});

/** 头像上传：走文件中心，仅暂存路径，保存资料时一并提交 */
async function onAvatarUpload(options: AvatarUploadOption) {
  try {
    const file = options.file as File;
    const result = await uploadFile(file);
    form.avatar = result.fileUrl;
    message.success('头像已上传，保存资料后生效');
  } catch (e: any) {
    message.error(e?.message || '头像上传失败，请检查文件中心服务');
  }
}

async function onSaveProfile() {
  saving.value = true;
  try {
    await saveMyProfile({ ...form });
    message.success('资料已更新');
    // 头像/昵称即时生效：刷新右上角门面
    await userStore.fetchUserInfo();
  } finally {
    saving.value = false;
  }
}

async function onChangePassword() {
  try {
    await pwdFormRef.value?.validate();
  } catch {
    return;
  }
  if (pwdForm.newPassword !== pwdForm.confirmPassword) {
    message.warning('两次输入的新密码不一致');
    return;
  }
  changing.value = true;
  try {
    await changeMyPassword({ oldPassword: pwdForm.oldPassword, newPassword: pwdForm.newPassword });
    message.success('密码修改成功，请重新登录');
    pwdForm.oldPassword = pwdForm.newPassword = pwdForm.confirmPassword = '';
    // 全部会话已失效，回登录页用新密码进入
    await userStore.logout();
    router.push('/login');
  } finally {
    changing.value = false;
  }
}

onMounted(async () => {
  loading.value = true;
  try {
    profile.value = await getMyProfile();
    Object.assign(form, {
      nickname: profile.value.nickname,
      avatar: profile.value.avatar,
      phone: profile.value.phone,
      email: profile.value.email,
    });
  } finally {
    loading.value = false;
  }
});
</script>

<style scoped>
.avatar-block {
  display: flex;
  align-items: center;
  gap: 12px;
}
.avatar-box {
  cursor: pointer;
}
.avatar-hint {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}
.pwd-hint {
  margin-top: 12px;
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}
.text-muted {
  color: rgba(0, 0, 0, 0.45);
}
.pref-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}
.pref-title {
  font-weight: 500;
}
.pref-desc {
  margin-top: 4px;
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}
</style>
