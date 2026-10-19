# rollback-arena（暂名）

一个能在浏览器里联机的 1v1 3D 擂台对战小游戏，用**确定性模拟 + 回滚**做网络同步（GGPO 那一类做法）。

这也是一次 AI 编程工作流的完整实践：从定点数学到回滚调度，每一层都配有能自动证明「它是确定的」的检查，并且会如实记录 AI 做了什么、我做了什么。

- 方案：[`docs/specs/0001-rollback-arena.md`](docs/specs/0001-rollback-arena.md)
- 状态：**S0、S1、S2 已完成 .NET 验收**。S2 有可运行的无头对战和输入回放；S3 回滚核心与模拟网络也已完成 .NET 验收（真实 1000 seeds）；S4 Unity 方案待批准，表现层尚未实施。

S1 提供纯整数的 Q32.32 定点数学：饱和乘除、平方根、`FixVector3`、弧度 Sin/Cos 查找表和可快照的确定性随机数。`Fixed64.Mul` 由人实现，其余数学及验证由 AI 辅助完成。

需要 `global.json` 指定的 .NET SDK 和 Python 3（仅标准库，用于查找表一致性检查）。在仓库根目录运行：

```sh
.ai/verify.sh
```

检查包含构建、生成表一致性、**734 项 .NET 测试**及 16 项 Python 结果检查器自测，无分类排除。原有 629 项完整保留，默认 V5 为固定 16 seeds；完整 1000 seeds 另由下方命令验收。本机默认检查约 15 秒。数学模糊测试各含百万组输入；跨 Unity / IL2CPP / WebGL 的检查属于后续 V6。

运行机器人完整对战，保存输入与逐帧哈希：

```sh
dotnet run --project tools/headless -c Release -- --bots --record inputs.csv --hashes live.csv
dotnet run --project tools/headless -c Release -- --replay inputs.csv --hashes replay.csv
```

默认对战在第 1000 帧进入 MatchOver，比分 0:2，发生 12 次命中。两种运行生成的哈希日志应完全一致。输入文件以帧 0 开始，哈希行记录执行该步后的状态（帧号从 1 开始）。输出文件是运行产物，建议存到临时或工作目录，不提交进仓库。

- [数学 API、精度和溢出契约](docs/math-contracts.md)
- [S1 验收与反向探针证据](docs/verification/s1-math.md)
- [S2 定稿玩法](docs/specs/0002-gameplay.md)
- [S2 验收证据](docs/verification/s2-simulation.md)
- [S3 已批准方案与分工](docs/specs/0004-s3-rollback.md)
- [完整 RollbackSession 学习导读与数据流图](docs/s3-human-guide.md)
- [S3 A/B 准备阶段验证证据](docs/verification/s3-preparation.md)
- [实际输入推进代码导读与验证](docs/verification/s3-actual-input.md)
- [S3 验证与诊断准备收尾](docs/verification/s3-readiness.md)
- [S3 完整验收与反向探针](docs/verification/s3-rollback.md)
- [S4 Unity 表现层方案（待批准）](docs/specs/0005-s4-unity.md)
- [当前交接：S3 完成，等待 S4](docs/handoff-2026-10-19-s3-complete-s4-plan.md)

V4 固定脚本验证可独立运行：

```sh
dotnet run --project tools/headless -c Release -- --sync-test --distance 8
```

当前输出 `SYNC_TEST_OK frames=1000 distance=8 checksum=AFD2C47A3AAF7FF5`。运行真实双端回滚演示：

```sh
dotnet run --project tools/headless -c Release -- --rollback-bots --seed 0
```

seed=0 输出 `ROLLBACK_EQUIVALENT`，2400 帧、24 次纠正、180 步重模拟、最大回滚 8 帧，校验和 `8F598103D8A8BDCB`。双方每个确认帧均与独立离线 oracle 比较；delay=0/2/4、抖动/丢包/重复及不同推进速度由 V5 覆盖。

执行完整 V5：

```sh
bash tools/verify_v5.sh
```

该命令重新构建、在全新目录执行 0–999 共 1000 个种子，解析 TRX 核对实际执行、通过及唯一种子集合。本机已实际通过，输出 `V5_RESULTS_OK seeds=1000 passed=1000`；CI 已配置每次 push/PR 自动执行同一命令，尚无本轮远端执行记录。没有本轮提交或推送。

S3 原约定亲写调度，后由用户明确转交 AI 完成；学习导读保留输入、预测、纠正、退休的实现依据。当前需人工参与的是下一阶段的具体方案审批、Unity 环境与试玩，不能把 AI 完成的调度记为个人独立实现。
