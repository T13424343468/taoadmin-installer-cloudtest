-- ============================================================================
-- TaoAdmin 数据库一键初始化脚本
-- ============================================================================
-- 用途：一键创建 tao_admin 库、全部 16 张表及种子数据（含 admin 平台账号、
--       多租户演示账号与数据、工作流模板与模拟审批实例、定时任务示例、数据字典），
--       导出于 2026-10-30
-- 执行方式（任选其一）：
--   mysql -uroot -p < tao_admin_init.sql
--   或在 Navicat / DataGrip / phpMyAdmin 中打开整个文件执行
-- 说明：
--   1. 本脚本从零初始化，也可用于从库（slave）快速建立与主库相同的数据底座；
--   2. 服务启动时 CodeFirst 会自动校验/补齐表结构，执行本脚本后直接启动服务即可；
--   3. 默认账号 admin / Admin@123，演示账号 east01 / east_leader / south01 / south_manager
--      密码均为 Tenant@123（多租户与工作流演示用），上线前务必全部修改密码并替换 JWT 密钥；
--   4. 表清单：sys_user / sys_role / sys_menu / sys_api / sys_role_menu / sys_role_api /
--      sys_user_role / sys_job / sys_tenant / sys_message / sys_workflow /
--      sys_workflow_instance / sys_workflow_task / sys_dict_type / sys_dict_data /
--      sys_operation_log（文件中心为独立库 tao_files，见 TaoFiles/README.md）；
--   5. 主从复制搭建步骤见文件末尾注释（不会被自动执行）。
-- ============================================================================

/*M!999999\- enable the sandbox mode */ 
-- MariaDB dump 10.19  Distrib 10.11.14-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: 127.0.0.1    Database: tao_admin
-- ------------------------------------------------------
-- Server version	10.11.14-MariaDB-0ubuntu0.24.04.1

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Current Database: `tao_admin`
--

CREATE DATABASE /*!32312 IF NOT EXISTS*/ `tao_admin` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci */;

USE `tao_admin`;

--
-- Table structure for table `sys_api`
--

DROP TABLE IF EXISTS `sys_api`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_api` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `Name` varchar(100) NOT NULL COMMENT '接口名称',
  `Path` varchar(200) NOT NULL COMMENT '接口路径',
  `HttpMethod` varchar(10) NOT NULL COMMENT 'HTTP方法',
  `PermsCode` varchar(100) NOT NULL COMMENT '权限码',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='接口资源';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_api`
--

LOCK TABLES `sys_api` WRITE;
/*!40000 ALTER TABLE `sys_api` DISABLE KEYS */;
INSERT INTO `sys_api` VALUES
(903770869715832832,'用户分页查询','/api/identity/users','GET','sys:user:list',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832833,'用户新增','/api/identity/users','POST','sys:user:add',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832834,'用户修改','/api/identity/users/{id}','PUT','sys:user:edit',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832835,'用户删除','/api/identity/users/{id}','DELETE','sys:user:delete',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832836,'角色列表','/api/identity/roles/all','GET','sys:role:list',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832837,'任务分页查询','/api/job/jobs','GET','sys:job:list',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832838,'服务健康查询','/api/monitor/services','GET','monitor:service:list',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869715832839,'日志查询','/api/monitor/logs','GET','monitor:log:list',1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_api` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_dict_data`
--

DROP TABLE IF EXISTS `sys_dict_data`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_dict_data` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `DictCode` varchar(50) NOT NULL COMMENT '字典编码',
  `Label` varchar(50) NOT NULL COMMENT '显示文本',
  `Value` varchar(50) NOT NULL COMMENT '存储值',
  `Sort` int(11) NOT NULL COMMENT '排序号',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `Remark` varchar(200) DEFAULT NULL COMMENT '备注',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='字典数据';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_dict_data`
--

LOCK TABLES `sys_dict_data` WRITE;
/*!40000 ALTER TABLE `sys_dict_data` DISABLE KEYS */;
INSERT INTO `sys_dict_data` VALUES
(903774828132175872,'gender','男','M',1,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828136370176,'gender','女','F',2,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828136370177,'gender','未知','U',3,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828169924608,'status','启用','1',1,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828169924609,'status','禁用','0',2,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828182507520,'workflow_status','进行中','1',1,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828182507521,'workflow_status','已通过','2',2,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828182507522,'workflow_status','已驳回','3',3,1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774963960516608,'test_dict','选项A','A',1,1,NULL,0,'2026-10-30 06:39:02','2026-10-30 06:39:02',0);
/*!40000 ALTER TABLE `sys_dict_data` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_dict_type`
--

DROP TABLE IF EXISTS `sys_dict_type`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_dict_type` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `DictName` varchar(50) NOT NULL COMMENT '字典名称',
  `DictCode` varchar(50) NOT NULL COMMENT '字典编码',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `Remark` varchar(200) DEFAULT NULL COMMENT '备注',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='字典类型';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_dict_type`
--

LOCK TABLES `sys_dict_type` WRITE;
/*!40000 ALTER TABLE `sys_dict_type` DISABLE KEYS */;
INSERT INTO `sys_dict_type` VALUES
(903774828052484096,'性别','gender',1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828161536000,'通用状态','status',1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774828178313216,'审批实例状态','workflow_status',1,NULL,0,'2026-10-30 06:38:30','2026-10-30 06:38:30',0),
(903774963658526720,'测试字典','test_dict',1,NULL,0,'2026-10-30 06:39:02','2026-10-30 06:39:02',0),
(903775058646929408,'编辑测试改名','edit_test',1,NULL,0,'2026-10-30 06:39:25','2026-10-30 06:39:25',1),
(903775183708491776,'编辑测试','edit_test2',1,NULL,0,'2026-10-30 06:39:55','2026-10-30 06:39:55',1);
/*!40000 ALTER TABLE `sys_dict_type` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_job`
--

DROP TABLE IF EXISTS `sys_job`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_job` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `Name` varchar(100) NOT NULL COMMENT '任务名',
  `Group` varchar(50) NOT NULL COMMENT '任务组',
  `Cron` varchar(50) NOT NULL COMMENT 'Cron 表达式',
  `Handler` varchar(100) NOT NULL COMMENT '处理器',
  `Params` varchar(500) DEFAULT NULL COMMENT '任务参数',
  `Status` varchar(20) NOT NULL COMMENT '状态',
  `Description` varchar(500) DEFAULT NULL COMMENT '备注',
  `LastFireTime` datetime DEFAULT NULL COMMENT '上次执行时间',
  `NextFireTime` datetime DEFAULT NULL COMMENT '下次执行时间',
  `LastResult` varchar(500) DEFAULT NULL COMMENT '上次执行结果',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='定时任务';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_job`
--

LOCK TABLES `sys_job` WRITE;
/*!40000 ALTER TABLE `sys_job` DISABLE KEYS */;
INSERT INTO `sys_job` VALUES
(903771277356048384,'heartbeatJob','default','0/30 * * * * ?','heartbeatJob',NULL,'RUNNING','示例任务：每 30 秒向 Redis 写入服务心跳',NULL,NULL,NULL,'2026-10-30 06:24:23','2026-10-30 06:24:23',0);
/*!40000 ALTER TABLE `sys_job` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_menu`
--

DROP TABLE IF EXISTS `sys_menu`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_menu` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `ParentId` bigint(20) NOT NULL COMMENT '父菜单ID 顶级为0',
  `MenuName` varchar(50) NOT NULL COMMENT '菜单名称',
  `MenuType` int(11) NOT NULL COMMENT '类型 0目录 1菜单 2按钮',
  `Path` varchar(200) DEFAULT NULL COMMENT '路由地址',
  `Component` varchar(200) DEFAULT NULL COMMENT '前端组件路径',
  `PermsCode` varchar(100) DEFAULT NULL COMMENT '权限码',
  `Icon` varchar(100) DEFAULT NULL COMMENT '图标',
  `OrderNum` int(11) NOT NULL COMMENT '排序号',
  `Visible` tinyint(1) NOT NULL COMMENT '是否显示',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='系统菜单';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_menu`
--

LOCK TABLES `sys_menu` WRITE;
/*!40000 ALTER TABLE `sys_menu` DISABLE KEYS */;
INSERT INTO `sys_menu` VALUES
(903770869673889792,0,'系统管理',0,'/system',NULL,NULL,'Setting',1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889793,0,'任务中心',0,'/task',NULL,NULL,'Timer',2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889794,0,'系统监控',0,'/monitor',NULL,NULL,'Monitor',3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889795,903770869673889792,'用户管理',1,'/system/user','system/user/index',NULL,NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889796,903770869673889792,'角色管理',1,'/system/role','system/role/index',NULL,NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889797,903770869673889792,'菜单管理',1,'/system/menu','system/menu/index',NULL,NULL,3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889798,903770869673889792,'接口管理',1,'/system/api','system/api/index',NULL,NULL,4,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889799,903770869673889793,'定时任务',1,'/task/job','job/index',NULL,NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889800,903770869673889794,'服务管理中心',1,'/monitor/service','monitor/service/index',NULL,NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889801,903770869673889794,'日志管理中心',1,'/monitor/log','monitor/log/index',NULL,NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889802,903770869673889795,'用户新增',2,NULL,NULL,'sys:user:add',NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889803,903770869673889795,'用户修改',2,NULL,NULL,'sys:user:edit',NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889804,903770869673889795,'用户删除',2,NULL,NULL,'sys:user:delete',NULL,3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889805,903770869673889795,'重置密码',2,NULL,NULL,'sys:user:resetPwd',NULL,4,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889806,903770869673889795,'分配角色',2,NULL,NULL,'sys:user:assignRole',NULL,5,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889807,903770869673889796,'角色新增',2,NULL,NULL,'sys:role:add',NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889808,903770869673889796,'角色修改',2,NULL,NULL,'sys:role:edit',NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889809,903770869673889796,'角色删除',2,NULL,NULL,'sys:role:delete',NULL,3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889810,903770869673889796,'分配权限',2,NULL,NULL,'sys:role:assign',NULL,4,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889811,903770869673889797,'菜单新增',2,NULL,NULL,'sys:menu:add',NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889812,903770869673889797,'菜单修改',2,NULL,NULL,'sys:menu:edit',NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889813,903770869673889797,'菜单删除',2,NULL,NULL,'sys:menu:delete',NULL,3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889814,903770869673889798,'接口新增',2,NULL,NULL,'sys:api:add',NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889815,903770869673889798,'接口修改',2,NULL,NULL,'sys:api:edit',NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889816,903770869673889798,'接口删除',2,NULL,NULL,'sys:api:delete',NULL,3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889817,903770869673889799,'任务新增',2,NULL,NULL,'sys:job:add',NULL,1,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889818,903770869673889799,'任务修改',2,NULL,NULL,'sys:job:edit',NULL,2,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889819,903770869673889799,'任务删除',2,NULL,NULL,'sys:job:delete',NULL,3,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889820,903770869673889799,'任务暂停',2,NULL,NULL,'sys:job:pause',NULL,4,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889821,903770869673889799,'任务恢复',2,NULL,NULL,'sys:job:resume',NULL,5,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869673889822,903770869673889799,'立即执行',2,NULL,NULL,'sys:job:run',NULL,6,1,1,'2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_menu` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_message`
--

DROP TABLE IF EXISTS `sys_message`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_message` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `Title` varchar(100) NOT NULL COMMENT '消息标题',
  `Content` varchar(500) DEFAULT NULL COMMENT '消息内容',
  `Topic` varchar(50) NOT NULL COMMENT '来源主题',
  `ReceiverId` bigint(20) NOT NULL COMMENT '接收人ID 0为系统',
  `IsRead` tinyint(1) NOT NULL COMMENT '是否已读',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='站内消息';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_message`
--

LOCK TABLES `sys_message` WRITE;
/*!40000 ALTER TABLE `sys_message` DISABLE KEYS */;
INSERT INTO `sys_message` VALUES
(903770872081420288,'欢迎使用 TaoAdmin','系统已内置消息队列演示：新增用户后会自动收到欢迎消息。','system.notice',0,0,'2026-10-30 06:22:47','2026-10-30 06:22:47',0),
(903770872081420289,'系统更新通知','本次更新新增：消息队列、工作流引擎、多租户支持。','system.notice',0,0,'2026-10-30 04:22:47','2026-10-30 04:22:47',0),
(903770872081420290,'数据备份完成','每日数据库自动备份已完成，无异常。','system.notice',0,1,'2026-10-29 06:22:47','2026-10-29 06:22:47',0);
/*!40000 ALTER TABLE `sys_message` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_operation_log`
--

DROP TABLE IF EXISTS `sys_operation_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_operation_log` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `UserId` bigint(20) NOT NULL COMMENT '操作人ID',
  `UserName` varchar(50) NOT NULL COMMENT '操作人账号',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `Category` varchar(20) NOT NULL COMMENT '类别 login/operation',
  `Method` varchar(10) NOT NULL COMMENT '请求方法',
  `Path` varchar(200) NOT NULL COMMENT '请求路径',
  `QueryString` varchar(500) DEFAULT NULL COMMENT '查询字符串',
  `StatusCode` int(11) NOT NULL COMMENT 'HTTP状态码',
  `Success` int(11) NOT NULL COMMENT '结果 1成功 0失败',
  `Ip` varchar(50) NOT NULL COMMENT '客户端IP',
  `ElapsedMs` int(11) NOT NULL COMMENT '耗时毫秒',
  `TraceId` varchar(50) DEFAULT NULL COMMENT '链路追踪ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='操作日志';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_operation_log`
--

LOCK TABLES `sys_operation_log` WRITE;
/*!40000 ALTER TABLE `sys_operation_log` DISABLE KEYS */;
INSERT INTO `sys_operation_log` VALUES
(903774937796448256,0,'',0,'login','POST','/auth/login',NULL,200,1,'127.0.0.1',543,'0HNPM1LJVM509:00000002','2026-10-30 06:38:56','2026-10-30 06:38:56',0),
(903774963675303936,903770868604342272,'admin',0,'operation','POST','/dict/types',NULL,200,1,'127.0.0.1',15,'0HNPM1LJVM509:0000000A','2026-10-30 06:39:02','2026-10-30 06:39:02',0),
(903775047922094080,0,'',0,'login','POST','/auth/login',NULL,200,1,'127.0.0.1',634,'0HNPM1LT187I6:00000001','2026-10-30 06:39:22','2026-10-30 06:39:22',0),
(903775048085671936,0,'',0,'login','POST','/auth/login',NULL,200,1,'127.0.0.1',22,'0HNPM1LT187I6:00000002','2026-10-30 06:39:22','2026-10-30 06:39:22',0),
(903775059183800320,903770868604342272,'admin',0,'operation','PUT','/dict/types/903775058646929408',NULL,200,1,'127.0.0.1',51,'0HNPM1LT187I6:00000007','2026-10-30 06:39:25','2026-10-30 06:39:25',0),
(903775172492922880,0,'admin',0,'login','POST','/auth/login',NULL,200,1,'127.0.0.1',651,'0HNPM1M5SAT2O:00000001','2026-10-30 06:39:52','2026-10-30 06:39:52',0),
(903775172752969728,0,'hacker',0,'login','POST','/auth/login',NULL,200,1,'127.0.0.1',15,'0HNPM1M5SAT2O:00000002','2026-10-30 06:39:52','2026-10-30 06:39:52',0),
(903775317783613440,0,'hacker2',0,'login','POST','/auth/login',NULL,200,0,'127.0.0.1',200,'0HNPM1MGAVQ2V:00000001','2026-10-30 06:40:27','2026-10-30 06:40:27',0),
(903775319901736960,0,'admin',0,'login','POST','/auth/login',NULL,200,1,'127.0.0.1',479,'0HNPM1MGAVQ2V:00000002','2026-10-30 06:40:27','2026-10-30 06:40:27',0);
/*!40000 ALTER TABLE `sys_operation_log` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_role`
--

DROP TABLE IF EXISTS `sys_role`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_role` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `RoleName` varchar(50) NOT NULL COMMENT '角色名称',
  `RoleCode` varchar(50) NOT NULL COMMENT '角色编码',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `Remark` varchar(200) DEFAULT NULL COMMENT '备注',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='系统角色';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_role`
--

LOCK TABLES `sys_role` WRITE;
/*!40000 ALTER TABLE `sys_role` DISABLE KEYS */;
INSERT INTO `sys_role` VALUES
(903770868520456192,'超级管理员','admin',1,'内置角色：拥有全部权限','2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770868520456193,'普通用户','common',1,'内置角色：基础查看权限','2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869858439168,'组长','group_leader',1,'工作流审批角色：第一级审批','2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869858439169,'经理','manager',1,'工作流审批角色：第二级审批','2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_role` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_role_api`
--

DROP TABLE IF EXISTS `sys_role_api`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_role_api` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `RoleId` bigint(20) NOT NULL COMMENT '角色ID',
  `ApiId` bigint(20) NOT NULL COMMENT '接口ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='角色接口关联';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_role_api`
--

LOCK TABLES `sys_role_api` WRITE;
/*!40000 ALTER TABLE `sys_role_api` DISABLE KEYS */;
INSERT INTO `sys_role_api` VALUES
(903770869753581568,903770868520456192,903770869715832832,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581569,903770868520456192,903770869715832833,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581570,903770868520456192,903770869715832834,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581571,903770868520456192,903770869715832835,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581572,903770868520456192,903770869715832836,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581573,903770868520456192,903770869715832837,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581574,903770868520456192,903770869715832838,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869753581575,903770868520456192,903770869715832839,'2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_role_api` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_role_menu`
--

DROP TABLE IF EXISTS `sys_role_menu`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_role_menu` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `RoleId` bigint(20) NOT NULL COMMENT '角色ID',
  `MenuId` bigint(20) NOT NULL COMMENT '菜单ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='角色菜单关联';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_role_menu`
--

LOCK TABLES `sys_role_menu` WRITE;
/*!40000 ALTER TABLE `sys_role_menu` DISABLE KEYS */;
INSERT INTO `sys_role_menu` VALUES
(903770869732610048,903770868520456192,903770869673889792,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610049,903770868520456192,903770869673889793,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610050,903770868520456192,903770869673889794,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610051,903770868520456192,903770869673889795,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610052,903770868520456192,903770869673889796,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610053,903770868520456192,903770869673889797,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610054,903770868520456192,903770869673889798,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610055,903770868520456192,903770869673889799,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610056,903770868520456192,903770869673889800,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610057,903770868520456192,903770869673889801,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610058,903770868520456192,903770869673889802,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610059,903770868520456192,903770869673889803,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610060,903770868520456192,903770869673889804,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610061,903770868520456192,903770869673889805,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610062,903770868520456192,903770869673889806,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610063,903770868520456192,903770869673889807,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610064,903770868520456192,903770869673889808,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610065,903770868520456192,903770869673889809,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610066,903770868520456192,903770869673889810,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610067,903770868520456192,903770869673889811,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610068,903770868520456192,903770869673889812,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610069,903770868520456192,903770869673889813,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610070,903770868520456192,903770869673889814,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610071,903770868520456192,903770869673889815,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610072,903770868520456192,903770869673889816,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610073,903770868520456192,903770869673889817,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610074,903770868520456192,903770869673889818,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610075,903770868520456192,903770869673889819,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610076,903770868520456192,903770869673889820,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610077,903770868520456192,903770869673889821,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869732610078,903770868520456192,903770869673889822,'2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_role_menu` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_tenant`
--

DROP TABLE IF EXISTS `sys_tenant`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_tenant` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `TenantName` varchar(50) NOT NULL COMMENT '租户名称',
  `TenantCode` varchar(50) NOT NULL COMMENT '租户编码',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `Contact` varchar(50) DEFAULT NULL COMMENT '联系人',
  `Remark` varchar(200) DEFAULT NULL COMMENT '备注',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='系统租户';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_tenant`
--

LOCK TABLES `sys_tenant` WRITE;
/*!40000 ALTER TABLE `sys_tenant` DISABLE KEYS */;
INSERT INTO `sys_tenant` VALUES
(903770869808107520,'华东分公司','east',1,'陈华东','演示租户：华东大区','2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869820690432,'华南分公司','south',1,'林华南','演示租户：华南大区','2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_tenant` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_user`
--

DROP TABLE IF EXISTS `sys_user`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_user` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `UserName` varchar(50) NOT NULL COMMENT '登录账号',
  `Password` varchar(100) NOT NULL COMMENT '密码（BCrypt）',
  `NickName` varchar(50) DEFAULT NULL COMMENT '昵称',
  `Phone` varchar(20) DEFAULT NULL COMMENT '手机号',
  `Email` varchar(100) DEFAULT NULL COMMENT '邮箱',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `LastLoginTime` datetime DEFAULT NULL COMMENT '最后登录时间',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='系统用户';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_user`
--

LOCK TABLES `sys_user` WRITE;
/*!40000 ALTER TABLE `sys_user` DISABLE KEYS */;
INSERT INTO `sys_user` VALUES
(903770868604342272,0,'admin','$2a$11$FEqJSgOBpUtOYSpYKnTT1eGdijEYN2F4uu5JnUuK6kUNtxjpgrE3q','系统管理员',NULL,NULL,1,'2026-10-30 06:40:27','2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770869862633472,903770869808107520,'east_leader','$2a$11$iBo1dVr5dGbF/xUmpvg4DeXA6FWUQIfdm7uOCbBp4TWeu2i7Cualy','李组长（华东）',NULL,'east.leader@demo.com',1,NULL,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770870407892992,903770869808107520,'east01','$2a$11$HcqKWNEoR3eg2xxi3Jxi6..AdfFIgZVclt3ZFFW7Xzpi8gLkct.mi','张员工（华东）',NULL,'east01@demo.com',1,'2026-10-30 06:23:08','2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770870953152512,903770869820690432,'south_manager','$2a$11$SRbye9t5GuwfB56znCYZ4OZx4HJrhLeG8a5Rz7g5crMDLDgiFY8aC','王经理（华南）',NULL,'south.manager@demo.com',1,NULL,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770871502606336,903770869820690432,'south01','$2a$11$5iU7sl/uXusYUW0PpXRl6.fKfYWG1/pf0Jh8hh7SFGfoA2km41ekS','赵员工（华南）',NULL,'south01@demo.com',1,NULL,'2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_user` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_user_role`
--

DROP TABLE IF EXISTS `sys_user_role`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_user_role` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `UserId` bigint(20) NOT NULL COMMENT '用户ID',
  `RoleId` bigint(20) NOT NULL COMMENT '角色ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='用户角色关联';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_user_role`
--

LOCK TABLES `sys_user_role` WRITE;
/*!40000 ALTER TABLE `sys_user_role` DISABLE KEYS */;
INSERT INTO `sys_user_role` VALUES
(903770869665501184,903770868604342272,903770868520456192,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770872064643072,903770869862633472,903770869858439168,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770872064643073,903770870407892992,903770868520456193,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770872064643074,903770870953152512,903770869858439169,'2026-10-30 06:22:46','2026-10-30 06:22:46',0),
(903770872064643075,903770871502606336,903770868520456193,'2026-10-30 06:22:46','2026-10-30 06:22:46',0);
/*!40000 ALTER TABLE `sys_user_role` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_workflow`
--

DROP TABLE IF EXISTS `sys_workflow`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_workflow` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `WorkflowName` varchar(50) NOT NULL COMMENT '流程名称',
  `WorkflowCode` varchar(50) NOT NULL COMMENT '流程编码',
  `Description` varchar(200) DEFAULT NULL COMMENT '流程说明',
  `NodesJson` text DEFAULT NULL COMMENT '节点定义JSON',
  `Status` int(11) NOT NULL COMMENT '状态 0停用 1启用',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='工作流定义';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_workflow`
--

LOCK TABLES `sys_workflow` WRITE;
/*!40000 ALTER TABLE `sys_workflow` DISABLE KEYS */;
INSERT INTO `sys_workflow` VALUES
(903770872110780416,'请假审批','leave_audit','员工请假审批：先组长审批，再经理审批','[{\"name\":\"组长审批\",\"approverRole\":\"group_leader\"},{\"name\":\"经理审批\",\"approverRole\":\"manager\"}]',1,0,'2026-10-30 06:22:47','2026-10-30 06:22:47',0),
(903770872123363328,'报销审批','expense_audit','费用报销：经理一级审批','[{\"name\":\"经理审批\",\"approverRole\":\"manager\"}]',1,0,'2026-10-30 06:22:47','2026-10-30 06:22:47',0);
/*!40000 ALTER TABLE `sys_workflow` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_workflow_instance`
--

DROP TABLE IF EXISTS `sys_workflow_instance`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_workflow_instance` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `WorkflowId` bigint(20) NOT NULL COMMENT '流程定义ID',
  `WorkflowName` varchar(50) NOT NULL COMMENT '流程名称',
  `BizTitle` varchar(100) NOT NULL COMMENT '业务标题',
  `Remark` varchar(500) DEFAULT NULL COMMENT '业务说明',
  `ApplicantId` bigint(20) NOT NULL COMMENT '发起人ID',
  `ApplicantName` varchar(50) NOT NULL COMMENT '发起人姓名',
  `CurrentNodeIndex` int(11) NOT NULL COMMENT '当前节点下标',
  `Status` int(11) NOT NULL COMMENT '状态 1进行中 2通过 3驳回 4取消',
  `FinishedAt` datetime DEFAULT NULL COMMENT '完成时间',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='工作流实例';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_workflow_instance`
--

LOCK TABLES `sys_workflow_instance` WRITE;
/*!40000 ALTER TABLE `sys_workflow_instance` DISABLE KEYS */;
INSERT INTO `sys_workflow_instance` VALUES
(903770872152723456,903770872110780416,'请假审批','张员工的年假申请','3 天年假，11-05 至 11-07',903770870407892992,'张员工（华东）',0,1,NULL,903770869808107520,'2026-10-30 03:22:47','2026-10-30 03:22:47',0),
(903770872152723458,903770872110780416,'请假审批','张员工的事假申请','1 天事假，10-28',903770870407892992,'张员工（华东）',1,1,NULL,903770869808107520,'2026-10-29 04:22:47','2026-10-29 10:22:47',0),
(903770872152723461,903770872123363328,'报销审批','张员工的差旅报销','差旅费 860 元，发票已交',903770870407892992,'张员工（华东）',0,2,'2026-10-29 06:22:47',903770869808107520,'2026-10-28 06:22:47','2026-10-29 06:22:47',0),
(903770872156917760,903770872110780416,'请假审批','赵员工的调休申请','调休 2 天',903770871502606336,'赵员工（华南）',1,3,'2026-10-30 01:22:47',903770869820690432,'2026-10-29 06:22:47','2026-10-30 01:22:47',0);
/*!40000 ALTER TABLE `sys_workflow_instance` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `sys_workflow_task`
--

DROP TABLE IF EXISTS `sys_workflow_task`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `sys_workflow_task` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `InstanceId` bigint(20) NOT NULL COMMENT '流程实例ID',
  `NodeName` varchar(50) NOT NULL COMMENT '节点名称',
  `NodeIndex` int(11) NOT NULL COMMENT '节点下标',
  `AssigneeRoleCode` varchar(50) NOT NULL COMMENT '审批角色编码',
  `Status` int(11) NOT NULL COMMENT '任务状态 0待审批 1通过 2驳回',
  `Comment` varchar(500) DEFAULT NULL COMMENT '审批意见',
  `HandlerName` varchar(50) DEFAULT NULL COMMENT '审批人姓名',
  `HandleTime` datetime DEFAULT NULL COMMENT '处理时间',
  `TenantId` bigint(20) NOT NULL COMMENT '所属租户ID',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='工作流任务';
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `sys_workflow_task`
--

LOCK TABLES `sys_workflow_task` WRITE;
/*!40000 ALTER TABLE `sys_workflow_task` DISABLE KEYS */;
INSERT INTO `sys_workflow_task` VALUES
(903770872152723457,903770872152723456,'组长审批',0,'group_leader',0,NULL,NULL,NULL,903770869808107520,'2026-10-30 03:22:47','2026-10-30 03:22:47',0),
(903770872152723459,903770872152723458,'组长审批',0,'group_leader',1,'情况属实，同意','李组长（华东）','2026-10-29 10:22:47',903770869808107520,'2026-10-29 04:22:47','2026-10-29 10:22:47',0),
(903770872152723460,903770872152723458,'经理审批',1,'manager',0,NULL,NULL,NULL,903770869808107520,'2026-10-29 10:22:47','2026-10-29 10:22:47',0),
(903770872152723462,903770872152723461,'经理审批',0,'manager',1,'票据齐全，同意报销','王经理（华南）','2026-10-29 06:22:47',903770869808107520,'2026-10-28 06:22:47','2026-10-29 06:22:47',0),
(903770872156917761,903770872156917760,'组长审批',0,'group_leader',1,'转经理确认','值班组长','2026-10-29 20:22:47',903770869820690432,'2026-10-29 06:22:47','2026-10-29 20:22:47',0),
(903770872156917762,903770872156917760,'经理审批',1,'manager',2,'近期项目忙，建议下周再调休','王经理（华南）','2026-10-30 01:22:47',903770869820690432,'2026-10-29 20:22:47','2026-10-30 01:22:47',0);
/*!40000 ALTER TABLE `sys_workflow_task` ENABLE KEYS */;
UNLOCK TABLES;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2026-10-30  6:41:03


-- ---------------------------------------------------------------------------
-- 文件管理菜单（页面在基础框架前端，数据走独立的 TaoFiles 文件服务）
-- ---------------------------------------------------------------------------
INSERT INTO sys_menu (Id, ParentId, MenuName, MenuType, Path, Component, PermsCode, Icon, OrderNum, Visible, Status, CreatedAt, UpdatedAt, IsDeleted)
VALUES (5280300000000000001, 0, '文件管理', 1, '/file', 'file/index', NULL, 'FolderOpen', 4, 1, 1, NOW(), NOW(), 0);

INSERT INTO sys_role_menu (Id, RoleId, MenuId, CreatedAt, UpdatedAt, IsDeleted)
SELECT 5280300000000000002, Id, 5280300000000000001, NOW(), NOW(), 0
FROM sys_role WHERE RoleCode = 'admin' AND IsDeleted = 0 LIMIT 1;

-- ---------------------------------------------------------------------------
-- 站点参数配置（sys_site_config + 站点参数菜单/按钮权限；管理员改完即生效，无需重启）
-- ---------------------------------------------------------------------------
CREATE TABLE `sys_site_config` (
  `Id` bigint(20) NOT NULL COMMENT '雪花ID主键',
  `ConfigKey` varchar(50) NOT NULL COMMENT '参数键',
  `ConfigValue` varchar(500) DEFAULT NULL COMMENT '参数值',
  `Remark` varchar(200) DEFAULT NULL COMMENT '备注',
  `CreatedAt` datetime NOT NULL COMMENT '创建时间',
  `UpdatedAt` datetime NOT NULL COMMENT '更新时间',
  `IsDeleted` tinyint(1) NOT NULL COMMENT '软删除标记',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci COMMENT='站点参数';

INSERT INTO sys_menu (Id, ParentId, MenuName, MenuType, Path, Component, PermsCode, Icon, OrderNum, Visible, Status, CreatedAt, UpdatedAt, IsDeleted)
VALUES
(5280300000000000011, 903770869673889792, '站点参数', 1, '/system/siteconfig', 'system/siteconfig/index', NULL, NULL, 5, 1, 1, NOW(), NOW(), 0),
(5280300000000000012, 5280300000000000011, '站点参数保存', 2, NULL, NULL, 'sys:site:manage', NULL, 1, 1, 1, NOW(), NOW(), 0);

INSERT INTO sys_role_menu (Id, RoleId, MenuId, CreatedAt, UpdatedAt, IsDeleted)
SELECT 5280300000000000013, Id, 5280300000000000011, NOW(), NOW(), 0
FROM sys_role WHERE RoleCode = 'admin' AND IsDeleted = 0 LIMIT 1;

INSERT INTO sys_role_menu (Id, RoleId, MenuId, CreatedAt, UpdatedAt, IsDeleted)
SELECT 5280300000000000014, Id, 5280300000000000012, NOW(), NOW(), 0
FROM sys_role WHERE RoleCode = 'admin' AND IsDeleted = 0 LIMIT 1;

-- ============================================================================
-- 第二部分：主从复制（读写分离）配置模板 —— 仅说明，不自动执行
-- ============================================================================
-- 应用层已支持读写分离（ConnectionStrings:Slaves + ReadWriteSplitting:Enabled），
-- 数据库侧需先完成主从复制。以下为 MySQL 8 主从配置要点：
--
-- 【主库 my.cnf】
--   [mysqld]
--   server-id  = 1                -- 多从库时每台唯一
--   log_bin    = mysql-bin
--   binlog_format = ROW
--
-- 【主库创建复制账号并锁表导出】
--   CREATE USER 'repl'@'%' IDENTIFIED BY '替换为强密码';
--   GRANT REPLICATION SLAVE ON *.* TO 'repl'@'%';
--   FLUSH TABLES WITH READ LOCK;
--   SHOW MASTER STATUS;           -- 记录 File 与 Position
--   UNLOCK TABLES;
--
-- 【从库 my.cnf】
--   [mysqld]
--   server-id  = 2                -- 多从库依次 3、4…不可重复
--   relay-log  = relay-bin
--   read_only  = 1                -- 从库只读
--
-- 【从库执行】先在从库导入本脚本（不带本段），再开启复制：
--   CHANGE MASTER TO
--     MASTER_HOST='主库IP',
--     MASTER_PORT=3306,
--     MASTER_USER='repl',
--     MASTER_PASSWORD='替换为强密码',
--     MASTER_LOG_FILE='mysql-bin.000001',   -- SHOW MASTER STATUS 记录的值
--     MASTER_LOG_POS=xxxx;                  -- SHOW MASTER STATUS 记录的值
--   START SLAVE;
--   SHOW SLAVE STATUS\G   -- Slave_IO_Running 与 Slave_SQL_Running 均为 Yes 即成功
--
-- 【验证】主库执行 SHOW PROCESSLIST; 从库 SHOW SLAVE STATUS\G
-- 【应用侧】把从库连接串填入各服务 appsettings.json 的 ConnectionStrings:Slaves，
--           并将 ReadWriteSplitting:Enabled 设为 true，重启服务生效。
-- ============================================================================
