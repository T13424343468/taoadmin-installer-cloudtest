# ⑲代码生成器模块 · 补账班（cover-codegen 分支）

## 任务
v23 交付时⑲代码生成器模块分支覆盖 69.2%（328/474）、行覆盖 91.2%，未达 100% 硬门禁被打回。本班补账：分支/行双 100%，全量测试全绿，只增不减。

## 覆盖账（coverlet / net10.0 / Release）
- **CodeGenAppService：行 100%，分支 100%**（含全部异步状态机类）
- **CodeGenController：行 100%，分支 100%**（含全部异步状态机类）
- **SysCodeGenTable / SysCodeGenRecord：行 100%，分支 100%**
- 出账口径：`dotnet test TaoAdmin.sln -c Release --collect:"XPlat Code Coverage"`，cobertura 原件入库 `docs/coverage/codegen-cover-codegen.cobertura.xml`
- 全量测试：**518/518 全绿**（原 502 测全保留 + 新增 16 测；排除并行 cover-file-dist 工作流的 FileAppServiceGapTests，其 1 失败与本班无关）

## 补账手段
1. **死分支重构消除**：`CodeGenAppService.SyncToDatabaseAsync` 加列三元表达式两分支字符串完全相同（Sqlite/非 Sqlite 的 addSql 同值），合并为单条 `ALTER TABLE` 语句。
2. **方言判断缝隙**：新增 `protected virtual bool IsSqliteDialect`，`TableExistsAsync`/`GetExistingColumnsAsync` 改用该属性；测试以子类覆写模拟 MySQL 方言，真实走 information_schema 探测分支（SQLite 挂载 `information_schema` schema），不再运行中突改 `CurrentConnectionConfig.DbType`（该写法会重建连接致 :memory: 库丢失）。
3. **实效断言用例（服务层 +10）**：字段名校验全分支（空白/正则/控件/查询方式/string 长度 null·0·4001/decimal 精度 null·-1·11）、全类型设计（索引落库 sqlite_master 断言、列类型与默认值 PRAGMA table_info 实测、实体特性、前端控件）、脏数据默认分支（未知类型/string 无长度/decimal 非空）、MySQL 方言 information_schema 路径、破坏性执行、分页关键字、更新不存在抛错、空字段抛错、记录不存在与空文件清单（null/空串/空白/JSON 字面量 null）、ParseFields 三分支。
4. **控制器测试（新增 5）**：全部 9 个入口走通（设计分页/保存/详情/删除/同步/破坏性执行带与不带 sql 键/生成 mode 缺省与 regenerate/记录分页/记录详情）。

## 顺带修复的真 bug
- `CodeGenController.SaveDesign`：`ApiOk(id.ToString())` 因重载解析命中「仅消息」重载，`Data` 恒为 null（id 被塞进 Message）。按代码库既有约定改为 `ApiOk<object>(id.ToString())`，Data 恢复返回新建设计 Id。前端保存后走列表刷新不受影响。

## 测试修正记录（补账过程中发现的原用例问题）
- 全类型测试误将 DDL 事实（bigint/boolean/DEFAULT）断言在实体文件上——实体是 C# 类型不含 DDL；改为 PRAGMA table_info 实测真实落库的类型与默认值。
- 索引断言块误用 `using` 释放 `db.Ado.Connection`——:memory: 库随连接存亡，释放后整库丢失（"no such table"）。
- 控制器破坏性执行测试删 `amount` 列但该测试夹具设计只有 `title` 字段——改为删实际存在的列。
- 脏数据断言 `public string Weird` 与实际生成 `public string? Weird`（nullable）不符——按实效修正。
