# TaoAdmin — .NET 8 微服务权限框架

.NET 8 + SqlSugar + MySQL + Ant Design Vue 的微服务后台基础框架，可直接作为底座开发不同业务项目。

## 技术栈

| 层 | 选型 |
|---|---|
| 后端 | .NET 8 / SqlSugarCore / YARP 网关 / Quartz.NET / JWT |
| 前端 | Vite + Vue 3 + TypeScript + Ant Design Vue 4 + Pinia |
| 中间件 | MySQL 8 / Redis 7 / Nacos v2（注册+配置）/ Elasticsearch + Kibana（分布式日志） |
| ID | 雪花ID（通用库内自实现，datacenterId + workerId 可配） |
| 部署 | Docker Compose / Jenkins / GitHub Actions / K8s |

## 目录结构

```
TaoAdmin/
├── backend/                        后端解决方案
│   ├── src/TaoAdmin.Common       通用库：雪花ID、Redis、SqlSugar、JWT、权限码授权、统一返回
│   ├── src/TaoAdmin.IdentityService  权限服务(5001)：用户/角色/菜单/接口权限，CodeFirst+种子数据
│   ├── src/TaoAdmin.TaskService  定时任务服务(5002)：Quartz 调度、任务 CRUD、暂停/恢复/执行
│   ├── src/TaoAdmin.GatewayService   YARP 网关(5000)：统一鉴权、限流、跨域、TraceId 贯穿
│   └── tests/TaoAdmin.Tests      xUnit 单元测试（雪花ID唯一性等）
├── frontend/                       管理界面
│   └── src/views/  登录、工作台、用户/角色/菜单/接口管理、服务管理中心、日志管理中心、定时任务
└── deploy/                         编排与自动化
    ├── docker-compose.yml          一键起 MySQL/Redis/RabbitMQ/Nacos/ES/Kibana/网关/服务/前端
    ├── sql/tao_admin_init.sql    数据库一键初始化脚本（建库建表+种子数据，含主从复制模板）
    ├── jenkins/Jenkinsfile         构建→测试→镜像→推送→滚动部署
    ├── github/workflows/ci.yml     GitHub Actions（使用时移到仓库根目录 .github/）
    └── k8s/                        K8s 清单（ConfigMap/Secret/Deployment/Ingress）
```

## 权限模型

- **菜单权限（显示/隐藏）**：`SysMenu` 支持目录/菜单/按钮三种类型，`Visible` 字段控制侧边栏显示隐藏；前端登录后按菜单树动态生成路由。
- **按钮权限（禁用/启用）**：按钮挂 `PermsCode`（如 `sys:user:add`），前端 `v-permission` 指令无权限时隐藏（`.disable` 修饰符切换为禁用）。
- **接口权限**：`SysApi` 登记接口资源（路径+方法+权限码），角色绑定后由 `PermissionAuthorizationHandler` 在服务端校验，双重防线。
- 实体统一保留 `Sys` 前缀（SysUser/SysRole/SysMenu/SysApi/SysJob/SysTenant/SysMessage/SysWorkflow*），为业务项目预留干净命名（如前台 User）。

## 内置业务能力（接口已就绪，前端页面规划中）

- **多租户**：租户管理接口 + 数据自动隔离（实现 `ITenantEntity` 即自动过滤）；`TenantId=0` 为平台公共数据全租户可见；JWT 携带 `tenant_id`，支持 `X-Tenant-Id` 头覆盖；总开关 `MultiTenant:Enabled` 可关闭。
- **工作流**：线性审批流引擎——JSON 节点模板、按角色逐级审批、驳回终止、自动流转完结；内置请假（两级）/报销（一级）模板与全状态模拟数据。
- **消息队列**：`IMessageQueue` 抽象，内存实现（默认零依赖）与 RabbitMQ（`RabbitMq:Enabled=true`）一键切换；后台订阅泵自动消费 `IMessageHandler`；示例：新用户自动收欢迎消息。
- **接口幂等**：`[Idempotent]` 接口凭 `Idempotency-Key` 请求头防重，重放返回原响应（`X-Idempotent-Replayed: true`）。
- **网关限流**：固定窗口默认 100 次/10 秒，登录用户按 ID、未登录按 IP 分桶，超限 429。
- **网关熔断**：按服务独立统计，连续 5 次失败熔断（503 快速失败），冷却 30 秒自动恢复。
- **多租户演示账号**：east01 / east_leader / south01 / south_manager，密码均为 `Tenant@123`（详见《04-操作手册》）。

## 分布式要点

- **日志**：各服务 Serilog 直接写 Elasticsearch（索引 taoadmin-{服务名}-yyyy.MM.dd），网关透传 `X-Trace-Id` 全链路追踪；Kibana 中创建 `taoadmin-*` 索引模式即可查询。日志管理中心页面走 `/api/monitor/logs`。
- **配置**：Nacos 配置中心，DataId 约定 `taoadmin-{服务名}.yaml`，见 `deploy/nacos/README.md`；服务注册发现同用 Nacos。
- **读写分离**：SqlSugar 主从路由，写入/事务走主库、查询自动分流从库；从库连接串配置 + 总开关启用，写后立即读用 `ForceMaster` 固定主库；一键建库脚本 `deploy/sql/tao_admin_init.sql`（含主从复制搭建模板）。
- **消息队列**：单实例默认内存实现；多实例/生产环境切 RabbitMQ（compose 已内置服务），详见《05-配置实施运维教程》第 9 节。

## 快速启动

```bash
# 1. 起中间件与应用（首次自动建库，权限服务启动时 CodeFirst 建表并种子 admin 账号）
cd deploy && docker compose up -d

# 2. 访问
# 前端：http://localhost:8080   网关：http://localhost:5000
# Nacos：http://localhost:8848   Kibana：http://localhost:5601
# 默认账号：admin / Admin@123

# 本地开发（不走容器）
dotnet run --project backend/src/TaoAdmin.IdentityService   # 5001
dotnet run --project backend/src/TaoAdmin.TaskService       # 5002
dotnet run --project backend/src/TaoAdmin.GatewayService    # 5000
cd frontend && npm i && npm run dev                           # 代理 /api -> 5000
```

## 自动化部署

- Jenkins：`deploy/jenkins/Jenkinsfile`，参数化镜像标签，按服务并行构建镜像，SSH 滚动更新（凭据：docker-registry / deploy-host）。
- GitHub Actions：`deploy/github/workflows/ci.yml`，push main 自动测试+构建+推镜像（使用时把该目录移到仓库根 `.github/`）。
- K8s：`deploy/k8s/` 按清单顺序 apply，Secret 需先替换真实值。

## 说明

- 代码为完整脚手架文本，本地首次使用请 `dotnet build` 与 `npm install` 验证；Jenkinsfile 中的镜像仓库地址、域名等按环境替换。
- 命名约定：系统框架实体带 `Sys` 前缀；控制器、DTO 等用简短语义化英文命名。

## 发布信息

- **版本**：1.0.0（纳入 Tao 系列的首个正式版本）
- **产品**：TaoAdmin ｜ Company/Authors：Tao ｜ Copyright (c) 2026 Tao
- **运行环境**：.NET 8（后端）/ Node 18+（前端构建）/ MySQL 8 或 MariaDB / Redis 7
- **许可**：归委托方所有，未授权不得对外分发
