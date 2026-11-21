-- 亿级分页优化：为已有部署补建二级索引（2026-11-21）
-- 说明：新部署由 CodeFirst 按实体索引声明自动创建；已有库执行本脚本补齐。
-- MySQL 8 无 ADD INDEX IF NOT EXISTS，重复执行会报 Duplicate key name，可忽略该报错。

-- 操作日志表：时间+主键（游标翻页/时间排序）、账号+时间、类别+时间（筛选）
ALTER TABLE `sys_operation_log`
  ADD KEY `idx_oplog_created_id` (`CreatedAt`, `Id` DESC),
  ADD KEY `idx_oplog_user_created` (`UserName`, `CreatedAt`),
  ADD KEY `idx_oplog_category_created` (`Category`, `CreatedAt`);

-- 文件记录表（TaoAdmin 管理端 sys_file）：时间+主键、文件名
ALTER TABLE `sys_file`
  ADD KEY `idx_file_created_id` (`CreatedAt`, `Id` DESC),
  ADD KEY `idx_file_name` (`FileName`);

-- 文件中心独立库（tao_files.sys_file，若与上面同库同表则已覆盖）
ALTER TABLE `sys_file`
  ADD KEY `idx_file_created_id` (`CreatedAt`, `Id` DESC),
  ADD KEY `idx_file_name` (`FileName`);
