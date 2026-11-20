# TaoAdmin 项目文档索引

> 适用版本：2026-11-18 主干 ｜ 更新日期：2026-11-19

本目录存放项目全套文档，建议按以下顺序阅读：

| 文件 | 内容 | 适合谁 |
|---|---|---|
| [01-Requirements.md](01-Requirements.md) | 项目背景、功能需求（FR-100~1900，含防护审计/内置文件中心）、非功能需求 | 全员 |
| [02-Mindmap.html](02-Mindmap.html) | 项目全貌思维导图（浏览器直接打开） | 全员，快速了解全貌 |
| [03-Architecture.html](03-Architecture.html) | 分层架构：客户端→网关→服务→中间件（浏览器直接打开） | 开发/架构/运维 |
| [04-UserGuide.md](04-UserGuide.md) | 各功能模块操作说明（含验证码登录/登录安全/审计日志/通知渠道/文件中心） | 使用者/管理员 |
| [05-DevOpsGuide.md](05-DevOpsGuide.md) | 环境搭建、一键环境、配置清单、部署、日常运维（含 RabbitMQ/限流/熔断/幂等/MinIO） | 实施/运维 |

> 配置文件的逐项中文注释（含本地/线上示例）在各服务 `appsettings.json`、
> 前端 `vite.config.ts` / `nginx.conf`，以及 `deploy/docker-compose.yml` 中。
