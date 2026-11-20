<template>
  <div class="page-container">
    <a-card title="站点参数" size="small" style="max-width: 640px">
      <template #extra>
        <span class="config-tip">改完即生效，无需重启</span>
      </template>

      <a-skeleton v-if="loading" active :paragraph="{ rows: 5 }" />

      <a-form
        v-else
        ref="formRef"
        :model="form"
        :rules="rules"
        layout="vertical"
      >
        <a-form-item label="站点名称" name="siteName" extra="显示在登录页标题、侧边栏与浏览器标签页">
          <a-input v-model:value="form.siteName" placeholder="如：TaoAdmin" :maxlength="50" show-count />
        </a-form-item>

        <a-form-item label="站点 Logo" name="siteLogo" extra="走文件中心上传，未配置时使用默认图标">
          <div class="logo-field">
            <a-upload
              accept="image/*"
              :show-upload-list="false"
              :before-upload="onLogoUpload"
            >
              <a-button>
                <template #icon><UploadOutlined /></template>
                上传 Logo
              </a-button>
            </a-upload>
            <a-button v-if="form.siteLogo" type="link" danger @click="form.siteLogo = ''">移除</a-button>
          </div>
          <div class="logo-preview">
            <img v-if="form.siteLogo" :src="logoPreviewUrl" alt="站点 Logo" class="logo-img" />
            <span v-else class="logo-empty">⚙️ 未配置，前台使用默认图标</span>
          </div>
        </a-form-item>

        <a-form-item label="页脚文案" name="siteFooter" extra="显示在全站页脚，可为空（不显示）">
          <a-input v-model:value="form.siteFooter" placeholder="如：TaoAdmin ©2026" :maxlength="100" show-count />
        </a-form-item>

        <a-form-item>
          <a-space>
            <a-button
              v-permission="'sys:site:manage'"
              type="primary"
              :loading="saving"
              @click="onSave"
            >
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
import { computed, onMounted, reactive, ref } from 'vue';
import { useRoute } from 'vue-router';
import { message } from 'ant-design-vue';
import { UploadOutlined } from '@ant-design/icons-vue';
import { getSiteConfig, saveSiteConfig, useSiteStore } from '@/api/site';
import { uploadFile, buildFileUrl } from '@/api/taoFiles';
import { applyDocumentTitle } from '@/router';

const siteStore = useSiteStore();
const route = useRoute();

const formRef = ref();
const loading = ref(true);
const saving = ref(false);
const form = reactive({ siteName: '', siteLogo: '', siteFooter: '' });

const rules = {
  siteName: [
    { required: true, message: '请输入站点名称', trigger: 'blur' },
    { max: 50, message: '站点名称不超过 50 个字符', trigger: 'blur' },
  ],
};

const logoPreviewUrl = computed(() =>
  form.siteLogo ? (/^https?:\/\//.test(form.siteLogo) ? form.siteLogo : buildFileUrl(form.siteLogo)) : ''
);

onMounted(async () => {
  // 远端配置拉到后与门面全局状态共用一份，保证设置页与已渲染门面一致；
  // 拉取失败不再卡骨架屏：回退全局状态里的当前配置（内置默认值），错误提示由请求封装统一给出
  try {
    const cfg = await getSiteConfig();
    form.siteName = cfg.siteName;
    form.siteLogo = cfg.siteLogo;
    form.siteFooter = cfg.siteFooter;
  } catch {
    form.siteName = siteStore.config.siteName;
    form.siteLogo = siteStore.config.siteLogo;
    form.siteFooter = siteStore.config.siteFooter;
  } finally {
    loading.value = false;
  }
});

/** Logo 走文件中心现有上传能力：上传成功后存相对路径 */
async function onLogoUpload(file: File): Promise<boolean> {
  if (!file.type.startsWith('image/')) {
    message.error('请选择图片文件');
    return false;
  }
  if (file.size > 5 * 1024 * 1024) {
    message.error('Logo 图片不超过 5MB');
    return false;
  }
  try {
    const r = await uploadFile(file);
    form.siteLogo = r.fileUrl;
    message.success('Logo 已上传，保存后全站生效');
  } catch {
    // 错误提示已由封装统一给出
  }
  return false; // 阻断 a-upload 默认上传，由这里接管
}

async function onSave() {
  try {
    await formRef.value?.validate();
  } catch {
    return;
  }
  saving.value = true;
  try {
    await saveSiteConfig({ ...form });
    const cfg = await getSiteConfig();
    siteStore.applyLocal(cfg);
    // 标签页标题同步新站点名，门面三处（侧栏/页脚/标题）一致生效
    applyDocumentTitle(route.meta?.title as string | undefined);
    message.success('保存成功，全站门面已生效');
  } catch {
    // 错误提示已由 request.ts 统一处理
  } finally {
    saving.value = false;
  }
}

function onReset() {
  form.siteName = siteStore.config.siteName;
  form.siteLogo = siteStore.config.siteLogo;
  form.siteFooter = siteStore.config.siteFooter;
}
</script>

<style scoped lang="less">
.config-tip {
  color: rgba(0, 0, 0, 0.45);
  font-size: 12px;
}

.logo-field {
  display: flex;
  align-items: center;
  gap: 8px;
}

.logo-preview {
  margin-top: 8px;

  .logo-img {
    height: 40px;
    max-width: 160px;
    object-fit: contain;
    border-radius: 4px;
  }

  .logo-empty {
    color: rgba(0, 0, 0, 0.45);
    font-size: 12px;
  }
}
</style>
