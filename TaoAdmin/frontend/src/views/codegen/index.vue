<template>
  <div class="page-container">
    <a-tabs v-model:active-key="activeTab">
      <!-- ============ 表设计 ============ -->
      <a-tab-pane key="designs" title="表设计">
        <div class="table-operator">
          <a-input
            v-model:value="designQuery.keyword"
            placeholder="搜索表名/中文名"
            allow-clear
            style="width: 220px"
            @press-enter="loadDesigns()"
          />
          <a-button type="primary" style="margin-left: 8px" @click="loadDesigns()">查询</a-button>
          <a-button v-permission="'sys:codegen:manage'" type="primary" style="margin-left: 12px" @click="openCreate">
            <template #icon><PlusOutlined /></template>
            新建表设计
          </a-button>
        </div>

        <a-table
          :data-source="designs"
          :columns="designColumns"
          :loading="designLoading"
          row-key="id"
          size="middle"
          :pagination="designPagination"
          @change="onDesignTableChange"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'tableName'">
              <a @click="openEdit(record)">{{ record.tableName }}</a>
            </template>
            <template v-else-if="column.key === 'fieldCount'">
              {{ parseFieldCount(record.fieldsJson) }}
            </template>
            <template v-else-if="column.key === 'lastSyncAt'">
              <a-tag v-if="record.lastSyncAt" color="green">已同步</a-tag>
              <a-tag v-else color="orange">未同步</a-tag>
            </template>
            <template v-else-if="column.key === 'action'">
              <a-space>
                <a v-permission="'sys:codegen:manage'" @click="openEdit(record)">设计</a>
                <a-popconfirm
                  v-permission="'sys:codegen:manage'"
                  title="同步会按设计建表/加列（自动执行），确定继续吗？"
                  @confirm="onSync(record)"
                >
                  <a v-permission="'sys:codegen:manage'">同步建库</a>
                </a-popconfirm>
                <a-popconfirm
                  v-permission="'sys:codegen:manage'"
                  title="一键生成全套增删改查前后端代码，确定继续吗？"
                  @confirm="onGenerate(record)"
                >
                  <a v-permission="'sys:codegen:manage'">一键生成</a>
                </a-popconfirm>
                <a-popconfirm
                  v-permission="'sys:codegen:manage'"
                  title="删除表设计不影响已生成的库表与代码，确定删除吗？"
                  @confirm="onDeleteDesign(record)"
                >
                  <a class="danger-link" v-permission="'sys:codegen:manage'">删除</a>
                </a-popconfirm>
              </a-space>
            </template>
          </template>
        </a-table>
      </a-tab-pane>

      <!-- ============ 生成记录 ============ -->
      <a-tab-pane key="records" title="生成记录">
        <a-table
          :data-source="records"
          :columns="recordColumns"
          :loading="recordLoading"
          row-key="id"
          size="middle"
          :pagination="recordPagination"
          @change="onRecordTableChange"
        >
          <template #bodyCell="{ column, record }">
            <template v-if="column.key === 'mode'">
              <a-tag>{{ record.mode === 'generate' ? '生成' : record.mode }}</a-tag>
            </template>
            <template v-else-if="column.key === 'elapsedMs'">
              {{ record.elapsedMs }} ms
            </template>
            <template v-else-if="column.key === 'action'">
              <a @click="openRecordDetail(record)">查看文件</a>
            </template>
          </template>
        </a-table>
      </a-tab-pane>
    </a-tabs>

    <!-- ============ 表设计编辑弹窗 ============ -->
    <a-modal
      v-model:open="editVisible"
      :title="editForm.id ? '表设计：' + editForm.tableName : '新建表设计'"
      width="960px"
      :confirm-loading="saving"
      ok-text="保存设计"
      @ok="onSaveDesign"
    >
      <a-form :model="editForm" layout="vertical">
        <a-row :gutter="16">
          <a-col :span="8">
            <a-form-item label="表名（英文下划线风格）" name="tableName" required>
              <a-input v-model:value="editForm.tableName" :disabled="!!editForm.id" placeholder="如 biz_contract" />
            </a-form-item>
          </a-col>
          <a-col :span="8">
            <a-form-item label="中文名（业务名）" name="displayName" required>
              <a-input v-model:value="editForm.displayName" placeholder="如 合同管理" />
            </a-form-item>
          </a-col>
          <a-col :span="8">
            <a-form-item label="备注" name="remark">
              <a-input v-model:value="editForm.remark" placeholder="选填" />
            </a-form-item>
          </a-col>
        </a-row>

        <!-- 字段设计编辑表 -->
        <a-form-item label="字段设计" required>
          <a-table
            :data-source="editForm.fields"
            :columns="fieldColumns"
            :pagination="false"
            row-key="_key"
            size="small"
            :scroll="{ x: 1080 }"
          >
            <template #bodyCell="{ column, record, index }">
              <template v-if="column.key === 'name'">
                <a-input v-model:value="record.name" placeholder="英文" :disabled="!!editForm.id" />
              </template>
              <template v-else-if="column.key === 'label'">
                <a-input v-model:value="record.label" placeholder="中文" />
              </template>
              <template v-else-if="column.key === 'dataType'">
                <a-select v-model:value="record.dataType" style="width: 110px">
                  <a-select-option value="string">string</a-select-option>
                  <a-select-option value="int">int</a-select-option>
                  <a-select-option value="long">long</a-select-option>
                  <a-select-option value="decimal">decimal</a-select-option>
                  <a-select-option value="datetime">datetime</a-select-option>
                  <a-select-option value="bool">bool</a-select-option>
                  <a-select-option value="text">text</a-select-option>
                </a-select>
              </template>
              <template v-else-if="column.key === 'length'">
                <a-input-number
                  v-if="record.dataType === 'string' || record.dataType === 'decimal'"
                  v-model:value="record.length"
                  :min="1"
                  :max="record.dataType === 'decimal' ? 38 : 8000"
                  size="small"
                  style="width: 80px"
                />
                <span v-else>-</span>
              </template>
              <template v-else-if="column.key === 'precision'">
                <a-input-number
                  v-if="record.dataType === 'decimal'"
                  v-model:value="record.precision"
                  :min="0"
                  :max="10"
                  size="small"
                  style="width: 70px"
                />
                <span v-else>-</span>
              </template>
              <template v-else-if="column.key === 'nullable'">
                <a-checkbox v-model:checked="record.nullable" />
              </template>
              <template v-else-if="column.key === 'showInList'">
                <a-checkbox v-model:checked="record.showInList" />
              </template>
              <template v-else-if="column.key === 'control'">
                <a-select v-model:value="record.control" style="width: 100px">
                  <a-select-option value="text">文本</a-select-option>
                  <a-select-option value="number">数字</a-select-option>
                  <a-select-option value="date">日期</a-select-option>
                  <a-select-option value="textarea">多行</a-select-option>
                  <a-select-option value="dict">字典</a-select-option>
                  <a-select-option value="switch">开关</a-select-option>
                </a-select>
              </template>
              <template v-else-if="column.key === 'dictCode'">
                <a-input
                  v-if="record.control === 'dict'"
                  v-model:value="record.dictCode"
                  placeholder="字典编码"
                  size="small"
                />
                <span v-else>-</span>
              </template>
              <template v-else-if="column.key === 'queryType'">
                <a-select v-model:value="record.queryType" style="width: 90px">
                  <a-select-option value="none">不查询</a-select-option>
                  <a-select-option value="eq">精确</a-select-option>
                  <a-select-option value="like">模糊</a-select-option>
                  <a-select-option value="range">范围</a-select-option>
                </a-select>
              </template>
              <template v-else-if="column.key === 'fieldAction'">
                <a class="danger-link" @click="removeField(index)">移除</a>
              </template>
            </template>
          </a-table>
          <a-button type="dashed" block style="margin-top: 8px" @click="addField">+ 添加字段</a-button>
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- ============ 同步结果弹窗 ============ -->
    <a-modal v-model:open="syncVisible" title="同步建库结果" :footer="null" width="640px">
      <a-alert
        v-if="syncResult?.executed"
        type="success"
        show-icon
        message="变更已自动执行"
        style="margin-bottom: 12px"
      />
      <a-alert
        v-else
        type="info"
        show-icon
        message="结构与设计一致，无需变更"
        style="margin-bottom: 12px"
      />
      <div v-if="syncResult?.actions?.length" style="margin-bottom: 12px">
        <h4>已执行</h4>
        <ul class="sync-list">
          <li v-for="(a, i) in syncResult.actions" :key="i">{{ a }}</li>
        </ul>
      </div>
      <div v-if="syncResult?.destructivePending?.length">
        <a-alert type="warning" show-icon message="破坏性变更（删列/改类型）不会自动执行，请逐条确认" style="margin-bottom: 8px" />
        <div v-for="(sql, i) in syncResult.destructivePending" :key="i" class="sql-line">
          <code>{{ sql }}</code>
          <a-popconfirm title="该语句不可恢复，确定执行吗？" @confirm="onExecuteDestructive(sql)">
            <a-button size="small" danger style="margin-left: 8px">执行</a-button>
          </a-popconfirm>
        </div>
      </div>
    </a-modal>

    <!-- ============ 生成结果弹窗 ============ -->
    <a-modal v-model:open="genVisible" title="生成结果" :footer="null" width="860px">
      <a-alert
        type="success"
        show-icon
        :message="`已生成 ${genResult?.files?.length || 0} 个文件，耗时 ${genResult?.elapsedMs || 0} ms`"
        style="margin-bottom: 12px"
      />
      <a-collapse>
        <a-collapse-panel v-for="f in genResult?.files || []" :key="f.path" :header="f.path">
          <pre class="code-preview"><code>{{ f.content }}</code></pre>
        </a-collapse-panel>
      </a-collapse>
    </a-modal>

    <!-- ============ 生成记录详情弹窗 ============ -->
    <a-modal v-model:open="recordVisible" title="生成记录详情" :footer="null" width="860px">
      <a-collapse>
        <a-collapse-panel v-for="f in recordFiles" :key="f.path" :header="f.path">
          <pre class="code-preview"><code>{{ f.content }}</code></pre>
        </a-collapse-panel>
      </a-collapse>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import {
  getCodegenDesigns,
  saveCodegenDesign,
  deleteCodegenDesign,
  syncCodegenDesign,
  executeDestructiveSql,
  generateCodegen,
  getCodegenRecords,
  getCodegenRecord,
  type CodegenDesign,
  type CodegenFieldDesign,
  type CodegenSyncResult,
  type CodegenGenerateResult,
  type CodegenRecord,
} from '@/api/codegen';
import { message } from 'ant-design-vue';
import { PlusOutlined } from '@ant-design/icons-vue';
import { ref, reactive, onMounted } from 'vue';

// ============ 表设计列表 ============
const activeTab = ref('designs');
const designs = ref<CodegenDesign[]>([]);
const designLoading = ref(false);
const designQuery = reactive({ keyword: '' });
const designPagination = reactive({ current: 1, pageSize: 10, total: 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 条` });

const designColumns = [
  { title: '表名', dataIndex: 'tableName', key: 'tableName' },
  { title: '中文名', dataIndex: 'displayName', key: 'displayName' },
  { title: '字段数', key: 'fieldCount', width: 90 },
  { title: '同步状态', key: 'lastSyncAt', width: 110 },
  { title: '备注', dataIndex: 'remark', key: 'remark', ellipsis: true },
  { title: '操作', key: 'action', width: 240 },
];

async function loadDesigns() {
  designLoading.value = true;
  try {
    const res = await getCodegenDesigns({
      pageIndex: designPagination.current,
      pageSize: designPagination.pageSize,
      keyword: designQuery.keyword || undefined,
    });
    designs.value = res.items;
    designPagination.total = res.total;
  } finally {
    designLoading.value = false;
  }
}

function onDesignTableChange(p: any) {
  designPagination.current = p.current;
  designPagination.pageSize = p.pageSize;
  loadDesigns();
}

function parseFieldCount(fieldsJson?: string): number {
  if (!fieldsJson) return 0;
  try {
    return (JSON.parse(fieldsJson) as any[]).length;
  } catch {
    return 0;
  }
}

// ============ 表设计编辑 ============
const editVisible = ref(false);
const saving = ref(false);
const editForm = reactive<{ id?: string; tableName: string; displayName: string; remark?: string; fields: CodegenFieldDesign[] }>({
  tableName: '',
  displayName: '',
  fields: [],
});

let fieldKeySeq = 0;
function newField(): CodegenFieldDesign {
  return {
    name: '',
    label: '',
    dataType: 'string',
    length: 200,
    precision: undefined,
    nullable: true,
    isPrimary: false,
    isUnique: false,
    isIndex: false,
    showInList: true,
    control: 'text',
    queryType: 'none',
    _key: ++fieldKeySeq,
  } as any;
}

const fieldColumns = [
  { title: '字段名', key: 'name', width: 150 },
  { title: '显示名', key: 'label', width: 130 },
  { title: '类型', key: 'dataType', width: 120 },
  { title: '长度', key: 'length', width: 90 },
  { title: '小数位', key: 'precision', width: 80 },
  { title: '可空', key: 'nullable', width: 60 },
  { title: '列表显示', key: 'showInList', width: 80 },
  { title: '控件', key: 'control', width: 110 },
  { title: '字典编码', key: 'dictCode', width: 110 },
  { title: '查询', key: 'queryType', width: 100 },
  { title: '操作', key: 'fieldAction', width: 60 },
];

function addField() {
  editForm.fields.push(newField());
}

function removeField(index: number) {
  editForm.fields.splice(index, 1);
}

function openCreate() {
  editForm.id = undefined;
  editForm.tableName = '';
  editForm.displayName = '';
  editForm.remark = undefined;
  editForm.fields = [newField()];
  editVisible.value = true;
}

async function openEdit(record: CodegenDesign) {
  const detail = await import('@/api/codegen').then((m) => m.getCodegenDesign(record.id));
  editForm.id = detail.id;
  editForm.tableName = detail.tableName;
  editForm.displayName = detail.displayName;
  editForm.remark = detail.remark;
  fieldKeySeq = 0;
  editForm.fields = parseFieldCount(detail.fieldsJson) > 0 ? JSON.parse(detail.fieldsJson!) : [];
  editForm.fields.forEach((f: any) => (f._key = ++fieldKeySeq));
  editVisible.value = true;
}

async function onSaveDesign() {
  if (!editForm.tableName.trim()) {
    message.warning('请填写表名');
    return;
  }
  if (!editForm.displayName.trim()) {
    message.warning('请填写中文名');
    return;
  }
  if (!editForm.fields.length) {
    message.warning('请至少添加一个字段');
    return;
  }
  saving.value = true;
  try {
    await saveCodegenDesign({
      id: editForm.id,
      tableName: editForm.tableName.trim(),
      displayName: editForm.displayName.trim(),
      remark: editForm.remark,
      fields: editForm.fields.map(({ _key, ...f }: any) => f as CodegenFieldDesign),
    });
    message.success('保存成功');
    editVisible.value = false;
    await loadDesigns();
  } finally {
    saving.value = false;
  }
}

// ============ 同步建库 ============
const syncVisible = ref(false);
const syncResult = ref<CodegenSyncResult | null>(null);

async function onDeleteDesign(record: CodegenDesign) {
  await deleteCodegenDesign(record.id);
  message.success('删除成功');
  await loadDesigns();
}

async function onSync(record: CodegenDesign) {
  const res = await syncCodegenDesign(record.id);
  syncResult.value = res;
  syncVisible.value = true;
  await loadDesigns();
}

async function onExecuteDestructive(sql: string) {
  await executeDestructiveSql(sql);
  message.success('已执行');
  // 从待确认清单中移除已执行语句
  if (syncResult.value) {
    syncResult.value.destructivePending = syncResult.value.destructivePending.filter((s) => s !== sql);
  }
}

// ============ 一键生成 ============
const genVisible = ref(false);
const genResult = ref<CodegenGenerateResult | null>(null);

async function onGenerate(record: CodegenDesign) {
  const res = await generateCodegen(record.id);
  genResult.value = res;
  genVisible.value = true;
}

// ============ 生成记录 ============
const records = ref<CodegenRecord[]>([]);
const recordLoading = ref(false);
const recordPagination = reactive({ current: 1, pageSize: 10, total: 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 条` });

const recordColumns = [
  { title: '表名', dataIndex: 'tableName', key: 'tableName' },
  { title: '方式', key: 'mode', width: 90 },
  { title: '文件数', dataIndex: 'fileCount', key: 'fileCount', width: 90 },
  { title: '耗时', key: 'elapsedMs', width: 100 },
  { title: '时间', dataIndex: 'createdAt', key: 'createdAt', width: 180 },
  { title: '操作', key: 'action', width: 110 },
];

async function loadRecords() {
  recordLoading.value = true;
  try {
    const res = await getCodegenRecords({ pageIndex: recordPagination.current, pageSize: recordPagination.pageSize });
    records.value = res.items;
    recordPagination.total = res.total;
  } finally {
    recordLoading.value = false;
  }
}

function onRecordTableChange(p: any) {
  recordPagination.current = p.current;
  recordPagination.pageSize = p.pageSize;
  loadRecords();
}

const recordVisible = ref(false);
const recordFiles = ref<{ path: string; content: string }[]>([]);

async function openRecordDetail(record: CodegenRecord) {
  const detail = await getCodegenRecord(record.id);
  recordFiles.value = detail.files || [];
  recordVisible.value = true;
}

onMounted(() => {
  loadDesigns();
  loadRecords();
});
</script>

<style scoped>
.sync-list {
  margin: 0;
  padding-left: 20px;
}
.sql-line {
  display: flex;
  align-items: center;
  margin-bottom: 6px;
}
.sql-line code {
  flex: 1;
  background: #f5f5f5;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 12px;
  word-break: break-all;
}
.code-preview {
  max-height: 420px;
  overflow: auto;
  background: #f5f5f5;
  padding: 8px;
  border-radius: 4px;
  font-size: 12px;
  margin: 0;
}
</style>
