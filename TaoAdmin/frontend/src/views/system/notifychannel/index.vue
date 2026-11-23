<template>
  <div class="page-container" style="max-width: 860px">
    <a-card title="通知渠道">
      <a-form layout="vertical" :model="form">
        <a-divider orientation="left">SMTP 邮件</a-divider>
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="SMTP 服务器"><a-input v-model:value="form.notify_smtp_host" placeholder="如 smtp.example.com" /></a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="端口"><a-input-number v-model:value="port" :min="1" :max="65535" style="width: 100%" placeholder="465 走隐式 TLS" /></a-form-item>
          </a-col>
        </a-row>
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="发件账号"><a-input v-model:value="form.notify_smtp_user" /></a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="发件密码/授权码"><a-input-password v-model:value="form.notify_smtp_pass" /></a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="发件人地址"><a-input v-model:value="form.notify_smtp_from" placeholder="如 noreply@example.com" /></a-form-item>
        <a-form-item label="告警接收邮箱（多个用逗号分隔）">
          <a-input v-model:value="form.notify_alert_receivers" placeholder="如 admin@example.com,ops@example.com" />
        </a-form-item>
        <a-divider orientation="left">钉钉机器人</a-divider>
        <a-form-item label="Webhook 地址">
          <a-input v-model:value="form.notify_dingtalk_webhook" placeholder="oapi.dingtalk.com/robot/send?access_token=..." />
        </a-form-item>
        <a-form-item>
          <a-space>
            <a-button type="primary" :loading="saving" @click="onSave">保存</a-button>
            <a-button :loading="testing === 'email'" @click="onTest('email')">发送测试消息（邮件）</a-button>
            <a-button :loading="testing === 'dingtalk'" @click="onTest('dingtalk')">发送测试消息（钉钉）</a-button>
          </a-space>
        </a-form-item>
      </a-form>
    </a-card>

    <a-card title="告警规则" style="margin-top: 16px">
      <template #extra>
        <a-button type="primary" size="small" @click="openRuleForm()">新增规则</a-button>
      </template>
      <a-table :data-source="rules" :loading="rulesLoading" row-key="id" size="middle" :pagination="false">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'eventType'">
            <a-tag :color="record.eventType === 'SERVICE_DOWN' ? 'red' : 'orange'">
              {{ record.eventType === 'SERVICE_DOWN' ? '服务下线' : '任务连续失败' }}
            </a-tag>
          </template>
          <template v-else-if="column.key === 'channels'">
            <a-tag v-for="c in String(record.channels).split(',')" :key="c" color="blue">{{ channelLabel(c) }}</a-tag>
          </template>
          <template v-else-if="column.key === 'enabled'">
            <a-switch
              :checked="record.enabled"
              @change="(v: any) => onToggleRule(record, !!v)"
            />
          </template>
          <template v-else-if="column.key === 'action'">
            <a-space>
              <a @click="openRuleForm(record)">编辑</a>
              <a-popconfirm title="确定删除该告警规则吗？" @confirm="onDeleteRule(record)">
                <a class="danger-link">删除</a>
              </a-popconfirm>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>

    <a-card title="发送记录" style="margin-top: 16px">
      <a-table
        :data-source="logs"
        :loading="logsLoading"
        row-key="id"
        size="middle"
        :pagination="logPagination"
        @change="onLogPageChange"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'channel'">
            <a-tag color="blue">{{ channelLabel(record.channel) }}</a-tag>
          </template>
          <template v-else-if="column.key === 'success'">
            <a-tag :color="record.success ? 'green' : 'red'">{{ record.success ? '成功' : '失败' }}</a-tag>
          </template>
          <template v-else-if="column.key === 'error'">
            <span v-if="record.error" class="error-text">{{ record.error }}</span>
            <span v-else class="text-muted">-</span>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- 新增/编辑告警规则弹窗 -->
    <a-modal
      v-model:open="ruleFormVisible"
      :title="ruleForm.id ? '编辑告警规则' : '新增告警规则'"
      :confirm-loading="ruleSubmitting"
      @ok="onRuleSubmit"
    >
      <a-form :model="ruleForm" :rules="ruleFormRules" layout="vertical">
        <a-form-item label="规则名称" name="name">
          <a-input v-model:value="ruleForm.name" placeholder="如：服务下线告警" />
        </a-form-item>
        <a-row :gutter="16">
          <a-col :span="12">
            <a-form-item label="事件类型" name="eventType">
              <a-select v-model:value="ruleForm.eventType">
                <a-select-option value="SERVICE_DOWN">服务下线</a-select-option>
                <a-select-option value="JOB_FAIL">任务连续失败</a-select-option>
              </a-select>
            </a-form-item>
          </a-col>
          <a-col :span="12">
            <a-form-item label="连续失败阈值" name="threshold">
              <a-input-number v-model:value="ruleForm.threshold" :min="1" style="width: 100%" />
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="通知渠道" name="channelChecked">
          <a-checkbox-group v-model:value="ruleForm.channelChecked">
            <a-checkbox value="email">邮件</a-checkbox>
            <a-checkbox value="dingtalk">钉钉</a-checkbox>
          </a-checkbox-group>
        </a-form-item>
        <a-form-item label="备注">
          <a-textarea v-model:value="ruleForm.remark" :rows="2" placeholder="规则说明" />
        </a-form-item>
      </a-form>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import {
  getNotifyChannel,
  saveNotifyChannel,
  sendNotifyTest,
  getNotifyLogs,
  getAlertRules,
  saveAlertRule,
  deleteAlertRule,
  type AlertRuleItem,
  type NotifyLogItem,
} from '@/api/audit';

const KEYS = [
  'notify_smtp_host', 'notify_smtp_port', 'notify_smtp_user', 'notify_smtp_pass', 'notify_smtp_from',
  'notify_dingtalk_webhook', 'notify_alert_receivers',
];

const configs = reactive<Record<string, string>>({});
const form = reactive<Record<string, string | undefined>>({});
const saving = ref(false);
const testing = ref<'email' | 'dingtalk' | null>(null);
const port = computed<number | undefined>({
  get: () => (form.notify_smtp_port ? Number(form.notify_smtp_port) : undefined),
  set: (v) => { form.notify_smtp_port = v == null ? '' : String(v); },
});

// ============ 告警规则 ============

const rules = ref<AlertRuleItem[]>([]);
const rulesLoading = ref(false);
const ruleFormVisible = ref(false);
const ruleSubmitting = ref(false);
const ruleForm = reactive<{
  id?: string;
  name: string;
  eventType: 'SERVICE_DOWN' | 'JOB_FAIL';
  threshold: number;
  channelChecked: string[];
  remark?: string;
}>({ id: undefined, name: '', eventType: 'SERVICE_DOWN', threshold: 1, channelChecked: ['email'], remark: undefined });

const ruleFormRules = {
  name: [{ required: true, message: '请输入规则名称', trigger: 'blur' }],
  eventType: [{ required: true, message: '请选择事件类型', trigger: 'change' }],
};

function channelLabel(c: string): string {
  return c === 'email' ? '邮件' : c === 'dingtalk' ? '钉钉' : c;
}

async function loadRules() {
  rulesLoading.value = true;
  try {
    rules.value = (await getAlertRules()) || [];
  } finally {
    rulesLoading.value = false;
  }
}

function openRuleForm(record?: AlertRuleItem) {
  if (record) {
    Object.assign(ruleForm, {
      id: record.id,
      name: record.name,
      eventType: record.eventType,
      threshold: record.threshold,
      channelChecked: String(record.channels || '').split(',').filter(Boolean),
      remark: record.remark ?? '',
    });
  } else {
    Object.assign(ruleForm, {
      id: undefined, name: '', eventType: 'SERVICE_DOWN', threshold: 1,
      channelChecked: ['email'], remark: '',
    });
  }
  ruleFormVisible.value = true;
}

async function onRuleSubmit() {
  if (!ruleForm.channelChecked.length) {
    message.warning('请至少选择一个通知渠道');
    return;
  }
  ruleSubmitting.value = true;
  try {
    await saveAlertRule({
      id: ruleForm.id,
      name: ruleForm.name,
      eventType: ruleForm.eventType,
      threshold: ruleForm.threshold,
      channels: ruleForm.channelChecked.join(','),
      enabled: true,
      remark: ruleForm.remark,
    });
    message.success(ruleForm.id ? '更新成功' : '新增成功');
    ruleFormVisible.value = false;
    loadRules();
  } finally {
    ruleSubmitting.value = false;
  }
}

async function onToggleRule(record: AlertRuleItem, enabled: boolean) {
  await saveAlertRule({
    id: record.id,
    name: record.name,
    eventType: record.eventType as 'SERVICE_DOWN' | 'JOB_FAIL',
    threshold: record.threshold,
    channels: record.channels,
    enabled,
    remark: record.remark ?? undefined,
  });
  message.success(enabled ? '规则已启用' : '规则已停用');
  loadRules();
}

async function onDeleteRule(record: AlertRuleItem) {
  await deleteAlertRule(record.id);
  message.success('删除成功');
  loadRules();
}

// ============ 发送记录 ============

const logs = ref<NotifyLogItem[]>([]);
const logsLoading = ref(false);
const logPagination = reactive({
  current: 1,
  pageSize: 10,
  total: 0,
  showTotal: (t: number) => `共 ${t} 条`,
});

async function loadLogs() {
  logsLoading.value = true;
  try {
    const res = await getNotifyLogs({ pageIndex: logPagination.current, pageSize: logPagination.pageSize });
    logs.value = res.items || [];
    logPagination.total = res.total || 0;
  } finally {
    logsLoading.value = false;
  }
}

function onLogPageChange(pag: any) {
  logPagination.current = pag.current;
  logPagination.pageSize = pag.pageSize;
  loadLogs();
}

// ============ 配置读写与测试消息 ============

onMounted(async () => {
  const c = await getNotifyChannel();
  for (const k of KEYS) {
    configs[k] = c[k] ?? '';
    form[k] = c[k] ?? '';
  }
  loadRules();
  loadLogs();
});

async function onSave() {
  saving.value = true;
  try {
    for (const k of KEYS) configs[k] = form[k] ?? '';
    await saveNotifyChannel({ ...configs });
    message.success('保存成功，配置即时生效');
  } finally {
    saving.value = false;
  }
}

async function onTest(channel: 'email' | 'dingtalk') {
  testing.value = channel;
  try {
    const msg = await sendNotifyTest(channel);
    message.success(msg || '测试消息已发送');
    loadLogs();
  } catch (e: any) {
    message.error(e?.message || '测试消息发送失败');
    loadLogs();
  } finally {
    testing.value = null;
  }
}
</script>

<style scoped>
.danger-link {
  color: #ff4d4f;
}

.error-text {
  color: #ff4d4f;
}

.text-muted {
  color: #999;
}
</style>
