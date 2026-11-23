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

## 2026-11-23 12:48 调度记录
- round3_enhance2 / round3_codegen2 两分身被中断（平台深度限制，无法再派子分身），工作区无遗留提交。
- 降级：主班（本班）接手，按增量提交顺序亲自落码：先功能四项（⑮⑰⑱→⑯），再生成器单表链路。
- 后续分身额度恢复（12 月）或平台限制解除后，剩余里程碑（生成器主从/增强、分布式续班）再并行补派。

## 2026-11-23 下午 第二期进度（round3-dist 分支增量提交）
- ✅ ①代理端 AgentService（commit f45d571）：四阶段执行（env→pull→start→health）、健康重试、心跳上报、坏阶段停住、清单载荷落盘 compose；IAgentCommandRunner/IAgentReporter 可注入。
- ✅ ②部署清单 DeploymentManifest（同上）：拓扑→每机清单→docker-compose.yml（分布式版）、跨机中间件地址注入（TAO_DB_HOST/TAO_CACHE_HOST/TAO_QUEUE_HOST）、发布端口顺延不冲突、镜像/端口/服务名校验、JSON 往返作代理载荷。
- ✅ ③④并行安装前端接入+中控界面（commit 3189ffe）：AgentNodeExecutor（POST /tasks + GET /progress 轮询，代理不可达转失败不抛出）+ ClusterInstallViewModel（四阶段只前进不回退、失败置红、整体均值）+ WPF PageCluster（机器清单/分配矩阵/进度实时滚动/并行安装/滚动升级坏机停住）。
- 测试：84/84 全绿（原 53 + 新增 31，含实效断言：坏阶段停住、健康重试打满、迟到进度不回退、代理不可达静默跳过）。
- 待续：⑤升级/回滚全链路细化（滚动升级编排已有，回滚动作待补）、⑥coverlet 覆盖账、代理端真实 HTTP 宿主（HttpListener 薄壳）与 Windows 代理包。
- ✅ ⑤升级/回滚全链路（commit 26e5d86）：UpgradeLedger 上次可用清单留痕 + ClusterRollback 逐台回滚（无账停住/坏机停住不盲滚/离线拒绝）。
- ✅ ⑥coverlet 覆盖账：coverlet.collector 6.0.2 接入，cobertura 出账——⑳Distributed 模块行覆盖 92.9%（1342/1444）；Deployer.Core 其余为单机版老引擎（9.8%），归存量补账班。
- 第二期收口：90/90 测全绿，round3-dist 分支 5 个增量提交（116cd1e→a1dddd0），未碰 main。
