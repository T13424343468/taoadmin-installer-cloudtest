/**
 * 逐条执行批量操作（可容错场景统一口径：逐条执行、单条失败不中断、完成后汇报失败明细）。
 * 用于后端暂无批量接口、按业务语义允许部分成功的场景（如文件删除、任务批量操作）。
 *
 * @param ids 待处理的记录 ID
 * @param action 单条处理动作
 * @returns okCount 成功条数、failedCount 失败条数
 */
export async function runBatchOneByOne(
  ids: string[],
  action: (id: string) => Promise<unknown>
): Promise<{ okCount: number; failedCount: number }> {
  let okCount = 0;
  let failedCount = 0;
  for (const id of ids) {
    try {
      await action(id);
      okCount++;
    } catch {
      failedCount++;
    }
  }
  return { okCount, failedCount };
}
