# TaoAdmin 配置实施运维教程

> 面向实施与运维人员：从零环境到上线，以及日常运维操作。
> 各配置项的详细注释直接看对应配置文件（含本地/线上示例），本文讲流程与要点。
> 适用版本：2026-11-18 主干 ｜ 更新日期：2026-11-19

---

## 1. 环境要求

| 组件 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 10.0 | 后端编译运行 |
| Node.js | 18+ | 前端构建 |
| MySQL | 8.0 | 业务数据库 |
| Redis | 7.0 | 缓存/心跳/刷新令牌 |
| MinIO（可选） | 最新 RELEASE | 内网对象存储（文件中心切 minio Provider 时） |
| Docker | 24+（可选） | 容器化部署 |
| Nacos / ES | v2.2 / 8.x（可选） | 注册配置中心 / 日志存储 |

## 2. 本地开发环境搭建

```bash
# 1) 准备数据库与缓存（本地直装或 docker 起最小化中间件）
docker run -d --name taoadmin-mysql -e MYSQL_ROOT_PASSWORD=Micro@2026 \
  -p 3306:3306 mysql:8.0
docker run -d --name taoadmin-redis -p 6379:6379 redis:7-alpine

# 2) 启动后端三个服务（首次启动自动建库建表 + 种子数据）
cd backend
dotnet run --project src/TaoAdmin.IdentityService   # 5001
dotnet run --project src/TaoAdmin.TaskService       # 5002
dotnet run --project src/TaoAdmin.GatewayService    # 5000

# 3) 启动前端
cd frontend
npm install
npm run dev        # http://localhost:5173，/api 自动代理到 5000
```

验证：浏览器打开 http://localhost:5173 ，用 `admin / Admin@123` 登录。

### 2.1 一键环境（沙箱/演示环境）

环境被回收后一条命令恢复全套（MariaDB + Redis + 三服务 + 心跳 + 就绪等待）：

```bash
bash /root/workspace/taoadmin-env/provision.sh
# 输出 PROVISION-DONE health=0 即三服务全绿（0=成功）
```

脚本拉起的三个服务（发布产物方式运行）：

| 服务 | 端口 | 健康检查 |
|---|---|---|
| 网关 GatewayService | 5000 | curl http://localhost:5000/health |
| 权限服务 IdentityService | 5001 | curl http://localhost:5001/health |
| 任务服务 TaskService | 5002 | curl http://localhost:5002/health |

> 注意：TaskService 必须拉起，缺失会让前端首页轮询熔断弹错。首次启动 IdentityService
> 要做 CodeFirst 建表播种，脚本已给足 60 秒就绪等待。

## 3. 配置说明（本地 vs 线上）

> 每个配置项的逐项注释见各 `appsettings.json`（内含本地/线上示例）。
> 配置优先级：环境变量 > appsettings.Production.json > Nacos > appsettings.json。

### 3.1 必改项清单（上线前逐项核对）

| 配置项 | 文件 | 本地默认 | 线上要求 |
|---|---|---|---|
| MySQL 连接串 | 各服务 appsettings.json | root/Micro@2026 | 专用账号 + 强密码 + 内网地址 |
| MySQL 从库 | 各服务 `ConnectionStrings:Slaves` | 空（不分库） | 1~N 个从库连接串（见第 8 节） |
| 读写分离开关 | 各服务 `ReadWriteSplitting:Enabled` | false | 有从库后改 true |
| Redis 连接串 | 同上 | localhost:6379 | 加密码，内网地址 |
| RabbitMQ | IdentityService `RabbitMq:Enabled` | false（内存队列） | 多实例/生产改 true（见第 9 节） |
| 限流阈值 | GatewayService `RateLimiting` | 100 次/10 秒 | 按业务流量调整 |
| 熔断阈值 | GatewayService `CircuitBreaker` | 连续 5 次失败，冷却 30 秒 | 按服务敏感度调整 |
| 多租户开关 | IdentityService `MultiTenant:Enabled` | true | 单租户项目可改 false |
| 文件存储 | IdentityService `FileStorage:Provider` | local（本地盘） | 对接内网 MinIO 改 minio（见第 15 节） |
| Jwt:SecretKey | 三个服务一致 | 演示密钥 | 换 64 位随机串：`openssl rand -base64 64` |
| Elasticsearch:Uri | 三个服务 | 空（不写 ES） | `http://elasticsearch:9200`（需要集中日志时） |
| Nacos:Enabled | 三个服务 | false | 用注册/配置中心时改 true |
| 网关路由目标 | GatewayService | localhost:5001/5002 | 容器服务名或 K8s Service DNS |
| Snowflake 机器号 | 各服务 | 1/1、1/2 | 多实例时保证全局唯一（环境变量注入） |

### 3.2 配置覆盖示例

```bash
# 方式一：环境变量（容器/K8s 推荐，无需改文件）
export ConnectionStrings__MySql="server=mysql;database=tao_admin;uid=app;pwd=xxx;"
export Jwt__SecretKey="生产环境随机长密钥"
export Snowflake__WorkerId=3

# 方式二：appsettings.Production.json（只写差异项，随发布包分发）
{
  "ConnectionStrings": {
    "MySql": "server=mysql;port=3306;database=tao_admin;uid=taoadmin;pwd=***;charset=utf8mb4;",
    "Redis": "redis:6379,password=***,abortConnect=false"
  },
  "Jwt": { "SecretKey": "生产环境随机长密钥" },
  "Elasticsearch": { "Uri": "http://elasticsearch:9200" }
}

# 方式三：Nacos 配置中心（启用后）
# DataId=taoadmin-identity.yaml / taoadmin-task.yaml / taoadmin-gateway.yaml
# 发布配置后服务热更新，无需重启（支持灰度：先发一个实例观察）
```

## 4. Docker Compose 部署（单机/小规模）

```bash
cd deploy
# 按需修改 .env（数据库密码等）
docker compose up -d

# 查看启动状态
docker compose ps
docker compose logs -f gateway
```

启动顺序：MySQL/Redis/Nacos/ES（基础设施）→ 权限/任务服务 → 网关 → 前端 Nginx。
服务健康检查通过后自动进入运行状态；前端访问 `http://服务器IP`（80 端口）。

**数据持久化**：MySQL/Redis 使用 named volume（mysql-data / redis-data），升级容器不丢数据。

## 5. K8s 部署（生产推荐）

```bash
# 1) 先替换 k8s/ 下的 Secret（数据库密码、JWT 密钥）
vim deploy/k8s/*-secret.yaml

# 2) 按顺序 apply
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/configmap.yaml
kubectl apply -f deploy/k8s/secrets.yaml
kubectl apply -f deploy/k8s/          # 各服务 Deployment + Service + Ingress
```

要点：
- **多副本**：权限/任务服务可水平扩容，通过环境变量注入不同 `Snowflake__WorkerId`（可用 StatefulSet 序号或 init 脚本）；
- **健康检查**：就绪探针 `/health`，失败自动摘流量；
- **资源限制**：每个服务建议 512Mi~1Gi 内存起步，按实测调整。

## 6. CI/CD

| 方式 | 入口 | 流程 |
|---|---|---|
| Jenkins | `deploy/jenkins/Jenkinsfile` | 构建 → xUnit 测试 → 打镜像 → 推仓库 → SSH 滚动更新（凭据：docker-registry / deploy-host） |
| GitHub Actions | `deploy/github/workflows/ci.yml` | push main 自动测试 + 构建镜像推送（该目录需移到仓库根 `.github/`） |

## 7. 日常运维

### 7.1 健康检查
```bash
curl http://localhost:5000/health   # 网关
curl http://localhost:5001/health   # 权限服务
curl http://localhost:5002/health   # 任务服务
# 或直接看页面：系统监控 → 服务管理中心
```

### 7.2 日志排查
- **页面**：日志中心按时间/级别/服务/关键字过滤，支持 TraceId 全链路串查；
- **本地文件**：各服务 `logs/` 目录（容器内挂载出来）；
- **ES 集中日志**：Kibana 创建 `taoadmin-*` 索引模式后检索。

### 7.3 数据库备份与恢复
```bash
# 每日备份（cron）
mysqldump -h127.0.0.1 -uroot -p'***' --single-transaction tao_admin | gzip > backup/tao_admin_$(date +%F).sql.gz

# 恢复
gunzip < backup/tao_admin_2026-10-30.sql.gz | mysql -uroot -p'***' tao_admin
```

### 7.4 版本升级
```bash
# 滚动更新（多实例场景）
# 1) 新版本先上一个实例 → 观察服务监控中心与错误日志
# 2) 正常后逐个替换其余实例；数据库变更由 CodeFirst 在服务启动时自动执行
# 3) 回滚：切回上一版本镜像即可（数据库变更需评估向下兼容）
```

### 7.5 常见问题

| 现象 | 排查 |
|---|---|
| 前端登录 404 | 前端请求前缀是否带 `/api`（`.env.development` 的 `VITE_API_BASE_URL=/api`）；Nginx/网关路由是否匹配 |
| 接口 401 | JWT SecretKey 三个服务是否一致；令牌是否过期 |
| 接口 403 | 角色未绑定该接口权限码 |
| 服务监控显示 DOWN | 目标服务心跳失败：看该服务日志与 Redis 连接 |
| 任务不执行 | 任务是否被暂停；Cron 六位格式是否正确；TaskService 是否存活 |
| 雪花 ID 冲突异常 | 多实例 WorkerId 重复，检查环境变量注入 |
| 消息队列消息堆积/丢失 | 内存实现仅单实例有效；多实例务必切 RabbitMQ（`RabbitMq:Enabled=true`） |
| 接口返回 429 请求太频繁 | 触发网关限流（默认 100 次/10 秒），确认是否被爬虫/压测拖累，按需调阈值 |
| 接口返回 503 服务熔断中 | 目标服务连续失败被熔断：先恢复该服务，冷却 30 秒后自动恢复 |
| 同一操作被执行两次 | 调用方未带 `Idempotency-Key` 请求头；防重接口必须随请求携带唯一键 |
| 登录提示「验证码不正确或已过期」 | 默认每次登录都要验证码；验证码一次性消费（校验即删），输错/过期后点图刷新重取 |
| 登录提示账号已锁定（HTTP 423） | 连续失败达阈值（默认 5 次）触发锁定（默认 15 分钟），到期自动解锁；可在「登录安全」页调整阈值 |
| 自动化/脚本登录被验证码挡住 | 先 `GET /api/identity/audit/captcha` 取 SVG 答案再随登录提交；或在「登录安全」页关闭验证码开关 |

### 7.6 安全基线（上线必查）

- [ ] 修改默认账号 admin 密码
- [ ] 替换 JWT SecretKey（三服务一致）
- [ ] 保留登录验证码开启（`loginCaptchaRequired`），按业务放宽/收紧失败锁定阈值
- [ ] 数据库不用 root，弱口令全部替换
- [ ] MySQL/Redis/Nacos/ES 不对公网暴露
- [ ] MinIO 若接入，Endpoint/AccessKey/SecretKey 仅内网可用，不对公网暴露
- [ ] 网关启用 HTTPS（Nginx/Ingress 层）
- [ ] 开启日志集中存储，保留 ≥ 30 天

## 8. 数据库读写分离

应用层已内置支持（SqlSugar 主从路由），启用分三步：

### 8.1 搭建主从复制（数据库侧）
1. 主库开启 binlog（`server-id=1`、`log_bin=mysql-bin`、`binlog_format=ROW`），创建复制账号；
2. 从库 `server-id` 唯一、`read_only=1`，导入 `deploy/sql/tao_admin_init.sql` 建好数据底座；
3. `CHANGE MASTER TO ...` + `START SLAVE`，`SHOW SLAVE STATUS` 双 Yes 即成。
   完整命令模板见 SQL 脚本末尾注释，或用云数据库只读实例（自带复制，跳过此步）。

### 8.2 应用侧配置（各服务 appsettings.json）
```jsonc
"ConnectionStrings": {
  "MySql": "server=mysql-master;...;database=tao_admin;",   // 主库（写/事务）
  "Slaves": [                                                  // 从库（读）
    "server=mysql-slave1;...;database=tao_admin;",
    "server=mysql-slave2;...;database=tao_admin;"
  ]
},
"ReadWriteSplitting": { "Enabled": true }                      // 总开关
```

### 8.3 生效规则与注意事项
- 路由规则：查询（Queryable）→ 从库（多从库自动分发）；写入/更新/删除/事务 → 主库；CodeFirst 建表 → 主库；
- **主从延迟**：从库复制存在毫秒~秒级延迟。「写后立即读」场景（新增后马上查列表）用 `db.ForceMaster(...)` 固定走主库，见 `TaoAdmin.Common/SqlSugar/ReadWriteExtensions.cs`；
- 从库必须只读（`read_only=1`），避免绕过主库写数据造成不一致；
- 扩容：加从库只需在 `Slaves` 数组追加连接串并重启服务。

## 9. 消息队列（内存 / RabbitMQ 双实现）

消息队列用于服务间异步协作（如「新增用户 → 自动发欢迎消息」）。抽象层统一，切换提供方只改配置：

### 9.1 内存实现（默认，本地/单机）
- 零依赖，随服务启动即用（`RabbitMq:Enabled=false`）；
- 消息存于进程内存，**服务重启消息即丢失**，仅适用于单实例部署与开发演示。

### 9.2 RabbitMQ（生产/多实例推荐）

**方式一：docker compose（推荐）** —— compose 已内置 rabbitmq 服务：

```bash
cd deploy && docker compose up -d rabbitmq
# 控制台：http://localhost:15672（guest/guest，上线务必改密码）
```

**方式二：现有 RabbitMQ 集群**，在 IdentityService `appsettings.json`：

```jsonc
"RabbitMq": {
  "Enabled": true,                 // 总开关：true 切换为 RabbitMQ
  "HostName": "rabbitmq",          // 【线上】填 compose 服务名或集群地址
  "Port": 5672,
  "UserName": "guest",             // 【线上】务必改掉默认账号
  "Password": "guest",
  "VirtualHost": "/"               // 多套环境共用集群时用不同 vHost 区分
}
```

- 客户端已开启自动重连与持久化投递，RabbitMQ 短暂重启不丢已确认消息；
- 切换后重启 IdentityService 生效，可用 `GET /api/identity/messages/demo/stats` 确认提供方已变为 rabbitmq；
- 业务侧无需任何改动：收发均面向 `IMessageQueue`，新增消费者只需实现 `IMessageHandler` 并注册到 DI。

## 10. 网关限流与熔断（GatewayService）

### 10.1 限流（固定窗口）

`GatewayService/appsettings.json`：

```jsonc
"RateLimiting": {
  "Enabled": true,
  "PermitLimit": 100,        // 窗口内最大请求数
  "WindowSeconds": 10        // 窗口时长（秒）
}
```

- 分桶规则：已登录按用户 ID，未登录按客户端 IP；
- 超限返回 429（JSON 中文提示），窗口滚动后自动恢复；
- 压测场景建议调大 `PermitLimit` 或在负载均衡层先做一次限流。

### 10.2 熔断（按路由前缀三态）

`GatewayService/appsettings.json`：

```jsonc
"CircuitBreaker": {
  "Enabled": true,
  "FailureThreshold": 5,     // 连续失败次数阈值（5xx / 连接失败）
  "BreakSeconds": 30         // 熔断冷却时长（秒），冷却后半开试探
}
```

- 熔断按目标服务（identity / job / monitor）独立统计，互不影响；
- 熔断打开期间请求直接 503 快速失败，保护故障服务不被继续拖垮；
- 目标服务恢复后，最多等 `BreakSeconds` 即自动恢复转发，无需重启网关。

## 11. 多租户与接口幂等

### 11.1 多租户
- 总开关：IdentityService `MultiTenant:Enabled`（默认 true）。关闭后所有租户过滤逻辑失效，退化为单组织系统；
- 数据规则：`TenantId=0` 为平台公共数据（全租户可见）；租户用户仅见「公共 + 本租户」数据；写入时业务层显式落租户 ID，超管跨租户数据可见；
- 演示数据：启动时自动种子「华东/华南分公司 + 4 个演示账号 + 审批流模板与模拟实例」，可登录 east01 体验（见操作手册）。

### 11.2 接口幂等
- 防重范围：标注 `[Idempotent]` 的接口（发起审批、消息演示发布等写操作）；
- 存储与有效期：内存存储，幂等键保留 600 秒（`Idempotency:TtlSeconds` 可调）；
- **多实例部署**：内存幂等表各实例独立，跨实例防重需将 `IdempotencyStore` 替换为 Redis 实现（预留接口 `IIdempotencyStore`）。

## 12. 接口文档（Swagger）

- 三个服务默认开启 `/swagger`，便于联调与对接；
- **生产环境建议关闭**：注释掉各服务 Program.cs 中的 `app.UseSwagger()` / `app.UseSwaggerUI()` 两行即可（或用环境变量包裹）。

## 13. 操作日志与数据字典

- `sys_operation_log` 表由中间件异步写入，**写入失败不影响业务**；日志量随业务增长，建议按月归档或分区（`DELETE FROM sys_operation_log WHERE CreatedAt < ...`）；
- 登录审计记录尝试的账号（含失败），可据此在 SQL 层做暴力破解告警（如 5 分钟内同一账号失败 > 10 次）；
- 数据字典表 `sys_dict_type` / `sys_dict_data`：`TenantId=0` 为平台公共字典（全租户可见），租户私有字典随租户过滤自动隔离；种子逐项幂等，删除的编码不会在重启时复活（编码存在即跳过）。

## 14. 文件中心（独立解决方案部署）

TaoFiles 为独立解决方案（`TaoFiles/` 目录，与基础框架同级），单独构建、单独部署、单独配库：

```bash
# 1) 建库（也可用任意独立 MySQL 实例）
mysql -uroot -p -e "CREATE DATABASE tao_files DEFAULT CHARACTER SET utf8mb4;"

# 2) 配置 src/TaoFiles.Api/appsettings.json 的 ConnectionStrings:MySql
# 3) 启动（端口按需，默认建议 5003）
cd TaoFiles/src/TaoFiles.Api
dotnet run --urls http://0.0.0.0:5003
```

要点：
- **存储目录持久化**：`FileStorage:Root`（默认 `{contentRoot}/uploads`）务必挂载 named volume 或持久盘，容器升级不丢文件；
- **大小与类型限制**：`FileStorage:MaxSizeMB`（默认 50）、`FileStorage:AllowedExtensions`（扩展名白名单）；
- **API Key**：配置 `TaoFiles:ApiKey` 后所有接口要求 `X-Api-Key` 请求头（内网可关闭）；
- **秒传去重**：按 MD5 判重，同内容文件全库只存一份；删除默认软删（`physicalDelete=true` 才清物理文件）；
- **业务系统集成**：业务表只存上传返回的 `fileUrl` 相对路径，展示时拼文件中心地址；替换为 MinIO/OSS 时业务零改动；
- 表结构（sys_file）由 CodeFirst 启动时自动创建，无需手工执行 SQL。

## 15. 内置文件中心存储（本地盘 / MinIO 切换）

基础框架内置文件中心（`/api/identity/files/**`）的存储经 `FileStorage:Provider` 切换，
IdentityService `appsettings.json`：

```jsonc
"FileStorage": {
  "Provider": "local",          // local = 本地磁盘（默认）；minio = 内网 MinIO 对象存储
  "Root": "uploads",            // 仅 local 生效：本地盘存储根目录（相对 contentRoot）
  "Minio": {                    // 仅 minio 生效：四项连接参数
    "Endpoint": "http://minio.internal:9000",
    "AccessKey": "xxx",
    "SecretKey": "xxx",
    "Bucket": "taoadmin"
  }
}
```

要点：

- **数据不出内网**：MinIO Endpoint 指向内网对象存储（S3 兼容协议，AWS Signature V4 签名），
  **禁止配置公有云 OSS**；
- **对象键与本地盘同构**：`/uploads/{yyyy}/{MM}/{GUID}{ext}`，业务侧保存相对路径，切 Provider 无感；
- **本地盘持久化**：Provider=local 时务必把 `FileStorage:Root` 目录挂 named volume 或持久盘，容器升级不丢文件；
- **秒传去重**：同 MD5 未删记录直接复用，全库一份物理文件（sys_file 表由 CodeFirst 自动建）；
- **下载权限**：下载仅要求登录令牌，无令牌返回 401；上传/分页/删除要求 `sys:file:manage` 权限；
- 与第 14 节的独立解决方案 TaoFiles 互不依赖：基础框架内业务用内置文件中心即可，无需额外部署。
