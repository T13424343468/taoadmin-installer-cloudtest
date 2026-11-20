<template>
  <div class="page-container">
    <a-page-header title="首页" sub-title="系统概览" style="padding-left: 0; padding-top: 0" />

    <!-- 统计卡片 -->
    <a-row :gutter="16">
      <a-col :span="6" v-for="card in statCards" :key="card.title">
        <a-card :loading="loading">
          <a-statistic :title="card.title" :value="card.value">
            <template #prefix>
              <component :is="card.icon" :style="{ color: card.color }" />
            </template>
          </a-statistic>
        </a-card>
      </a-col>
    </a-row>

    <!-- 服务状态 -->
    <a-row :gutter="16" style="margin-top: 16px">
      <a-col :span="12">
        <a-card title="服务状态" :loading="loading" size="small">
          <a-list :data-source="services" size="small">
            <template #renderItem="{ item }">
              <a-list-item>
                <a-list-item-meta>
                  <template #title>
                    <a-badge
                      :status="item.status === 'UP' ? 'success' : 'error'"
                      :text="item.displayName || item.serviceName"
                    />
                  </template>
                  <template #description>
                    实例数：{{ item.instances }} · 最近检查：{{ item.lastHealthCheck || '-' }}
                  </template>
                </a-list-item-meta>
              </a-list-item>
            </template>
            <template #empty>
              <a-empty description="暂无服务数据" />
            </template>
          </a-list>
        </a-card>
      </a-col>

      <a-col :span="12">
        <a-card title="运行中定时任务" :loading="loading" size="small">
          <a-list :data-source="runningJobs.slice(0, 6)" size="small">
            <template #renderItem="{ item }">
              <a-list-item>
                <a-list-item-meta :title="item.name" :description="`Cron: ${item.cron} · 下次执行: ${item.nextFireTime || '-'}`" />
              </a-list-item>
            </template>
            <template #empty>
              <a-empty description="暂无运行中的任务" />
            </template>
          </a-list>
        </a-card>
      </a-col>
    </a-row>

    <!-- 最近日志 -->
    <a-card title="最近错误日志" style="margin-top: 16px" size="small" :loading="loading">
      <a-table
        :data-source="recentErrorLogs"
        :columns="logColumns"
        :pagination="false"
        row-key="id"
        size="small"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'level'">
            <a-tag :color="record.level === 'ERROR' ? 'red' : 'orange'">{{ record.level }}</a-tag>
          </template>
          <template v-else-if="column.key === 'message'">
            <span class="log-message">{{ record.message }}</span>
          </template>
        </template>
        <template #empty>
          <a-empty description="暂无错误日志" />
        </template>
      </a-table>
    </a-card>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import {
  UserOutlined,
  ClusterOutlined,
  ClockCircleOutlined,
  FileTextOutlined,
} from '@ant-design/icons-vue';
import { getServices } from '@/api/monitor';
import { getJobPage, type JobItem } from '@/api/job';
import { getLogs, type LogItem, type ServiceItem } from '@/api/monitor';

const loading = ref(false);
const services = ref<ServiceItem[]>([]);
const runningJobs = ref<JobItem[]>([]);
const recentErrorLogs = ref<LogItem[]>([]);

const statCards = ref([
  { title: '注册用户', value: 0, icon: UserOutlined, color: '#1890ff' },
  { title: '在线服务', value: 0, icon: ClusterOutlined, color: '#52c41a' },
  { title: '定时任务', value: 0, icon: ClockCircleOutlined, color: '#faad14' },
  { title: '今日日志', value: 0, icon: FileTextOutlined, color: '#722ed1' },
]);

const logColumns = [
  { title: '时间', dataIndex: 'timestamp', key: 'timestamp', width: 180 },
  { title: '级别', key: 'level', width: 80 },
  { title: '服务', dataIndex: 'service', key: 'service', width: 140 },
  { title: '内容', key: 'message', ellipsis: true },
];

async function loadStats() {
  loading.value = true;
  try {
    const [svc, jobs, logs] = await Promise.all([
      getServices().catch(() => []),
      getJobPage({ pageIndex: 1, pageSize: 50 }).catch(() => ({ items: [], total: 0 }) as any),
      getLogs({ pageIndex: 1, pageSize: 10, level: 'ERROR' }).catch(() => ({ items: [], total: 0 }) as any),
    ]);

    services.value = svc || [];
    runningJobs.value = jobs.items || [];
    recentErrorLogs.value = logs.items || [];

    statCards.value[0].value = 0; // 用户数从 /identity/users 获取，此处默认展示，可按需接入
    statCards.value[1].value = (svc || []).filter((s) => s.status === 'UP').length;
    statCards.value[2].value = jobs.total || 0;
    statCards.value[3].value = logs.total || 0;
  } finally {
    loading.value = false;
  }
}

onMounted(loadStats);
</script>

<style scoped lang="less">
.log-message {
  color: rgba(0, 0, 0, 0.65);
  font-size: 12px;
  font-family: Consolas, Monaco, monospace;
}
</style>
