// ⑳分布式安装器：原生路线移除口径。
// 原生安装（Route="native"）在本轮整体移除，安装器收敛为容器一条路。
// 本文件只保留迁移说明与旧配置兼容读取：旧 state 文件里 route=native 的记录按 docker 处理。
namespace TaoAdmin.Deployer.Core.Distributed;

public static class NativeRouteRemoval
{
    /// <summary>旧配置兼容：native 一律映射为 docker（容器路线）。</summary>
    public static string NormalizeRoute(string? route) =>
        string.Equals(route, "docker", StringComparison.OrdinalIgnoreCase) ? "docker" : "docker";
}
