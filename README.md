# rollback-arena（暂名）

一个能在浏览器里联机的 1v1 3D 擂台对战小游戏，用**确定性模拟 + 回滚**做网络同步（GGPO 那一类做法）。

这也是一次 AI 编程工作流的完整实践：从定点数学到回滚调度，每一层都配有能自动证明「它是确定的」的检查，并且会如实记录 AI 做了什么、我做了什么。

- 方案：[`docs/specs/0001-rollback-arena.md`](docs/specs/0001-rollback-arena.md)
- 状态：**S0、S1 已完成**。下一阶段是 S2 无头游戏模拟；目前还没有可玩的游戏或 Unity 表现层。

S1 提供纯整数的 Q32.32 定点数学：饱和乘除、平方根、`FixVector3`、弧度 Sin/Cos 查找表和可快照的确定性随机数。`Fixed64.Mul` 由人实现，其余数学及验证由 AI 辅助完成。

需要 `global.json` 指定的 .NET SDK 和 Python 3（仅标准库，用于查找表一致性检查）。在仓库根目录运行：

```sh
.ai/verify.sh
```

检查包含构建、全部测试和生成表一致性，不排除乘法。S1 验收时总计 **427 个测试全部通过**，数学模糊测试各含百万组输入；这证明当前 .NET 运行时的行为，跨 Unity / IL2CPP / WebGL 的检查属于后续 V6。

- [数学 API、精度和溢出契约](docs/math-contracts.md)
- [S1 验收与反向探针证据](docs/verification/s1-math.md)
- [当前交接：S1 完成，准备 S2](docs/handoff-2026-10-18.md)
