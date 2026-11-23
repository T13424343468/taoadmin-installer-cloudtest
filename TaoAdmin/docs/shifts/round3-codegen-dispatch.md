# ⑲代码生成器班 派工与进度账（2026-11-23 12:5x 起，死线 17:00）

## 派工口径（老板拍板，不得改动）
- 老板死线令（2026-11-23 12:49）：17:00 前必须交付；测试分段上云不等批次末；沙箱抓紧编码。
- 可视化表设计器：画表（字段/类型/长度/索引/控件/查询/校验）→ 同步建库（加字段自动同步；删字段/改类型等破坏性变更生成语句经确认后执行）→ 一键生成全套增删改查前后端 → 自动编译测试重启生效。
- 三个独立入口：单表设计器 / 主从表设计器 / 增强配置；不分期一次性交付，按优先级分段提交。
- 主从表：一层主从、整单事务、可挂已有表、从表可选独立全套功能、数据来源可选字典。
- 两项系统能力：一键生成新服务（脚手架+端口自动分配+网关路由/部署脚本/compose 登记）；生成器可选目标服务。
- 生成代码与手写隔离：生成文件带统一标记头，重复生成只覆盖生成区，手改逻辑写扩展文件（部分类）不被冲掉。
- 验收口径：标准功能接近全生成，个性逻辑走扩展区，合计覆盖 99%。

## 分段计划（死线内按此顺序，每段完成即 commit）
1. 【第一段】单表设计器全链路（后端核心）：
   - 元数据模型：TableDesign（表名/中文名/字段清单：名称/类型/长度/可空/默认/主键/索引/唯一/控件/查询方式/校验规则）
   - 建表同步器：按设计元数据生成 SqlSugar 实体（带 SugarTable/SugarColumn 标注）+ CodeFirst 建表；破坏性变更出 SQL 语句待确认
   - 代码生成引擎：实体/应用服务（分页+增删改查+软删）/控制器（路由+权限码）/前端 api.ts/列表页/表单弹窗，全部带生成标记头
   - 菜单权限种子：生成记录落库（SysCodeGenTable/SysCodeGenColumn 元数据表），菜单挂「系统管理」下
   - 设计器前端页：views/system/codegen（画表表单+字段表格+同步建库+生成按钮+生成记录列表）
2. 【第二段】主从表设计器（一层主从、整单事务、从表独立功能开关、字典数据来源）
3. 【第三段】增强配置（字典关联/查询配置/导入导出补强）+ 一键生成新服务 + 目标服务可选

## 环境备忘
- 工作区：/root/workspace/installer-fix/wt-round3-codegen2（分支 round3-codegen2，自 main 8648a55 切出）
- 后端：TaoAdmin/backend/TaoAdmin.sln（src: Application/Common/Domain/Infrastructure/IdentityService/TaskService/GatewayService/Utilities；tests: TaoAdmin.Tests）
- 前端：TaoAdmin/frontend（Vue3+antd，动态路由由后端菜单树生成，页面放 views/system/codegen）
- 实体基类 BaseEntity（Id/CreatedAt/UpdatedAt/IsDeleted）；控制器基类 BaseApiController（ApiOk/ApiFail）；[Permission("code")] 权限标注；PageResult<T> 分页
- 建表：SqlSugar CodeFirst.InitTables；菜单种子模式见 DbInitializer.Seed*Menu（幂等，按 Component 判重）
- 测试夹具：SQLite 内存库真库测试（见 AnnouncementAppServiceTests）
- export DOTNET_ROOT=/root/workspace/.local/dotnet10 && export PATH=$DOTNET_ROOT:$PATH（10.0.404）
- 基线构建已验证：0 警告 0 错误

## 进度
- [x] 12:49 建工作区、基线构建绿
- [ ] 第一段：单表设计器全链路
- [ ] 第二段：主从表
- [ ] 第三段：增强配置+新服务+目标服务
