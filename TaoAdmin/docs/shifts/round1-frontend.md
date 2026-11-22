# round1-frontend 班次档案（2026-11-22 21:38 开工）

## 结论

两项全部落地，前端构建绿（vue-tsc + vite build，0 错 0 警）。

### ⑧ 批量操作 8 页 → 10 页

- **任务中心**（`frontend/src/views/job/index.vue`）：补齐勾选批量——跨页勾选、表头全选只管当前页、「选中全部 N 条」显式生效、批量前确认框写明条数、完成后清空刷新，与既有 8 页口径一致。批量动作：批量暂停 / 批量恢复 / 批量删除（权限沿用单条操作的 `job:pause` / `job:resume` / `job:delete`）。
- **文件中心**（`frontend/src/views/file/index.vue`）：原单条删除升级出批量——同款勾选交互 + 批量删除（确认框写明条数，逐条执行、完成后汇报成功/失败条数，对齐老板⑩「可容错场景逐条执行、汇报失败明细」口径）；换搜索条件自动清空勾选。
- **后端说明**：任务中心与文件中心后端现无批量接口（JobController / FileController 均为单条接口）。本班按前端路边界不动后端控制器（round1-txn 线在做⑩批量事务，避免同文件冲突），批量走统一封装 `runBatchOneByOne` 逐条调用既有单条接口，单条失败不中断、完成后汇报失败条数；后续⑩落批量接口后前端可平滑替换调用点。

### ⑨ 删除后翻页停在空页 · 系统性修复

- 新增统一封装 `frontend/src/utils/listPage.ts`：`pageAfterDelete(totalBefore, deleted, pageIndex, pageSize)`——删完当前页最后一条自动翻回上一页；批量删除多条时回退到最后一个仍有数据的页；全删清空回第 1 页。全系统唯一写法，不逐页打补丁。
- 接入页面（8 个有分页且有删除路径的列表页）：用户、角色、接口、公告、站内消息、审计日志（批量删除 + 清理过期日志）、任务中心、文件中心。租户页与在线用户页为全量列表无分页，不涉及。
- 游标翻页页（审计日志、文件中心）：删除后清空游标缓存再按计算页码加载，防陈旧游标错位。

## 证据

- 构建：`npm run build`（vue-tsc --noEmit && vite build）EXIT=0，日志 0 error 0 warning（/tmp/fe-build.log）。
- 逐文件 diff 自查（总纲§12）：10 个改动文件逐一对照意图核对，无越界改动、无残留死代码（文件中心原临时翻页判断已删净）。
- 全量测试按批次末调度核心统一跑（总纲§16），本班未单独起测。

## 路径（本活文件清单）

新增：
- `frontend/src/utils/listPage.ts`（⑨统一翻页封装）
- `frontend/src/utils/batchRun.ts`（逐条批量执行封装）

修改：
- `frontend/src/views/job/index.vue`（⑧批量 + ⑨翻页）
- `frontend/src/views/file/index.vue`（⑧批量 + ⑨翻页）
- `frontend/src/views/system/user/index.vue`（⑨）
- `frontend/src/views/system/role/index.vue`（⑨）
- `frontend/src/views/system/api/index.vue`（⑨）
- `frontend/src/views/system/announcement/index.vue`（⑨）
- `frontend/src/views/system/message/index.vue`（⑨）
- `frontend/src/views/system/auditlog/index.vue`（⑨）

## git

- 分支 `round1-frontend`（开工时仓库干净，按纪律建分支；只 add 本活文件提交）。工作区中途出现 round1-txn 线的后端未提交改动，未触碰。

## 遗留项

- 任务中心/文件中心批量现为前端逐条调用；待 round1-txn（⑩）落后端批量接口后，可将 `runBatchOneByOne` 调用点替换为真批量接口（交互口径不变）。
- 文件中心「选中全部」受后端单页 100 条上限约束，按总数循环取齐；超大库（亿级）下该显式操作耗时随总量增长，与既有 8 页「一次取 10000」口径相比更保守，如需优化待后端出批量查询接口。

## 制度对照三行

1. 读了：tao-result-first 全文（重点§12 改动自查、§13 真读真看、§16 统一验证、§18 并发令）；tao-code-standard 全文（重点〇·七.3 归一、一.1 零警告、一.3 死代码必删）；tao-testing-standard 一~四章（本活为前端构建级自查，全测归批次末）；tao-subagent-dispatch §四§七（交接靠档案、分身自律）。
2. 实际改变做法：§16/§18——构建验证后台起、起完即转 diff 自查，不空等；〇·七.3 归一——⑨不做逐页补丁，落 `pageAfterDelete` 单点封装全页复用；一.3——文件中心原临时翻页判断（`files.value.length === 1` 分支）同轮删净。
3. 回灌事项：无新坑。既有「后端无批量接口时前端逐条执行」口径已落 `runBatchOneByOne` 封装注释，供⑩落接口后对照替换。
