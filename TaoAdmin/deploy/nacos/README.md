# Nacos 配置中心使用说明

## DataId 约定

每个服务一份配置，命名：`taoadmin-{service}.yaml`，分组 `TAOADMIN`，命名空间按环境区分（dev / prod）。

| DataId | 所属服务 |
|---|---|
| taoadmin-gateway.yaml | 网关（含 YARP 路由，可动态调整） |
| taoadmin-identity.yaml | 权限服务 |
| taoadmin-task.yaml | 定时任务服务 |

## 示例：taoadmin-identity.yaml

```yaml
Logging:
  LogLevel:
    Default: Information
Jwt:
  Secret: "请使用足够长的随机串，生产建议放 Nacos 加密配置"
  Issuer: TaoAdmin
  Audience: TaoAdmin
  ExpireMinutes: 120
Cache:
  UserPermissionTTLSeconds: 300   # 用户权限缓存时长
Snowflake:
  DatacenterId: 1
  WorkerId: 1                     # 多实例部署时每实例不同
```

## 导入步骤

1. 启动后打开 `http://localhost:8848/nacos`（默认 nacos/nacos）。
2. 新建命名空间 dev，记录其命名空间 ID。
3. 按上表逐个创建 DataId，粘贴 YAML 内容。
4. 服务端通过 `NACOS_HOST` 环境变量连接，启动时拉取配置并支持热更新（雪flake WorkerId 建议配合 Nacos 注册顺序或环境变量分配，避免多实例冲突）。
