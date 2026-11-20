# 日志方案说明

本项目**不需要**额外部署 Filebeat / Logstash：

- 各服务通过 Serilog 的 `Serilog.Sinks.Elasticsearch` 直接把结构化日志写入 Elasticsearch。
- 网关请求日志、SqlSugar 执行 SQL、Quartz 任务调度日志都会带上 `TraceId` 字段，方便全链路排查。
- 日志管理中心页面通过网关的 `/api/monitor/logs` 查询 ES（如需在页面查看），底层为 Elasticsearch DSL 查询。

## Kibana 配置指引

1. 打开 `http://localhost:5601`。
2. 进入 Stack Management -> Index Patterns，创建 `logstash-*`，时间字段选 `@timestamp`。
3. 在 Discover 中即可按 `service.name`、`level`、`TraceId` 过滤日志。

## 索引字段（常用）

| 字段 | 说明 |
|---|---|
| `service.name` | 服务名（gateway/identity/task） |
| `level` | Information / Warning / Error |
| `TraceId` | 全链路追踪 ID |
| `message` | 日志正文（含慢 SQL 明细） |
