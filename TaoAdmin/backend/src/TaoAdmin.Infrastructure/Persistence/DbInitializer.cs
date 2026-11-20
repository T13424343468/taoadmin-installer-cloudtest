using TaoAdmin.Domain.Entities;
using TaoAdmin.Common.Snowflake;
using SqlSugar;

namespace TaoAdmin.Infrastructure.Persistence;

/// <summary>
/// 数据库初始化：CodeFirst 建表 + 首次种子数据
/// （admin 账号、内置角色、菜单/按钮权限、接口资源、监控中心菜单）。
/// </summary>
public static class DbInitializer
{
    /// <summary>默认管理员账号</summary>
    public const string AdminUserName = "admin";
    /// <summary>默认管理员密码（首次登录后请修改）</summary>
    public const string AdminPassword = "Admin@123";

    public static void Initialize(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        db.CodeFirst.InitTables(typeof(SysUser), typeof(SysRole), typeof(SysMenu), typeof(SysUserRole), typeof(SysRoleMenu), typeof(SysRoleApi), typeof(SysApi),
            typeof(SysTenant), typeof(SysMessage), typeof(SysWorkflow), typeof(SysWorkflowInstance), typeof(SysWorkflowTask),
            typeof(SysDictType), typeof(SysDictData), typeof(SysOperationLog), typeof(SysSiteConfig), typeof(SysAnnouncement),
            typeof(TaoAdmin.Domain.Entities.SysFile));

        if (db.Queryable<SysUser>().Count() == 0)
            SeedBase(db, snowflake);

        // 新功能种子（内部逐项幂等）：多租户演示数据、站内消息、工作流模板与模拟审批数据、数据字典
        SeedTenants(db, snowflake);
        if (db.Queryable<SysMessage>().Count() == 0)
            SeedMessages(db, snowflake);
        SeedWorkflows(db, snowflake);
        SeedDicts(db, snowflake);
        SeedSiteConfigMenu(db, snowflake);
        SeedOnlineUsersMenu(db, snowflake);
        SeedAnnouncementMenu(db, snowflake);
        SeedPwdPolicyMenu(db, snowflake);
        SeedTenantMenu(db, snowflake);
        SeedWorkflowMenu(db, snowflake);
        SeedMessageMenu(db, snowflake);
        SeedAuditMenus(db, snowflake);
    }

    /// <summary>防护审计菜单与权限种子（幂等）：登录安全、审计日志、通知渠道（挂系统管理目录下）</summary>
    private static void SeedAuditMenus(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir == null) return;

        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");

        // 登录安全
        if (!db.Queryable<SysMenu>().Any(m => m.Component == "system/loginsecurity/index"))
        {
            var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "登录安全", MenuType = 1, Path = "/system/loginsecurity", Component = "system/loginsecurity/index", OrderNum = 11, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
            var perm = new SysMenu { Id = snowflake.NextId(), ParentId = menu.Id, MenuName = "登录安全保存", MenuType = 2, PermsCode = "sys:audit:manage", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
            db.Insertable(new List<SysMenu> { menu, perm }).ExecuteCommand();
            if (adminRole != null)
                db.Insertable(new List<SysRoleMenu>
                {
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now },
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = now, UpdatedAt = now }
                }).ExecuteCommand();
        }

        // 审计日志
        if (!db.Queryable<SysMenu>().Any(m => m.Component == "system/auditlog/index"))
        {
            var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "审计日志", MenuType = 1, Path = "/system/auditlog", Component = "system/auditlog/index", OrderNum = 12, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
            var perm = new SysMenu { Id = snowflake.NextId(), ParentId = menu.Id, MenuName = "审计日志管理", MenuType = 2, PermsCode = "sys:audit:manage", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
            db.Insertable(new List<SysMenu> { menu, perm }).ExecuteCommand();
            if (adminRole != null)
                db.Insertable(new List<SysRoleMenu>
                {
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now },
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = now, UpdatedAt = now }
                }).ExecuteCommand();
        }

        // 通知渠道
        if (!db.Queryable<SysMenu>().Any(m => m.Component == "system/notifychannel/index"))
        {
            var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "通知渠道", MenuType = 1, Path = "/system/notifychannel", Component = "system/notifychannel/index", OrderNum = 13, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
            var perm = new SysMenu { Id = snowflake.NextId(), ParentId = menu.Id, MenuName = "通知渠道保存", MenuType = 2, PermsCode = "sys:audit:manage", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
            db.Insertable(new List<SysMenu> { menu, perm }).ExecuteCommand();
            if (adminRole != null)
                db.Insertable(new List<SysRoleMenu>
                {
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now },
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = now, UpdatedAt = now }
                }).ExecuteCommand();
        }
    }

    /// <summary>站点参数菜单与权限种子（逐项幂等：已有数据不重复插入，兼容老库增量升级）</summary>
    private static void SeedSiteConfigMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        // 菜单：挂在系统管理目录下（按组件路径判重；仅首次运行时与全量菜单一起插入的老库也补上）
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir != null && !db.Queryable<SysMenu>().Any(m => m.Component == "system/siteconfig/index"))
        {
            var siteMenu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "站点参数", MenuType = 1, Path = "/system/siteconfig", Component = "system/siteconfig/index", OrderNum = 5, Visible = true, Status = 1, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
            var perm = new SysMenu { Id = snowflake.NextId(), ParentId = siteMenu.Id, MenuName = "站点参数保存", MenuType = 2, PermsCode = "sys:site:manage", OrderNum = 1, Status = 1, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
            db.Insertable(new List<SysMenu> { siteMenu, perm }).ExecuteCommand();

            // 超管角色补授权（按角色编码定位，绑定新菜单）
            var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
            if (adminRole != null)
                db.Insertable(new List<SysRoleMenu>
                {
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = siteMenu.Id, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now },
                    new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now }
                }).ExecuteCommand();
        }
    }

    /// <summary>数据字典种子（平台公共字典 TenantId=0，全租户可用；按编码幂等）</summary>
    private static void SeedDicts(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        long NewId() => snowflake.NextId();

        void EnsureDict(string dictName, string dictCode, (string label, string value, int sort)[] items)
        {
            if (db.Queryable<SysDictType>().Any(t => t.DictCode == dictCode)) return;
            db.Insertable(new SysDictType { Id = NewId(), DictName = dictName, DictCode = dictCode, Status = 1, TenantId = 0, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
            db.Insertable(items.Select((i, idx) => new SysDictData
            {
                Id = NewId(), DictCode = dictCode, Label = i.label, Value = i.value, Sort = i.sort,
                Status = 1, TenantId = 0, CreatedAt = now, UpdatedAt = now
            }).ToList()).ExecuteCommand();
        }

        EnsureDict("性别", "gender", new[] { ("男", "M", 1), ("女", "F", 2), ("未知", "U", 3) });
        EnsureDict("通用状态", "status", new[] { ("启用", "1", 1), ("禁用", "0", 2) });
        EnsureDict("审批实例状态", "workflow_status", new[] { ("进行中", "1", 1), ("已通过", "2", 2), ("已驳回", "3", 3) });
    }

    /// <summary>公告通知菜单与权限种子（逐项幂等：已有数据不重复插入，兼容老库增量升级）</summary>
    private static void SeedAnnouncementMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir == null || db.Queryable<SysMenu>().Any(m => m.Component == "system/announcement/index")) return;

        var annMenu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "公告通知", MenuType = 1, Path = "/system/announcement", Component = "system/announcement/index", OrderNum = 6, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var perm = new SysMenu { Id = snowflake.NextId(), ParentId = annMenu.Id, MenuName = "公告管理", MenuType = 2, PermsCode = "sys:notice:manage", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysMenu> { annMenu, perm }).ExecuteCommand();

        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
        if (adminRole != null)
            db.Insertable(new List<SysRoleMenu>
            {
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = annMenu.Id, CreatedAt = now, UpdatedAt = now },
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = now, UpdatedAt = now }
            }).ExecuteCommand();
    }

    /// <summary>在线用户菜单与权限种子（逐项幂等：已有数据不重复插入，兼容老库增量升级）</summary>
    private static void SeedOnlineUsersMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        // 菜单：挂在系统监控目录下（按组件路径判重）
        var monDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统监控" && m.ParentId == 0);
        if (monDir == null || db.Queryable<SysMenu>().Any(m => m.Component == "monitor/online/index")) return;

        var onlineMenu = new SysMenu { Id = snowflake.NextId(), ParentId = monDir.Id, MenuName = "在线用户", MenuType = 1, Path = "/monitor/online", Component = "monitor/online/index", Icon = "Team", OrderNum = 3, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var kickPerm = new SysMenu { Id = snowflake.NextId(), ParentId = onlineMenu.Id, MenuName = "强制下线", MenuType = 2, PermsCode = "monitor:online:kick", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysMenu> { onlineMenu, kickPerm }).ExecuteCommand();

        // 超管角色补授权（按角色编码定位，绑定新菜单）
        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
        if (adminRole != null)
            db.Insertable(new List<SysRoleMenu>
            {
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = onlineMenu.Id, CreatedAt = now, UpdatedAt = now },
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = kickPerm.Id, CreatedAt = now, UpdatedAt = now }
            }).ExecuteCommand();
    }

    /// <summary>密码策略菜单与权限种子（逐项幂等：已有数据不重复插入，兼容老库增量升级）</summary>
    private static void SeedPwdPolicyMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir == null || db.Queryable<SysMenu>().Any(m => m.Component == "system/pwdpolicy/index")) return;

        var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "密码策略", MenuType = 1, Path = "/system/pwdpolicy", Component = "system/pwdpolicy/index", OrderNum = 7, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var perm = new SysMenu { Id = snowflake.NextId(), ParentId = menu.Id, MenuName = "密码策略保存", MenuType = 2, PermsCode = "sys:pwdpolicy:manage", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysMenu> { menu, perm }).ExecuteCommand();

        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
        if (adminRole != null)
            db.Insertable(new List<SysRoleMenu>
            {
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now },
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = now, UpdatedAt = now }
            }).ExecuteCommand();
    }

    /// <summary>多租户管理菜单与权限种子（逐项幂等：已有数据不重复插入，兼容老库增量升级）</summary>
    private static void SeedTenantMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir == null || db.Queryable<SysMenu>().Any(m => m.Component == "system/tenant/index")) return;

        var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "租户管理", MenuType = 1, Path = "/system/tenant", Component = "system/tenant/index", OrderNum = 8, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var perm = new SysMenu { Id = snowflake.NextId(), ParentId = menu.Id, MenuName = "租户管理操作", MenuType = 2, PermsCode = "sys:tenant:manage", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysMenu> { menu, perm }).ExecuteCommand();

        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
        if (adminRole != null)
            db.Insertable(new List<SysRoleMenu>
            {
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now },
                new() { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = perm.Id, CreatedAt = now, UpdatedAt = now }
            }).ExecuteCommand();
    }

    /// <summary>工作流菜单种子（幂等；接口未挂权限码，不摆假按钮权限）</summary>
    private static void SeedWorkflowMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir == null || db.Queryable<SysMenu>().Any(m => m.Component == "system/workflow/index")) return;

        var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "工作流审批", MenuType = 1, Path = "/system/workflow", Component = "system/workflow/index", OrderNum = 9, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(menu).ExecuteCommand();

        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
        if (adminRole != null)
            db.Insertable(new SysRoleMenu { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
    }

    /// <summary>站内消息菜单种子（幂等；接口未挂权限码，不摆假按钮权限）</summary>
    private static void SeedMessageMenu(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        var sysDir = db.Queryable<SysMenu>().First(m => m.MenuName == "系统管理" && m.ParentId == 0);
        if (sysDir == null || db.Queryable<SysMenu>().Any(m => m.Component == "system/message/index")) return;

        var menu = new SysMenu { Id = snowflake.NextId(), ParentId = sysDir.Id, MenuName = "站内消息", MenuType = 1, Path = "/system/message", Component = "system/message/index", OrderNum = 10, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(menu).ExecuteCommand();

        var adminRole = db.Queryable<SysRole>().First(r => r.RoleCode == "admin");
        if (adminRole != null)
            db.Insertable(new SysRoleMenu { Id = snowflake.NextId(), RoleId = adminRole.Id, MenuId = menu.Id, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
    }

    /// <summary>基础种子：内置角色、admin 账号、菜单/按钮权限、接口资源（仅首次运行）</summary>
    private static void SeedBase(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        long NewId() => snowflake.NextId();

        // ---- 内置角色 ----
        var adminRole = new SysRole { Id = NewId(), RoleName = "超级管理员", RoleCode = "admin", Status = 1, Remark = "内置角色：拥有全部权限", CreatedAt = now, UpdatedAt = now };
        var commonRole = new SysRole { Id = NewId(), RoleName = "普通用户", RoleCode = "common", Status = 1, Remark = "内置角色：基础查看权限", CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysRole> { adminRole, commonRole }).ExecuteCommand();

        // ---- 内置账号 admin / Admin@123 ----
        var adminUser = new SysUser
        {
            Id = NewId(), UserName = AdminUserName,
            Password = BCrypt.Net.BCrypt.HashPassword(AdminPassword),
            NickName = "系统管理员", Status = 1, CreatedAt = now, UpdatedAt = now
        };
        db.Insertable(adminUser).ExecuteCommand();
        db.Insertable(new SysUserRole { Id = NewId(), UserId = adminUser.Id, RoleId = adminRole.Id, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();

        // ---- 菜单树：系统管理 / 任务中心 / 系统监控 ----
        var allMenus = new List<SysMenu>();
        var sysDir = new SysMenu { Id = NewId(), ParentId = 0, MenuName = "系统管理", MenuType = 0, Path = "/system", Icon = "Setting", OrderNum = 1, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var taskDir = new SysMenu { Id = NewId(), ParentId = 0, MenuName = "任务中心", MenuType = 0, Path = "/task", Icon = "Timer", OrderNum = 2, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var monDir = new SysMenu { Id = NewId(), ParentId = 0, MenuName = "系统监控", MenuType = 0, Path = "/monitor", Icon = "Monitor", OrderNum = 3, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        allMenus.AddRange(new[] { sysDir, taskDir, monDir });

        var userMenu = new SysMenu { Id = NewId(), ParentId = sysDir.Id, MenuName = "用户管理", MenuType = 1, Path = "/system/user", Component = "system/user/index", OrderNum = 1, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var roleMenu = new SysMenu { Id = NewId(), ParentId = sysDir.Id, MenuName = "角色管理", MenuType = 1, Path = "/system/role", Component = "system/role/index", OrderNum = 2, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var menuMenu = new SysMenu { Id = NewId(), ParentId = sysDir.Id, MenuName = "菜单管理", MenuType = 1, Path = "/system/menu", Component = "system/menu/index", OrderNum = 3, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var apiMenu = new SysMenu { Id = NewId(), ParentId = sysDir.Id, MenuName = "接口管理", MenuType = 1, Path = "/system/api", Component = "system/api/index", OrderNum = 4, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var jobMenu = new SysMenu { Id = NewId(), ParentId = taskDir.Id, MenuName = "定时任务", MenuType = 1, Path = "/task/job", Component = "job/index", OrderNum = 1, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var svcMenu = new SysMenu { Id = NewId(), ParentId = monDir.Id, MenuName = "服务管理中心", MenuType = 1, Path = "/monitor/service", Component = "monitor/service/index", OrderNum = 1, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        var logMenu = new SysMenu { Id = NewId(), ParentId = monDir.Id, MenuName = "日志管理中心", MenuType = 1, Path = "/monitor/log", Component = "monitor/log/index", OrderNum = 2, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        // 文件管理：页面在基础框架内，数据通过接口调用独立的 TaoFiles 文件服务
        var fileMenu = new SysMenu { Id = NewId(), ParentId = 0, MenuName = "文件管理", MenuType = 1, Path = "/file", Component = "file/index", Icon = "FolderOpen", OrderNum = 4, Visible = true, Status = 1, CreatedAt = now, UpdatedAt = now };
        allMenus.AddRange(new[] { userMenu, roleMenu, menuMenu, apiMenu, jobMenu, svcMenu, logMenu, fileMenu });

        // ---- 按钮权限 ----
        allMenus.AddRange(new[]
        {
            new SysMenu { Id = NewId(), ParentId = userMenu.Id, MenuName = "用户新增", MenuType = 2, PermsCode = "sys:user:add", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = userMenu.Id, MenuName = "用户修改", MenuType = 2, PermsCode = "sys:user:edit", OrderNum = 2, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = userMenu.Id, MenuName = "用户删除", MenuType = 2, PermsCode = "sys:user:delete", OrderNum = 3, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = userMenu.Id, MenuName = "重置密码", MenuType = 2, PermsCode = "sys:user:resetPwd", OrderNum = 4, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = userMenu.Id, MenuName = "分配角色", MenuType = 2, PermsCode = "sys:user:assignRole", OrderNum = 5, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = roleMenu.Id, MenuName = "角色新增", MenuType = 2, PermsCode = "sys:role:add", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = roleMenu.Id, MenuName = "角色修改", MenuType = 2, PermsCode = "sys:role:edit", OrderNum = 2, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = roleMenu.Id, MenuName = "角色删除", MenuType = 2, PermsCode = "sys:role:delete", OrderNum = 3, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = roleMenu.Id, MenuName = "分配权限", MenuType = 2, PermsCode = "sys:role:assign", OrderNum = 4, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = menuMenu.Id, MenuName = "菜单新增", MenuType = 2, PermsCode = "sys:menu:add", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = menuMenu.Id, MenuName = "菜单修改", MenuType = 2, PermsCode = "sys:menu:edit", OrderNum = 2, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = menuMenu.Id, MenuName = "菜单删除", MenuType = 2, PermsCode = "sys:menu:delete", OrderNum = 3, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = apiMenu.Id, MenuName = "接口新增", MenuType = 2, PermsCode = "sys:api:add", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = apiMenu.Id, MenuName = "接口修改", MenuType = 2, PermsCode = "sys:api:edit", OrderNum = 2, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = apiMenu.Id, MenuName = "接口删除", MenuType = 2, PermsCode = "sys:api:delete", OrderNum = 3, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = jobMenu.Id, MenuName = "任务新增", MenuType = 2, PermsCode = "sys:job:add", OrderNum = 1, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = jobMenu.Id, MenuName = "任务修改", MenuType = 2, PermsCode = "sys:job:edit", OrderNum = 2, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = jobMenu.Id, MenuName = "任务删除", MenuType = 2, PermsCode = "sys:job:delete", OrderNum = 3, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = jobMenu.Id, MenuName = "任务暂停", MenuType = 2, PermsCode = "sys:job:pause", OrderNum = 4, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = jobMenu.Id, MenuName = "任务恢复", MenuType = 2, PermsCode = "sys:job:resume", OrderNum = 5, Status = 1, CreatedAt = now, UpdatedAt = now },
            new SysMenu { Id = NewId(), ParentId = jobMenu.Id, MenuName = "立即执行", MenuType = 2, PermsCode = "sys:job:run", OrderNum = 6, Status = 1, CreatedAt = now, UpdatedAt = now }
        });
        db.Insertable(allMenus).ExecuteCommand();

        // ---- 接口资源 ----
        var apis = new List<SysApi>
        {
            new() { Id = NewId(), Name = "用户分页查询", Path = "/api/identity/users", HttpMethod = "GET", PermsCode = "sys:user:list", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "用户新增", Path = "/api/identity/users", HttpMethod = "POST", PermsCode = "sys:user:add", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "用户修改", Path = "/api/identity/users/{id}", HttpMethod = "PUT", PermsCode = "sys:user:edit", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "用户删除", Path = "/api/identity/users/{id}", HttpMethod = "DELETE", PermsCode = "sys:user:delete", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "角色列表", Path = "/api/identity/roles/all", HttpMethod = "GET", PermsCode = "sys:role:list", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "任务分页查询", Path = "/api/job/jobs", HttpMethod = "GET", PermsCode = "sys:job:list", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "服务健康查询", Path = "/api/monitor/services", HttpMethod = "GET", PermsCode = "monitor:service:list", Status = 1, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Name = "日志查询", Path = "/api/monitor/logs", HttpMethod = "GET", PermsCode = "monitor:log:list", Status = 1, CreatedAt = now, UpdatedAt = now }
        };
        db.Insertable(apis).ExecuteCommand();

        // ---- 超管绑定全部菜单与接口 ----
        db.Insertable(allMenus.Select(m => new SysRoleMenu { Id = NewId(), RoleId = adminRole.Id, MenuId = m.Id, CreatedAt = now, UpdatedAt = now }).ToList()).ExecuteCommand();
        db.Insertable(apis.Select(a => new SysRoleApi { Id = NewId(), RoleId = adminRole.Id, ApiId = a.Id, CreatedAt = now, UpdatedAt = now }).ToList()).ExecuteCommand();
    }

    // ==========================================================================
    // 多租户 / 消息队列 / 工作流 —— 演示与模拟测试数据（幂等种子）
    // ==========================================================================

    /// <summary>租户与租户演示账号（逐项幂等：按编码/账号判断是否已存在）</summary>
    private static void SeedTenants(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        long NewId() => snowflake.NextId();

        long GetOrInsertTenant(string name, string code, string contact, string remark)
        {
            var exist = db.Queryable<SysTenant>().First(t => t.TenantCode == code);
            if (exist != null) return exist.Id;
            var t = new SysTenant { Id = NewId(), TenantName = name, TenantCode = code, Status = 1, Contact = contact, Remark = remark, CreatedAt = now, UpdatedAt = now };
            db.Insertable(t).ExecuteCommand();
            return t.Id;
        }

        var eastId = GetOrInsertTenant("华东分公司", "east", "陈华东", "演示租户：华东大区");
        var southId = GetOrInsertTenant("华南分公司", "south", "林华南", "演示租户：华南大区");

        if (db.Queryable<SysUser>().Any(u => u.UserName == "east01"))
            return; // 演示账号已存在，跳过整个演示数据块

        // 租户审批角色：组长 / 经理（平台层通用，供工作流审批人使用）
        var commonRole = db.Queryable<SysRole>().First(r => r.RoleCode == "common");
        var leaderRole = new SysRole { Id = NewId(), RoleName = "组长", RoleCode = "group_leader", Status = 1, Remark = "工作流审批角色：第一级审批", CreatedAt = now, UpdatedAt = now };
        var managerRole = new SysRole { Id = NewId(), RoleName = "经理", RoleCode = "manager", Status = 1, Remark = "工作流审批角色：第二级审批", CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysRole> { leaderRole, managerRole }).ExecuteCommand();

        // 各租户演示账号（密码均为 Tenant@123）
        string Hash() => BCrypt.Net.BCrypt.HashPassword("Tenant@123");
        var eastLeader = new SysUser { Id = NewId(), TenantId = eastId, UserName = "east_leader", Password = Hash(), NickName = "李组长（华东）", Email = "east.leader@demo.com", Status = 1, CreatedAt = now, UpdatedAt = now };
        var eastStaff = new SysUser { Id = NewId(), TenantId = eastId, UserName = "east01", Password = Hash(), NickName = "张员工（华东）", Email = "east01@demo.com", Status = 1, CreatedAt = now, UpdatedAt = now };
        var southManager = new SysUser { Id = NewId(), TenantId = southId, UserName = "south_manager", Password = Hash(), NickName = "王经理（华南）", Email = "south.manager@demo.com", Status = 1, CreatedAt = now, UpdatedAt = now };
        var southStaff = new SysUser { Id = NewId(), TenantId = southId, UserName = "south01", Password = Hash(), NickName = "赵员工（华南）", Email = "south01@demo.com", Status = 1, CreatedAt = now, UpdatedAt = now };
        db.Insertable(new List<SysUser> { eastLeader, eastStaff, southManager, southStaff }).ExecuteCommand();

        // 绑定角色：组长 → 组长角色；经理 → 经理角色；员工 → 普通用户
        var ur = new List<SysUserRole>
        {
            new() { Id = NewId(), UserId = eastLeader.Id, RoleId = leaderRole.Id, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), UserId = eastStaff.Id, RoleId = commonRole.Id, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), UserId = southManager.Id, RoleId = managerRole.Id, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), UserId = southStaff.Id, RoleId = commonRole.Id, CreatedAt = now, UpdatedAt = now },
        };
        db.Insertable(ur).ExecuteCommand();
    }

    /// <summary>站内消息模拟数据（幂等）</summary>
    private static void SeedMessages(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        long NewId() => snowflake.NextId();
        db.Insertable(new List<SysMessage>
        {
            new() { Id = NewId(), Title = "欢迎使用 TaoAdmin", Content = "系统已内置消息队列演示：新增用户后会自动收到欢迎消息。", Topic = "system.notice", ReceiverId = 0, IsRead = false, CreatedAt = now, UpdatedAt = now },
            new() { Id = NewId(), Title = "系统更新通知", Content = "本次更新新增：消息队列、工作流引擎、多租户支持。", Topic = "system.notice", ReceiverId = 0, IsRead = false, CreatedAt = now.AddHours(-2), UpdatedAt = now.AddHours(-2) },
            new() { Id = NewId(), Title = "数据备份完成", Content = "每日数据库自动备份已完成，无异常。", Topic = "system.notice", ReceiverId = 0, IsRead = true, CreatedAt = now.AddDays(-1), UpdatedAt = now.AddDays(-1) },
        }).ExecuteCommand();
    }

    /// <summary>
    /// 工作流模板 + 模拟审批数据（幂等）。
    /// 模拟数据覆盖全部状态：待第一级审批 / 待第二级审批 / 已通过 / 已驳回。
    /// 演示账号密码均为 Tenant@123。
    /// </summary>
    private static void SeedWorkflows(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
    {
        var now = DateTime.Now;
        long NewId() => snowflake.NextId();

        // ---- 流程模板（平台层，租户 0 全局可用；按编码幂等） ----
        var leave = db.Queryable<SysWorkflow>().First(w => w.WorkflowCode == "leave_audit");
        if (leave == null)
        {
            leave = new SysWorkflow
            {
                Id = NewId(), WorkflowName = "请假审批", WorkflowCode = "leave_audit", Status = 1,
                Description = "员工请假审批：先组长审批，再经理审批",
                NodesJson = "[{\"name\":\"组长审批\",\"approverRole\":\"group_leader\"},{\"name\":\"经理审批\",\"approverRole\":\"manager\"}]",
                TenantId = 0, CreatedAt = now, UpdatedAt = now
            };
            db.Insertable(leave).ExecuteCommand();
        }
        var expense = db.Queryable<SysWorkflow>().First(w => w.WorkflowCode == "expense_audit");
        if (expense == null)
        {
            expense = new SysWorkflow
            {
                Id = NewId(), WorkflowName = "报销审批", WorkflowCode = "expense_audit", Status = 1,
                Description = "费用报销：经理一级审批",
                NodesJson = "[{\"name\":\"经理审批\",\"approverRole\":\"manager\"}]",
                TenantId = 0, CreatedAt = now, UpdatedAt = now
            };
            db.Insertable(expense).ExecuteCommand();
        }

        // 模拟实例已有数据则跳过（演示数据整体幂等）
        if (db.Queryable<SysWorkflowInstance>().Any())
            return;

        // 演示人物：张员工(华东)发起，李组长(华东)、王经理(华南)审批
        var zhang = db.Queryable<SysUser>().First(u => u.UserName == "east01");
        var li = db.Queryable<SysUser>().First(u => u.UserName == "east_leader");
        var wang = db.Queryable<SysUser>().First(u => u.UserName == "south_manager");
        if (zhang == null || li == null || wang == null) return;

        // ---- 模拟实例 1：进行中，待组长审批（华东租户） ----
        var i1 = new SysWorkflowInstance { Id = NewId(), WorkflowId = leave.Id, WorkflowName = leave.WorkflowName, BizTitle = "张员工的年假申请", Remark = "3 天年假，11-05 至 11-07", ApplicantId = zhang.Id, ApplicantName = zhang.NickName ?? "张员工", CurrentNodeIndex = 0, Status = 1, TenantId = zhang.TenantId, CreatedAt = now.AddHours(-3), UpdatedAt = now.AddHours(-3) };
        var t1 = new SysWorkflowTask { Id = NewId(), InstanceId = i1.Id, NodeName = "组长审批", NodeIndex = 0, AssigneeRoleCode = "group_leader", Status = 0, TenantId = zhang.TenantId, CreatedAt = now.AddHours(-3), UpdatedAt = now.AddHours(-3) };

        // ---- 模拟实例 2：进行中，组长已批，待经理审批（华东租户） ----
        var i2 = new SysWorkflowInstance { Id = NewId(), WorkflowId = leave.Id, WorkflowName = leave.WorkflowName, BizTitle = "张员工的事假申请", Remark = "1 天事假，10-28", ApplicantId = zhang.Id, ApplicantName = zhang.NickName ?? "张员工", CurrentNodeIndex = 1, Status = 1, TenantId = zhang.TenantId, CreatedAt = now.AddHours(-26), UpdatedAt = now.AddHours(-20) };
        var t2a = new SysWorkflowTask { Id = NewId(), InstanceId = i2.Id, NodeName = "组长审批", NodeIndex = 0, AssigneeRoleCode = "group_leader", Status = 1, Comment = "情况属实，同意", HandlerName = li.NickName ?? "李组长", HandleTime = now.AddHours(-20), TenantId = zhang.TenantId, CreatedAt = now.AddHours(-26), UpdatedAt = now.AddHours(-20) };
        var t2b = new SysWorkflowTask { Id = NewId(), InstanceId = i2.Id, NodeName = "经理审批", NodeIndex = 1, AssigneeRoleCode = "manager", Status = 0, TenantId = zhang.TenantId, CreatedAt = now.AddHours(-20), UpdatedAt = now.AddHours(-20) };

        // ---- 模拟实例 3：已走完全流程（华东租户） ----
        var i3 = new SysWorkflowInstance { Id = NewId(), WorkflowId = expense.Id, WorkflowName = expense.WorkflowName, BizTitle = "张员工的差旅报销", Remark = "差旅费 860 元，发票已交", ApplicantId = zhang.Id, ApplicantName = zhang.NickName ?? "张员工", CurrentNodeIndex = 0, Status = 2, FinishedAt = now.AddDays(-1), TenantId = zhang.TenantId, CreatedAt = now.AddDays(-2), UpdatedAt = now.AddDays(-1) };
        var t3 = new SysWorkflowTask { Id = NewId(), InstanceId = i3.Id, NodeName = "经理审批", NodeIndex = 0, AssigneeRoleCode = "manager", Status = 1, Comment = "票据齐全，同意报销", HandlerName = wang.NickName ?? "王经理", HandleTime = now.AddDays(-1), TenantId = zhang.TenantId, CreatedAt = now.AddDays(-2), UpdatedAt = now.AddDays(-1) };

        // ---- 模拟实例 4：已驳回（华南租户） ----
        var southUser = db.Queryable<SysUser>().First(u => u.UserName == "south01");
        if (southUser != null)
        {
            var i4 = new SysWorkflowInstance { Id = NewId(), WorkflowId = leave.Id, WorkflowName = leave.WorkflowName, BizTitle = "赵员工的调休申请", Remark = "调休 2 天", ApplicantId = southUser.Id, ApplicantName = southUser.NickName ?? "赵员工", CurrentNodeIndex = 1, Status = 3, FinishedAt = now.AddHours(-5), TenantId = southUser.TenantId, CreatedAt = now.AddDays(-1), UpdatedAt = now.AddHours(-5) };
            var t4a = new SysWorkflowTask { Id = NewId(), InstanceId = i4.Id, NodeName = "组长审批", NodeIndex = 0, AssigneeRoleCode = "group_leader", Status = 1, Comment = "转经理确认", HandlerName = "值班组长", HandleTime = now.AddHours(-10), TenantId = southUser.TenantId, CreatedAt = now.AddDays(-1), UpdatedAt = now.AddHours(-10) };
            var t4b = new SysWorkflowTask { Id = NewId(), InstanceId = i4.Id, NodeName = "经理审批", NodeIndex = 1, AssigneeRoleCode = "manager", Status = 2, Comment = "近期项目忙，建议下周再调休", HandlerName = wang.NickName ?? "王经理", HandleTime = now.AddHours(-5), TenantId = southUser.TenantId, CreatedAt = now.AddHours(-10), UpdatedAt = now.AddHours(-5) };
            db.Insertable(new List<SysWorkflowInstance> { i4 }).ExecuteCommand();
            db.Insertable(new List<SysWorkflowTask> { t4a, t4b }).ExecuteCommand();
        }

        db.Insertable(new List<SysWorkflowInstance> { i1, i2, i3 }).ExecuteCommand();
        db.Insertable(new List<SysWorkflowTask> { t1, t2a, t2b, t3 }).ExecuteCommand();
    }
}
