# TaoFiles 文件中心

独立的文件管理解决方案：与业务系统解耦，单独部署，任何业务系统都可以直接集成。

## 特性

- **上传秒传/去重**：相同内容（MD5 一致）的文件重复上传直接复用，不占额外空间，返回 `reused: true`
- **业务集成零负担**：业务系统上传后只保存返回的相对路径（如 `/uploads/2026/10/xxx.png`），展示时拼服务地址即可
- **自带管理页面**：浏览器打开服务根路径，上传、搜索、全功能分页（总条数、每页条数切换、首末页快跳、页码跳转）、图片缩略图、一键复制相对路径、删除，开箱即用；TaoAdmin 基础框架后台也已内置「文件管理」菜单页（接口调用本服务，服务地址可在页面右上角配置）
- **安全**：扩展名白名单 + 大小限制；文件名以雪花 ID 重命名（不信任用户输入）；可选 API Key
- **存储可插拔**：`IFileStorageProvider` 抽象 + 本地磁盘实现，可替换为 MinIO/OSS，业务代码零改动
- **接口文档**：内置 Swagger（`/swagger`）

## 快速开始

```bash
# 1) 建库（CodeFirst 启动时自动建表）
mysql -uroot -p -e "CREATE DATABASE IF NOT EXISTS tao_files DEFAULT CHARACTER SET utf8mb4;"

# 2) 修改 src/TaoFiles.Api/appsettings.json 的数据库连接

# 3) 启动
cd src/TaoFiles.Api
dotnet run --urls http://0.0.0.0:5003
```

- 管理页面：http://localhost:5003/
- 接口文档：http://localhost:5003/swagger
- 健康检查：http://localhost:5003/health

## 业务系统集成

```bash
# 上传（multipart，字段名 file）
curl -F "file=@report.pdf" -F "source=订单系统" -F "uploadBy=张三" \
  http://localhost:5003/api/files/upload
```

```json
{
  "code": 200,
  "data": {
    "id": "5280159423008407552",
    "fileName": "report.pdf",
    "fileUrl": "/uploads/2026/10/5280159420568604672.pdf",
    "size": 54,
    "md5": "33aaafce91b3cd9b17d44f0a01c7b8dd",
    "reused": false
  }
}
```

业务系统只需把 `data.fileUrl`（相对路径）存进业务表。展示时拼接服务地址：
`http://文件中心地址 + /uploads/2026/10/xxx.png`。

其他接口：
- 文件分页列表：`GET /api/files?pageIndex=1&pageSize=10&keyword=名称&source=来源系统`
- 删除（软删）：`DELETE /api/files/{id}`；物理删除加 `?physicalDelete=true`

## 配置（appsettings.json）

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `ConnectionStrings:MySql` | 本机 root | 独立数据库 tao_files |
| `FileStorage:Root` | `{contentRoot}/uploads` | 存储根目录，生产建议挂载持久盘 |
| `FileStorage:MaxSizeMB` | 50 | 单文件大小上限 |
| `FileStorage:AllowedExtensions` | 常用格式 | 扩展名白名单（逗号分隔） |
| `TaoFiles:ApiKey` | 空 | 配置后所有接口要求 `X-Api-Key` 请求头 |

## 项目结构

```
TaoFiles/
└── src/TaoFiles.Api/
    ├── Program.cs               启动 + CodeFirst + Swagger + 可选 API Key
    ├── Entities/SysFile.cs      文件记录表
    ├── Storage/FileStorage.cs   存储抽象 + 本地磁盘实现
    ├── Controllers/             上传/列表/删除接口 + 应用服务
    └── wwwroot/index.html       文件管理页面
```

## 发布信息

- **版本**：1.0.0（纳入 Tao 系列的首个正式版本）
- **产品**：TaoFiles ｜ Company/Authors：Tao ｜ Copyright (c) 2026 Tao
- **运行环境**：.NET 8（后端）/ Node 18+（前端构建）/ MySQL 8 或 MariaDB / Redis 7
- **许可**：归委托方所有，未授权不得对外分发
