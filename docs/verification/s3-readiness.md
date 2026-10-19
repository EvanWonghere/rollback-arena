# S3：保留手写核心时的 AI 验证准备收尾

2026-10-18。用户补充“除了需要我实现的部分，继续推进到下一批需要我实现的”，回滚调度归属的澄清尚待回复。本轮遵守原分工：**不改 RollbackSession**，只完成可独立推进的测试、诊断与验收工具。整体 S3 未完成。

## 已补齐

- `RollbackSessionEdgeTests.cs`：10 个 HumanTodo case，覆盖时间线预测种子、非零预测匹配、两种到达顺序、窗口缺口、纠正请求容量、未来帧拒绝及多个环形周期退休。当前 4 通过、6 待核心实现。
- `RollbackDiagnosticsTests.cs`：3 个故障注入 case，覆盖 capture/send、receive、advance。所有错误包含 seed/tick/peer/phase，RollbackException 的 Error/Frame 显式保留在外层文字中，原异常作为 InnerException 保留。
- `verify_v5_results.py`：验证真实 TRX 的 Completed 状态、实际执行与通过数、逐条 Passed，以及完整且不重复的种子集合。
- `verify_v5.sh`：重新构建，在每次新建的目录中执行 V5，再核对结果。默认1000 seeds，支持明确指定快速数量。
- CI 增加 `workflow_dispatch` 的完整 V5 入口；尚未把它启用为 push/PR 自动验收，因为核心仍未完成。本轮没有运行 GitHub Actions。

诊断不包裹 `NotImplementedException`，因此尚未完成的预测仍能让 CLI 明确返回 `ROLLBACK_NOT_READY` / 3。故障会话替身仅用于制造错误，不产生游戏推进请求，也不可能完成等价性运行。

## 实际检查

```sh
.ai/verify.sh
```

输出：生成表4097样本一致；16项 Python 自测通过；构建0 warnings/errors；`.NET Passed: 683, Failed: 0, Total: 683`。本机首轮最终检查14.94秒。683=之前680+新增诊断3，原有629项未减少。

默认仍排除42项：26个 HumanTodo 会话case和16个依赖调度的V5 case。分类排除不代表这些用例通过。

```sh
dotnet test RollbackArena.sln -c Release --filter TestCategory=HumanTodo
```

实际26项：13 passed、13 failed。失败按实际异常核对：10项需要尚未实现的远端预测，3项需要历史退休；不是编译失败或零用例。

```sh
ARENA_V5_SEEDS=1 bash tools/verify_v5.sh
```

实际构建成功，执行真实 `ConfirmedFramesEqualIndependentOfflineOracle(seed=0)` 后因 `Remote input prediction is not implemented.` 失败，wrapper退出1并留下新的TRX。将这份真实失败报告交给验证器，验证器退出1，输出 `V5_RESULTS_INVALID`。真实TRX的testName形状与验证器匹配；另外核对了历史16个真实V5结果的同一命名格式。

```sh
dotnet run --project tools/headless -c Release --no-build -- --rollback-bots
```

仍输出 `ROLLBACK_NOT_READY`、退出3。**未通过完整1000种子，未声称V5或S3完成。**

Python 的16项检查使用明确标注的合成报告测试“报告验证器本身”，不拿合成报告当作真实V5运行证据。覆盖完整/快速数量、空跑、仅发现、少跑、跳过、失败、重复、越界种子、错误fixture、计数不符、无效数量、缺失文件、终止状态，以及CLI成功/失败退出。

## 反向探针与独立审查

新增诊断先在旧实现上实际执行3项，全部断言失败；报告检查器最初未实现时，13项自测实际执行并失败。补齐实现后转绿。

| 故意破坏 | 实际 executed / failed |
| --- | ---: |
| 跳过执行计数核对 | 1 / 1 |
| 跳过逐条结果 outcome 核对 | 1 / 1 |
| 跳过唯一完整种子集合核对 | 1 / 1 |
| 接受 Aborted 报告 | 1 / 1 |
| 容器永远报告帧缺失，验证已绿的到达顺序和未来帧用例会失败 | 4 / 4 |
| 诊断省略原始输入帧号 | 3 / 3 |

6项均实际捕获，恢复原始字节；最后重新构建验收，生产源码无探针开关。所有探针仅涉及AI所有的容器/诊断/报告代码，没有改动手写调度核心。

独立只读审查了冻结的工具、测试、CI与相关契约，未发现范围内确定问题。静态审查未代替本轮构建/运行。测试审查去掉了“容量错误后在同一会话重试”的额外承诺；保留预检无半批请求和拒绝未来输入不污染历史的断言，正常推进对照使用新会话。

## 仍需完成

下一人工节点为 `RollbackSession.cs` 的预测、最早错误帧、纠正请求与退休窗口。完成后再执行全部会话契约、快速V5、完整1000种子和真实调度故障探针，移除分类与默认过滤，并把完整V5接入自动CI。

当前阶段没有新增Unity工程，V6/IL2CPP/WebGL、性能、浸泡与手感均未验收。保留当前核心与上轮实际输入版本逐字节一致；本轮构建/测试前后工作区哈希一致。无提交、推送、分支切换或部署。
