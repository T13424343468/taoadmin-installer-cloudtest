/**
 * 列表页删除后翻页统一口径（全系统唯一写法，各列表页删除/批量删除后一律经此计算停留页码）：
 * 删完当前页最后一条自动翻回上一页，不停留在空页；批量删除多条时回退到最后一个仍有数据的页。
 *
 * @param totalBefore 删除前的总条数（本次查询拿到的 total）
 * @param deleted 本次实际删除条数
 * @param pageIndex 当前页码
 * @param pageSize 每页条数
 * @returns 删除后应加载的页码
 */
export function pageAfterDelete(
  totalBefore: number,
  deleted: number,
  pageIndex: number,
  pageSize: number
): number {
  const remaining = Math.max(totalBefore - deleted, 0);
  if (remaining <= 0) return 1;
  return Math.min(pageIndex, Math.ceil(remaining / pageSize));
}
