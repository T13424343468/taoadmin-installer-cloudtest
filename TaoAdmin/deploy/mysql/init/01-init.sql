-- 初始化数据库（业务表由后端 SqlSugar CodeFirst 自动建表并种子数据）
CREATE DATABASE IF NOT EXISTS `tao_admin` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;

-- 可选：独立业务账号（生产建议不用 root）
-- CREATE USER IF NOT EXISTS 'taoadmin'@'%' IDENTIFIED BY 'Strong@Password';
-- GRANT ALL PRIVILEGES ON `tao_admin`.* TO 'taoadmin'@'%';
-- FLUSH PRIVILEGES;

-- 文件中心独立库（TaoFiles.Api CodeFirst 自动建表）
CREATE DATABASE IF NOT EXISTS `tao_files` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
