# 交接：2026-10-18（S3 A/B 完成，等待用户调度）

> 历史交接，记录 A/B 完成时的状态。当前入口已更新为 `handoff-2026-10-18-s3-actual-input.md`；项目规则仍看根目录 AGENTS.md。

## 状态

- 分支 `feat/s1-fixed64-tests`，HEAD `7d9396c3b1d9e854a3c1816d684b44b6de33e3ff`（S2，上一轮已推送并核对远端）。本轮读取 upstream..HEAD 为空；本轮未请求新的远端状态。
- 本轮没有提交、推送、切分支、合并或创建 worktree。
- 工作区保留上一轮未提交的 S3 方案/交接与索引修改，加上本轮 AI 实施的 A/B 代码、测试、配置和文档；用户乘法与所有 S2 逻辑/旧测试未改。
- 用户已批准方案并仅授权 A/B。`RollbackSession.cs` 的全部行为仍为 NotImplementedException，不得把本轮授权解读为允许 AI 写生产调度。
- `.ai/verify.sh`：678 passed（629 原有 + 49 新增），0 warnings/errors，12.05 秒；默认明确排除并列出调度依赖的 32 项。

## 已做与关键边界

| 主题 | 入口 |
| --- | --- |
| 已批准契约、阶段与 done-when | `docs/specs/0004-s3-rollback.md` |
| 用户实现说明与命令 | `docs/s3-human-guide.md` |
| 实际验证、14 项探针、独立审查修复 | `docs/verification/s3-preparation.md` |
| 固定标签存储/纯请求执行器/协议/V4 | `src/Arena.Rollback/` |
| 显式 tick 模拟网络、可靠实际输入搬运 | `src/Arena.Net/` |
| V5 oracle 与 CLI | `tools/headless/RollbackEquivalenceRunner.cs`、`RollbackCli.cs` |

`CaptureLocalInput` 返回映射后的 FramedInput，网络不再加 delay；映射实现仍由用户写。请求执行器只执行明确的 Save/Load/Step，不补请求或选回滚点。V4 是固定回退脚本，不能充当生产会话替代品。确认哈希只在完整纠正批次执行之后发布；不能根据刚收包推进的前沿发布旧预测状态。

## 已验证 / 尚未验证

- V4 完整录制 k=1/2/4/8；真实 CLI k=8 输出 `SYNC_TEST_OK frames=1000 distance=8 checksum=AFD2C47A3AAF7FF5`，退出 0。
- 帧标签、请求执行、编解码、包所有权、乱序/丢包/重复、ACK、末包重发及 oracle 自检通过；14 项故障探针均断言失败后原字节恢复，重新构建全绿。
- HumanTodo 16 个契约和 RequiresRollbackScheduler 16 个 V5 实际运行，32 个均只因 NotImplementedException 失败，非空用例、非编译错误。
- 完整 1000 seeds 恰好发现，未执行；非法 seed 数拒绝，退出非零且有失败 case。
- `--rollback-bots` 实际返回 3 和 `ROLLBACK_NOT_READY`，没有假成功。
- 独立审查的发布/退休时机和终点 NoLocalInput 两项发现已核对、修复并复核；检查过程文件 SHA-256 前后一致。
- 尚未验证真实生产调度、V5 成功、V5 完整 CI、V6/V7/V8/V9。S3 整体不能标完成。

## 下一步

1. 用户先读指南和契约测试，填写 `src/Arena.Rollback/RollbackSession.cs`。AI 只给失败证据/思路/审查；不要代写。
2. 契约通过后运行快速 16 seeds，再 `ARENA_V5_SEEDS=1000 dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~RollbackEquivalenceTests`。修调度由用户负责。
3. 完成 D/E 的真实 V5 反向探针、独立审查，移除两组分类及默认过滤，启用实际 1000 seeds CI；达到总方案 S3 done-when 再记完成。
4. 新提交/推送或分支操作仍需用户当次授权。

## 现场遗留

没有常驻服务或新 worktree。构建产物在忽略的 bin/obj 内；本轮探针脚本、审查冻结副本及测试日志放在系统临时目录，均不属于交付文件，可清理。实际源码不保留故障开关或探针；所有探针都未修改用户乘法及调度骨架。
