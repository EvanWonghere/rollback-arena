# rollback-arena（暂名）

一个能在浏览器里联机的 1v1 3D 擂台对战小游戏，用**确定性模拟 + 回滚**做网络同步（GGPO 那一类做法）。

这也是一次 AI 编程工作流的完整实践：从定点数学到回滚调度，每一层都配有能自动证明「它是确定的」的检查，并且会如实记录 AI 做了什么、我做了什么。

- 方案：[`docs/specs/0001-rollback-arena.md`](docs/specs/0001-rollback-arena.md)
- 状态：S0（骨架与检查体系）已完成：dotnet 解决方案、架构守卫 V3、`.ai/verify.sh`、CI。
