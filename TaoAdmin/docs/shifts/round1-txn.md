# 第一轮·⑩ 批量操作事务分级（round1-txn）

- 依据：老板 2026-11-22 21:07 令「批量操作按业务场景判断是否加事务，不搞一刀切」——强一致场景（如角色授权）用事务全成全败；可容错场景（如文件删除）逐条执行、完成后汇报失败明细。
- 分支：`round1-txn`（自 main 切出；工作区另有调度核心未提交的 `docs/shifts/` 未跟踪文件，未动未提交）。
- 验证：后端 dotnet build 零错零警（见证据段）；新增自测 `BatchTransactionTests` 3 例全绿（含强一致回滚实证）；按派工单不跑全量测试，批次末调度核心统一跑。

## 一、归类清单（接口 → 场景 → 事务策略 → 理由）

| # | 接口 | 场景 | 事务策略 | 理由 |
|---|---|---|---|---|
| 1 | POST /roles/batch-delete | 角色授权（强一致） | **整批一个事务，全成全败** | 角色软删级联清理 role-menu/role-api/user-role 三张授权关系表，部分失败会留下孤儿授权记录（角色已删、授权残留），权限口径不一致 |
| 2 | POST /users/batch-delete | 用户账号（强一致） | **整批一个事务，全成全败** | 用户软删连带清理 user-role 关联，部分失败会留下带角色关联的已删用户 |
| 3 | POST /apis/batch-delete | 接口授权（强一致） | **整批一个事务，全成全败** | 接口软删连带清理 role-api 授权关联，部分失败会留下指向已删接口的授权记录 |
| 4 | POST /users/batch-status | 用户启停（可容错） | 逐条执行 + 失败明细随响应 | 各用户状态互相独立，单条失败不影响他人 |
| 5 | POST /announcements/batch-status | 公告上下线（可容错） | 逐条执行 + 失败明细随响应 | 各公告状态互相独立 |
| 6 | POST /tenants/batch-delete | 租户软删（可容错） | 逐条执行 + 失败明细随响应 | 各租户软删互相独立 |
| 7 | POST /online-users/batch-kick | 会话下线（可容错，内存账本无 DB） | 逐条执行 + 失败明细随响应 | 逐会话 Revoke 天然独立，不存在/已下线的会话 ID 列入 failed |
| 8 | POST /announcements/batch-delete | 公告软删 | 单语句原子，无需显式事务 | 单条 UPDATE（SetColumns+Where）天然原子 |
| 9 | POST /audit/logs/batch-delete | 审计日志物理删除 | 单语句原子，无需显式事务 | 单条 DELETE（In）天然原子 |
| 10 | POST /messages/batch-delete、/messages/batch-read | 站内消息软删/已读 | 单语句原子，无需显式事务 | 单条 UPDATE（SetColumns+Where）天然原子 |
| 11 | DELETE /api/files/{id}（TaoFiles 文件删除，现无批量接口） | 文件删除（可容错） | 逐条执行 + 失败明细（策略预归类） | 文件软删与磁盘清理互相独立；⑧批量扩页落文件中心后端时按本口径实现，不另立口径 |

## 二、落码改动（逐文件）

| 文件 | 改动 |
|---|---|
| backend/src/TaoAdmin.Application/Services/RoleAppService.cs | 批量删除改 `_db.Ado.UseTranAsync` 整批事务，失败抛 DomainException 并注明已回滚 |
| backend/src/TaoAdmin.Application/Services/UserAppService.cs | 批量删除同上；批量启停改逐条 try/catch，返回 `(Skipped, Failed)` 失败明细 |
| backend/src/TaoAdmin.Application/Services/ApiAppService.cs | 批量删除改整批事务 |
| backend/src/TaoAdmin.Application/Services/AnnouncementAppService.cs | 批量上下线改逐条 try/catch，返回 `(Updated, Failed)` |
| backend/src/TaoAdmin.Application/Services/WorkflowAppService.cs | 租户批量删除改逐条 try/catch，返回 `(Deleted, Failed)` |
| backend/src/TaoAdmin.IdentityService/Controllers/AnnouncementController.cs | batch-status 响应增补 `failed` 字段（增量，不破坏既有字段） |
| backend/src/TaoAdmin.IdentityService/Controllers/ApiControllers.cs | 用户 batch-status 响应增补 `failed`；角色/接口 batch-delete 注释同步事务口径 |
| backend/src/TaoAdmin.IdentityService/Controllers/ExtensionControllers.cs | 租户 batch-delete 响应增补 `failed` |
| backend/src/TaoAdmin.IdentityService/Controllers/OnlineUserController.cs | batch-kick 响应增补 `failed`（未下线的会话 ID） |
| backend/tests/TaoAdmin.Tests/Application/BatchTransactionTests.cs | 新增自测 3 例（见下） |

响应字段均为增量补充（`failed` 数组），既有前端读取的 `deleted/updated/skipped` 字段不变，不碰前端列表页。

## 三、自测证据（强一致回滚实证）

新增 `BatchTransactionTests`（SQLite 真库，临时文件库随测随清）：

1. `RoleBatchDelete_AllValid_ShouldSoftDeleteAllAndCascadeLinks`——批量删 2 角色，三张授权关系表级联清空。
2. `UserBatchDelete_SecondItemFails_ShouldRollBackWholeBatch`——**回滚实证**：仓储桩在第 2 条软删注入故障（确定性，不依赖跨连接 DDL），第 1 条已落库的删除被整批撤销 → 断言 2 个用户均未删（IsDeleted=false 计数=2）、用户角色关联 2 条原样，异常消息含「回滚」。
3. `RoleBatchDelete_NonexistentIds_ShouldSkipQuietly`——不存在 ID 静默跳过，有效 ID 正常删除。

运行方式（定向，非全量）：`dotnet test --filter BatchTransactionTests` → Passed! 3/3；`dotnet build` → 0 Warning(s) 0 Error(s)。

定因留痕（四.7 失败即取证）：首轮自测 3 红两因——①`:memory:` 库下 `db.Ado.ExecuteCommand` 走独立连接（空库），DROP 注入不可靠，改仓储桩确定性注入；②软删后行仍在表内，断言口径由 Empty 改有效计数。另核得 SqlSugar `UseTranAsync` 异常口径随版本有吞/抛之异，服务侧统一 try/catch+IsSuccess 双兜住，回滚语义不受影响。

## 四、制度对照三行

1. **读了哪几册哪几条**：tao-result-first 总纲 §12 改动自查、§13 真读真看、§16 统一验证、§18 并发令；tao-code-standard 〇·六五问、一.10 断言先行、三.2 异常、三.7 防错写法、六.7 DBA 帽（事务显式）；tao-testing-standard 一（校验显式统计）、二（用例命名/边界）、四.7（失败即取证）；tao-subagent-dispatch §一13a 交活带制度对照、§七分身自律。
2. **哪一条实际改变了本班做法**：总纲 §18 并发令——编译后台起、等待段落档案；tao-code-standard 一.10 断言先行——先写回滚断言用例再定稿实现；tao-code-standard 六.7 DBA 帽「事务显式」——强一致三接口全部显式 UseTranAsync 并注释拍板依据。
3. **有无回灌事项**：无新坑。可容错场景逐条 catch 后失败明细随响应汇报，属三.2「边界可恢复」既有条款可推出，不另立新条。

## 五、遗留项

- 文件中心（TaoFiles）现无批量删除接口；⑧批量扩 10 页落文件中心后端时按本档第 11 条口径实现（逐条执行+失败明细），未在本班动 TaoFiles 代码。
- 可容错场景的 `failed` 明细为增量字段，前端列表页展示由 ⑧⑨ 路（round1-frontend）统一接。
- 全量测试按派工单留批次末调度核心统一跑。
