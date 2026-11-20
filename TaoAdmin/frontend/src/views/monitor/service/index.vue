<template>
  <div class="page-container">
    <a-page-header title="服务管理中心" sub-title="微服务实例状态与健康检查" style="padding-left: 0; padding-top: 0">
      <template #extra>
        <a-button :loading="loading" @click="loadData">刷新</a-button>
      </template>
    </a-page-header>

    <a-spin :spinning="loading">
      <a-empty v-if="!loading && services.length === 0" description="暂无服务注册信息" />

      <a-row :gutter="[16, 16]">
        <a-col v-for="svc in services" :key="svc.serviceName" :xs="24" :sm="12" :lg="8" :xl="6">
          <a-card size="small" :class="['service-card', svc.status.toLowerCase()]" hoverable>
              <template #title>
                <a-badge
                  :status="statusBadge(svc.status)"
                  :text="svc.displayName || svc.serviceName"
                  class="svc-title"
                />
              </template>
              <template #extra>
                <a-tag :color="statusColor(svc.status)">{{ statusText(svc.status) }}</a-tag>
              </template>

              <a-descriptions :column="1" size="small">
                <a-descriptions-item label="服务标识">
                  <code>{{ svc.serviceName }}</code>
                </a-descriptions-item>
                <a-descriptions-item label="实例数">
                  <a-badge :count="svc.instances" :number-style="{ backgroundColor: '#1890ff' }" />
                </a-descriptions-item>
                <a-descriptions-item label="健康检查时间">
                  {{ svc.lastHealthCheck || '-' }}
                </a-descriptions-item>
                <a-descriptions-item v-if="svc.version" label="版本">
                  {{ svc.version }}
                </a-descriptions-item>
                <a-descriptions-item v-if="svc.description" label="描述">
                  {{ svc.description }}
                </a-descriptions-item>
              </a-descriptions>

              <!-- 实例明细 -->
              <div v-if="svc.instanceList?.length" class="instance-list">
                <div class="instance-title">实例明细</div>
                <div v-for="ins in svc.instanceList" :key="ins.instanceId" class="instance-item">
                  <a-badge :status="ins.status === 'UP' ? 'success' : 'error'" />
                  <span class="instance-addr">{{ ins.host }}:{{ ins.port }}</span>
                  <span class="text-muted instance-time">{{ ins.lastHeartbeat || '' }}</span>
                </div>
              </div>
          </a-card>
        </a-col>
      </a-row>
    </a-spin>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { getServices, type ServiceItem } from '@/api/monitor';

const loading = ref(false);
const services = ref<ServiceItem[]>([]);
let timer: ReturnType<typeof setInterval> | null = null;

function statusColor(status: string): string {
  const map: Record<string, string> = { UP: 'green', DOWN: 'red', PARTIAL: 'orange' };
  return map[status] || 'default';
}

function statusText(status: string): string {
  const map: Record<string, string> = { UP: '正常', DOWN: '宕机', PARTIAL: '部分可用' };
  return map[status] || status;
}

function statusBadge(status: string): 'success' | 'error' | 'warning' {
  const map: Record<string, 'success' | 'error' | 'warning'> = {
    UP: 'success',
    DOWN: 'error',
    PARTIAL: 'warning',
  };
  return map[status] || 'warning';
}

async function loadData() {
  loading.value = true;
  try {
    services.value = (await getServices()) || [];
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  loadData();
  // 每 30 秒自动刷新
  timer = setInterval(loadData, 30000);
});

onUnmounted(() => {
  if (timer) clearInterval(timer);
});
</script>

<style scoped lang="less">
.service-card {
  transition: box-shadow 0.2s;

  &.up {
    border-top: 2px solid #52c41a;
  }
  &.down {
    border-top: 2px solid #ff4d4f;
  }
  &.partial {
    border-top: 2px solid #faad14;
  }
}

.svc-title {
  font-weight: 600;
}

.instance-list {
  margin-top: 8px;
  border-top: 1px dashed #f0f0f0;
  padding-top: 8px;

  .instance-title {
    font-size: 12px;
    color: rgba(0, 0, 0, 0.45);
    margin-bottom: 4px;
  }

  .instance-item {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 2px 0;
    font-size: 12px;

    .instance-addr {
      font-family: Consolas, Monaco, monospace;
    }

    .instance-time {
      margin-left: auto;
      font-size: 11px;
    }
  }
}
</style>
