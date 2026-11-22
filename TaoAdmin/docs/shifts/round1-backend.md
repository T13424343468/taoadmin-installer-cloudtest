# round1-backend 班次档案（③文件回收站 + ④同账号互踢）

- 依据：老板 2026-11-22 21:38 开工令第一轮范围（⑧⑨⑩⑪③④）；③为 2026-11-18 拍板阶段二安全闸项（11-22 对账确认未实施），④为 11-22 对账项。
- 分支：`round1-backend`（自 main cbaea99 切出，独立工作树 /root/workspace/installer-fix/TaoAdmin-be）；HEAD `3601d3e`，两笔隔离提交：9e701a9（③）、3601d3e（④）。
- 断言清单先于实现落盘（编码规范一.10），见各任务断言段；自测全部通过后本档案才算成立。
- 终验证据（2026-11-22 23:0x）：TaoAdmin 后端 build 0 警 0 错 + dotnet test **275/275**（基线 272 + 新增 3）；TaoFiles build 0 警 0 错 + dotnet test **12/12**（既有 7 + 新增 5）；前端 vue-tsc + vite build 绿（dist 产物含回收站代码，file-*.js/taoFiles-*.js 核验）。

## 任务一：③ 文件回收站（TaoFiles + 文件中心页）

### 断言清单（先于实现）
1. 删除后记录 IsDeleted=true 且 DeletedAt 非空，磁盘文件仍可读（软删阶段不物理删除）。
2. 回收站分页只列已删记录，按删除时间倒序，含 deletedAt。
3. 恢复后 IsDeleted=false、DeletedAt=null，回到正常列表、离开回收站，磁盘文件从未删除可直接用。
4. 彻底删除后记录物理消失、磁盘文件被清理、回收站列表减少。
5. 未删除/不存在的记录做恢复或彻底删除 → 404。
6. 软删后同内容重传：去重不命中回收站记录，落新记录新物理文件，互不影响。
7. 开关默认关闭时多端登录互不影响（④合并见任务二断言1）。

### 落码改动（逐文件）
| 文件 | 改动 |
|---|---|
| TaoFiles/src/TaoFiles.Api/Entities/SysFile.cs | 新增 `DeletedAt DateTime?`（可空列，CodeFirst 启动自动补列，存量库无需手工迁移） |
| TaoFiles/src/TaoFiles.Api/Controllers/FileAppService.cs | DeleteAsync 改纯软删进回收站（移除 physicalDelete 参数——删除不再直接清磁盘，该参数已无意义，按一.3 删净）；新增 RecycleBinPageAsync / RestoreAsync / PurgeAsync；FileDto 增 deletedAt；列表映射归一复用 ToDto |
| TaoFiles/src/TaoFiles.Api/Controllers/FileController.cs | DELETE /api/files/{id} 语义改为移入回收站；新增 GET /api/files/recycle-bin、POST /api/files/{id}/restore、DELETE /api/files/{id}/purge |
| TaoAdmin/frontend/src/api/taoFiles.ts | deleteFile 去 physicalDelete 参数（前端从未传 true，无调用面破坏）；新增 listRecycleBin / restoreFile / purgeFile；FileItem 增 deletedAt |
| TaoAdmin/frontend/src/views/file/index.vue | 工具条加「文件列表/回收站」视图切换；回收站表格（文件名+原上传时间、删除时间、恢复、彻底删除）；彻底删除 Modal.confirm 二次确认；删除成功提示改为「已移入回收站」 |
| TaoFiles/tests/TaoFiles.Tests/Application/RecycleBinTests.cs | 新增 5 例（SQLite 真库+临时目录真实存储），对应断言 1~6 |

### 自测证据
- `dotnet test --filter RecycleBinTests` → 5/5 Passed（见交差回报）。
- 磁盘文件保留/清理均以 LocalFileStorageProvider 真实读写断言，非 mock。

### 范围说明
- TaoAdmin 自带文件服务（gateway /api/files，IdentityService/FileController + Application/FileAppService，头像等内部用）删除口径未动：该服务无列表 UI 入口，前端唯一文件中心页走 TaoFiles(5003)；如需同口径改造属后续增量，不在本班范围。

## 任务二：④ 同账号互踢（TaoAdmin IdentityService）

### 断言清单（先于实现）
1. 开关默认关闭：登录安全设置空表读出 KickOnDuplicateLogin=false，多端并存现状不变。
2. 开关开启保存后读取立即生效（缓存清除），可逆关闭。
3. 重复保存不产生重复配置行。
4. 开启时新登录吊销同用户全部旧会话（OnlineSessionStore.RevokeByUser 语义，既有测试覆盖：只踢目标用户、吊销即 401）。

### 落码改动（逐文件）
| 文件 | 改动 |
|---|---|
| backend/src/TaoAdmin.Application/Services/LoginSecurityAppService.cs | LoginSecuritySettings 增 KickOnDuplicateLogin（默认 false）；键 kickOnDuplicateLogin 入 sys_site_config；Get/Save 接线，保存即清缓存立即生效 |
| backend/src/TaoAdmin.Application/Services/AuthAppService.cs | LoginAsync 登录成功后、签发新令牌前：开关开启则 `_sessions.RevokeByUser(user.Id)`，旧端下次请求经 SessionRevocationMiddleware 命中吊销即 401 |
| frontend/src/api/audit.ts | LoginSecuritySettings 接口增 kickOnDuplicateLogin |
| frontend/src/views/system/loginsecurity/index.vue | 登录安全页增「同账号互踢」开关（默认关，附影响说明文案） |
| backend/tests/TaoAdmin.Tests/Application/LoginKickSettingTests.cs | 新增 3 例（默认关/开启立即生效可逆/不重复落行） |

### 自测证据
- `dotnet test --filter LoginKickSettingTests` → 3/3 Passed（见交差回报）。
- 会话吊销→401 链路：OnlineSessionStore.RevokeByUser + SessionRevocationMiddleware 均为既有已测件，本班只做登录链路一行接线；接线本身由批次末调度核心统一 E2E 复核（登录→再登录→旧 token 请求 401）。

## 制度对照三行

1. **读了哪几册哪几条**：tao-result-first 全文（重点 §12 改动自查、§13 真读真看、§16 统一验证、§18 并发令、§19 产出自检）；tao-code-standard 全文（重点 〇·六 五问、〇·七.3 归一、一.3 死代码必删、一.5 常量集中、一.10 断言先行、三.2 异常、三.7 防错写法、六.7 DBA 帽、六.9 安全帽）。
2. **哪一条实际改变了本班做法**：一.10 断言先行——两任务均先落断言清单再写实现，测试与实现同批交付；〇·七.3 归一——FileDto 映射两现合并为 ToDto；一.3——physicalDelete 参数连同语义删净（服务层+控制器+前端三层同步），不留半死参数；六.7 DBA 帽——DeletedAt 用可空列 + CodeFirst 自动补列，存量库零手工迁移。
3. **有无回灌事项**：无新坑。TaoFiles FileAppService 测试构造需 IConfiguration——`new ConfigurationBuilder().Build()` 空配置即满足（AllowedExt/MaxSize 走默认值分支），属既有条款可推出，不另立新条。

## 遗留项

- views/file/index.vue 本班改动（回收站视图）与 round1-frontend（⑧⑨批量+翻页）落在同一文件，批次末合并时 onDelete 函数体与工具条区域需人工对齐，其余改动互不相交。
- 互踢接线（登录→再登录→旧 token 401）的端到端验证留批次末统一 E2E；单测层已覆盖设置持久化与会话吊销语义两段。
- TaoAdmin 内部文件服务（gateway /api/files）删除口径未改（无 UI 入口），如需同口径回收站待老板示下。
- 全量测试按派工单留批次末调度核心统一跑。
