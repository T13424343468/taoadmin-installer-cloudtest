<template>
  <a-cascader
    :value="innerValue"
    :options="options"
    :load-data="loadData"
    :placeholder="placeholder"
    :disabled="disabled"
    change-on-select
    allow-clear
    show-search
    @change="handleChange"
    @search="handleSearch"
  />
</template>

<script setup lang="ts">
/**
 * 省市区街道四级联动选择组件（老板 2026-11-22 令⑪地址库）。
 * 可复用：v-model:value 绑定区划代码数组 [省code, 市code, 县code, 街道code]；
 * 支持只选到区县（街道可选，change-on-select 允许任意层级收尾）。
 * 数据源：行政区划只读接口，懒加载子级 + 远程关键词搜索。
 */
import { ref, watch } from 'vue';
import { getRegionChildren, getRegionByCode, searchRegions, type RegionItem } from '@/api/region';

const props = withDefaults(defineProps<{
  value?: string[];
  placeholder?: string;
  disabled?: boolean;
}>(), {
  value: () => [],
  placeholder: '请选择省 / 市 / 区县（街道可选）',
  disabled: false,
});

const emit = defineEmits<{ (e: 'update:value', val: string[]): void }>();

interface Option { value: string; label: string; level: number; isLeaf?: boolean; children?: Option[] }

const options = ref<Option[]>([]);
const innerValue = ref<string[]>(props.value ?? []);

watch(
  () => props.value,
  (v) => { innerValue.value = v ?? []; },
  { deep: true }
);

function toOption(r: RegionItem): Option {
  return { value: r.code, label: r.name, level: r.level, isLeaf: r.level >= 4 };
}

/** 首次展开加载省级；子级按需懒加载 */
async function loadOptions(): Promise<Option[]> {
  if (options.value.length > 0) return options.value;
  const provinces = await getRegionChildren('');
  options.value = provinces.map(toOption);
  return options.value;
}
loadOptions();

async function loadData(selected: Option[]): Promise<void> {
  const target = selected[selected.length - 1];
  if (!target || target.children) return;
  const children = await getRegionChildren(target.value);
  target.children = children.map(toOption);
}

/** 远程搜索：命中条目反推路径并挂进选项树，保证级联可选中 */
async function handleSearch(keyword: string): Promise<void> {
  if (!keyword || keyword.trim().length === 0) return;
  const hits = await searchRegions(keyword.trim());
  for (const hit of hits) {
    await ensurePath(hit);
  }
}

/** 按搜索命中条目的全路径逐级展开选项树（省 → 市 → 县） */
async function ensurePath(hit: RegionItem): Promise<void> {
  await loadOptions();
  const codes = hit.fullPath ? await resolvePathCodes(hit) : [hit.code];
  let level = options.value;
  let parent: Option | undefined;
  for (let i = 0; i < codes.length; i++) {
    const code = codes[i];
    let node = level.find((o) => o.value === code);
    if (!node) {
      // 路径节点尚未加载，逐级拉取
      const children = await getRegionChildren(parent?.value ?? '');
      const mapped = children.map(toOption);
      if (parent) parent.children = mapped;
      else options.value = mapped;
      level = mapped;
      node = level.find((o) => o.value === code);
      if (!node) return;
    }
    if (i < codes.length - 1 && !node.children) {
      const children = await getRegionChildren(node.value);
      node.children = children.map(toOption);
    }
    parent = node;
    if (node.children) level = node.children;
  }
}

/** 从全路径（省 / 市 / 县）解析出各级代码链 */
async function resolvePathCodes(hit: RegionItem): Promise<string[]> {
  const codes: string[] = [];
  // 县级反查：详情接口带 parentCode，逐级向上
  let current: RegionItem | null = hit;
  while (current) {
    codes.unshift(current.code);
    if (!current.parentCode) break;
    current = await getRegionByCode(current.parentCode);
  }
  return codes;
}

function handleChange(vals: unknown): void {
  const arr = Array.isArray(vals) ? (vals as string[]) : [];
  innerValue.value = arr;
  emit('update:value', arr);
}
</script>
