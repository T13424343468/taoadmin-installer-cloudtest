<template>
  <div class="apidocs">
    <a-card :bordered="false">
      <template #title>
        <span>接口文档</span>
      </template>
      <a-alert
        type="info"
        show-icon
        style="margin-bottom: 12px"
        message="文档默认仅服务本机可访问；如需放宽，请在站点配置中将 DocsAccess:Mode 改为 Authenticated（登录后可看）。"
      />
      <a-tabs v-model:active-key="activeService" tab-position="left">
        <a-tab-pane v-for="svc in services" :key="svc.key" :tab="svc.name">
          <a-descriptions :column="1" size="small" style="margin-bottom: 12px">
            <a-descriptions-item label="服务">{{ svc.name }}</a-descriptions-item>
            <a-descriptions-item label="文档地址">
              <a :href="svc.url" target="_blank" rel="noopener">{{ svc.url }}</a>
            </a-descriptions-item>
            <a-descriptions-item label="说明">{{ svc.desc }}</a-descriptions-item>
          </a-descriptions>
          <a-button type="primary" @click="openDocs(svc)">
            <template #icon><ApiOutlined /></template>
            新窗口打开文档
          </a-button>
        </a-tab-pane>
      </a-tabs>
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { ApiOutlined } from '@ant-design/icons-vue';

interface DocService {
  key: string;
  name: string;
  url: string;
  desc: string;
}

const activeService = ref('identity');

const services: DocService[] = [
  {
    key: 'identity',
    name: '权限服务',
    url: 'http://127.0.0.1:5001/scalar',
    desc: '认证/用户/角色/菜单/接口/租户/工作流/消息/字典/日志等主要业务接口',
  },
  {
    key: 'task',
    name: '任务服务',
    url: 'http://127.0.0.1:5002/scalar',
    desc: '定时任务管理与执行历史接口',
  },
  {
    key: 'files',
    name: '文件中心',
    url: 'http://127.0.0.1:5003/scalar',
    desc: '文件上传（MD5 去重/秒传）、列表、删除、下载接口',
  },
  {
    key: 'gateway',
    name: '网关',
    url: 'http://127.0.0.1:5000/scalar',
    desc: '网关自身健康检查与转发说明',
  },
];

function openDocs(svc: DocService) {
  window.open(svc.url, '_blank', 'noopener');
}
</script>

<style scoped>
.apidocs {
  padding: 12px;
}
</style>
