# rollback-arena（暂名）

一个能在浏览器里联机的 1v1 3D 擂台对战小游戏，用**确定性模拟 + 回滚**做网络同步（GGPO 那一类做法）。

这也是一次 AI 编程工作流的完整实践：从定点数学到回滚调度，每一层都配有能自动证明「它是确定的」的检查，并且会如实记录 AI 做了什么、我做了什么。

- 方案：[`docs/specs/0001-rollback-arena.md`](docs/specs/0001-rollback-arena.md)
- 状态：**S0、S1、S2 已完成 .NET 验收**。S2 有可运行的无头对战和输入回放；Unity 表现层与联网回滚尚未实施。

S1 提供纯整数的 Q32.32 定点数学：饱和乘除、平方根、`FixVector3`、弧度 Sin/Cos 查找表和可快照的确定性随机数。`Fixed64.Mul` 由人实现，其余数学及验证由 AI 辅助完成。

需要 `global.json` 指定的 .NET SDK 和 Python 3（仅标准库，用于查找表一致性检查）。在仓库根目录运行：

```sh
.ai/verify.sh
```

检查包含构建、全部测试和生成表一致性，不排除乘法。当前 **629 项全部通过**：S1 的 427 项加上 S2 的 202 项场景、边界、快照、V2 和工具测试。数学模糊测试各含百万组输入；跨 Unity / IL2CPP / WebGL 的检查属于后续 V6。

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
- [当前交接：S2 完成，下一步 S3](docs/handoff-2026-10-18-s2.md)
