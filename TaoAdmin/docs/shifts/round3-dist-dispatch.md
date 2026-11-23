# ⑳分布式安装器班 派工与进度账（2026-11-23 11:5x）

## 派工口径（老板拍板，不得改动）
- 安装器只出单机版+分布式版两版；每版内选目标环境（Windows/Linux）；分布式支持混合集群
- 分布式走中控+代理（第4档最细粒度）：机器×服务实例矩阵分配，支持加机器扩容、挪服务、统一升级滚动更新坏机停住
- Linux 接入：IP+SSH账号密码（或密钥），首次连接自动装容器环境+代理；凭据加密存储不落明文；Windows 优先试系统可选 SSH，不通提示手动装代理包
- 原生安装路线整体移除，收敛为容器一条路
- 大任务后台跑+进度可见；新代码分支覆盖100%（coverlet），实效断言；跨机场景专项测试场

## 已完成（第一期，commit c2e914f，分支 round3-dist）
- ClusterModels：节点/角色/分配矩阵/拓扑模型（含高可用可选开关）
- PlacementPlanner：推荐布局（中间件→数据机、网关前端→边缘机、应用实例轮转均摊）、挪服务 MoveRole、扩容再平衡 RebalanceRole
- CredentialVault：AES-GCM 加密托管，密文落盘不落明文，密钥文件权限保护
- SshBootstrapper：Linux 五步引导（连通→容器→代理→健康→在线），Windows 试 SSH 兜底提示手动装包
- ClusterOrchestrator：并行下发+进度账本、滚动升级逐台坏机停住
- NativeRouteRemoval：旧配置 native 一律映射 docker 的兼容口径
- 测试：53/53 全绿（含实效断言：SSH失败置Failed、坏机停住不继续、凭据不落明文、损坏文件抛错、密钥跨实例复用）

## 待续里程碑
1. 中控界面（机器清单/分配矩阵/进度实时滚动）——WinUI 或 Web
2. 代理端实现（AgentService：本机执行+状态上报）
3. 并行安装前端接入（装环境→拉镜像→起服务→健康检查四阶段进度）
4. 部署清单：docker-compose 分布式版、跨机网络配置
5. 分布式升级/回滚全链路
6. coverlet 覆盖账出账（当前为 xunit 实效断言，覆盖工具账待补）

## 环境备忘
- dotnet SDK：export DOTNET_ROOT=/root/workspace/.local/dotnet10 && export PATH=$DOTNET_ROOT:$PATH（global.json 要求 10.0.404）
- 测试：cd TaoAdmin/deploy/windows/TaoAdminDeployer && dotnet test tests/Deployer.Core.Tests/...
- 工作区：/root/workspace/installer-fix/wt-round3-dist（分支 round3-dist）
