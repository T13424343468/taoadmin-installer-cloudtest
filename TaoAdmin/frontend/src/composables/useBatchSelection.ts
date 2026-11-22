import { ref, computed } from 'vue';

/**
 * 列表批量选择（跨页保留勾选）：
 * - 勾选跨页保留：翻页回来还亮着，批量按钮实时显示「已选 N 项」
 * - 表头全选只管当前页；「选中全部 N 条」按钮按当前筛选条件全选（显式点击才生效）
 * - 换筛选条件、重新查询自动清空已选
 *
 * @param fetchAllIds 按当前筛选条件取全部记录 ID（供「选中全部」使用）
 */
export function useBatchSelection(fetchAllIds: () => Promise<string[]>) {
  const selectedRowKeys = ref<string[]>([]);
  const allSelected = ref(false);
  const total = ref(0);

  /** 已选条数：按条件全选时显示总条数 */
  const selectedCount = computed(() => (allSelected.value ? total.value : selectedRowKeys.value.length));

  /** a-table row-selection onChange：手动勾选/取消即退出「选中全部」态 */
  function onTableSelectChange(keys: (string | number)[]) {
    allSelected.value = false;
    selectedRowKeys.value = keys.map(String);
  }

  /** loadData 拿到总数后同步，供「选中全部 N 条」与已选计数显示 */
  function setTotal(t: number) {
    total.value = t;
  }

  /** 「选中全部 N 条」：按当前筛选条件取全部 ID，显式点击才生效 */
  async function selectAll() {
    allSelected.value = true;
    selectedRowKeys.value = await fetchAllIds();
  }

  /** 清空勾选（换筛选、重新查询、批量操作完成后调用） */
  function clearSelection() {
    allSelected.value = false;
    selectedRowKeys.value = [];
  }

  /** 批量操作实际生效的 ID 列表 */
  function getSelectedIds(): string[] {
    return selectedRowKeys.value;
  }

  return {
    selectedRowKeys,
    allSelected,
    selectedCount,
    onTableSelectChange,
    setTotal,
    selectAll,
    clearSelection,
    getSelectedIds,
  };
}
