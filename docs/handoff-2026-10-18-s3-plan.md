# 交接：2026-10-18（S2 已推送，S3 方案待批准）

> 当前交接为本份。之前 S2 交接保留为实施当时的历史。S3 入口为 `specs/0004-s3-rollback.md`，工程约束仍以根目录 AGENTS.md 为准。

## 状态

- 分支仍为 `feat/s1-fixed64-tests`，没有切换、重置、合并或创建 worktree。
- 用户明确要求提交推送 S2，已执行：`7d9396c3b1d9e854a3c1816d684b44b6de33e3ff`（`feat: add S2 deterministic arena simulation and headless replay`）。
- `git push origin feat/s1-fixed64-tests` 成功；`git ls-remote origin refs/heads/feat/s1-fixed64-tests` 返回同一完整 hash，已确认远端交付。
- 提交前 `.ai/verify.sh` 629 项全部通过，0 warnings / 0 errors。原先所有 S2 代码、测试、规格和证据已包含在该提交。
- 提交后仅准备 S3 方案和交接，并更新 AGENTS.md 的文档索引；这些文档未另行提交或推送。
- `Arena.Rollback` 仍只有原有 AssemblyMarker。没有新建 RollbackSession、V4/V5、协议或模拟网络代码。

## S3 提案要点（未批准）

- 用户亲写生产调度：输入延迟、预测、连续确认、最早错帧、回滚/重模拟请求和停等。AI 不把这些决策搬到辅助类里。
- AI 阶段 A/B 可准备空骨架、带帧标签存储、纯请求执行器、协议、可靠输入/模拟网络、固定脚本 V4 与 V5 harness。
- 请求模型 SaveState / LoadState / Advance 保留 S2 的状态帧语义：状态 F 在输入 F 前，执行后比较 H(F+1)。
- 默认 delay=2、max prediction=8、history=256、请求缓冲=32、包输入块=32；快速 V5 16 seeds，完整 1000 seeds。这些是待用户批准的本项目默认值。
- 只预测远端，本地缺少实际输入时可纠正历史但不能新增游戏步。捕获 M 帧、delay=d 时目标状态帧 N=M+d；终点后不得输出 Advance(N)。
- 可靠输入使用连续实际前沿 ACK、冗余和末包重发；网络 tick 独立于游戏帧，停等仍收发。

## 已核查与未核查

- 查阅 GGPO 开发指南及 GGRS 请求/SyncTest/主循环官方文档；没有引入第三方库。
- 独立只读审查发现尾部纠正缺少停止新增步的明确条件；已补齐本地实际输入前置条件和专门测试，原审查者复核通过。
- 设计算术检查确认 d=0/2/4、M=2400 对应目标输入 0..2399 / 2401 / 2403，终点 N 分别 2400/2402/2404 且没有 N 的本地实际输入；请求默认容量能容纳 2×8+3 项，历史容量满足 8+4+2。
- 这些不是 S3 动态验收，V4/V5 尚未运行，S3 仍未实施。Unity、IL2CPP、WebGL 和手感也未验证。

## 下一步

1. 用户审阅并批准 `0004-s3-rollback.md` 后再实施 A/B，不能把本轮“准备方案”当作实施授权。
2. 基础设施/固定脚本 V4/网络原语测试先变绿，核心行为测试以 HumanTodo 明确保留红灯，依赖调度的集成测试单独列为 RequiresRollbackScheduler；原有乘法不得重新排除。
3. 交给用户填写 `src/Arena.Rollback/RollbackSession.cs`，AI 只解释失败与审查。用户完成后移除所有待实现过滤，运行完整 1000 seeds 再判 S3 完成。
4. 用户乘法仍保留；真实网络、Unity 视图与时间节奏控制在后续阶段。

## 遗留

没有 S3 临时实现、运行中服务或待清理的实验代码；工作区只保留本轮方案与文档索引变化。后续提交、推送、切分支均仍需用户当次明确授权。
