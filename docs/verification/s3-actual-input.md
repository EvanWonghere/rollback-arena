# S3 实际输入推进学习阶段（2026-10-18）

本轮 done-when 已满足：缺本地输入、零预测锁步等待、delay=0/2/4 与两个全局槽位的 8 个 case 通过。用户本轮明确要求 AI 实施此小步供学习；这不代表整体 S3 完成，也不扩大到预测、纠正或动态退休。

## 代码从哪里读

1. `RollbackSession.AdvanceFrame`：依次查询当前目标帧的本地/远端实际输入；准备好两个请求后才更新会话帧号。
2. `RollbackSession.StallAdvance`：只累计一次 Stalls，返回未新增一步，不修改帧号或请求。
3. `RollbackRequestExecutor.Execute`：真正按顺序处理 Save/Step，调用 S2 的 ArenaGame。完整执行批次后，会话帧号与 GameState.Frame 相同。

成功路径输出 `Save(frame)`、`Step(frame, inputs)`。输入按 LocalSlot 映射到全局 P1/P2；查询目标帧时不再增加 delay。容量检查发生在追加请求前，避免只留下 Save 的半批请求。

缺本地输入返回 NoLocalInput；零预测缺远端返回 PredictionLimit。两者均只记录一次停等。配置允许预测但远端输入缺失时仍抛 `NotImplementedException: Remote input prediction is not implemented.`，不把它伪装成零预测停等。

## 实际验证

新增 `ActualInputAdvanceTests` 两项：

- `WaitingCountsStallsWithoutAdvancingOrProducingRequests`：连续两种停等不改变帧号或请求，累计 Stalls=2；输入齐全后真正执行批次，双方帧号变成 1，成功推进不再增加 Stalls。
- `TooSmallBufferDoesNotAppendHalfABatchOrAdvance`：缓冲只能放一个请求时明确 BufferTooSmall，缓冲、帧号和停等统计保持不变。

先在旧未实现方法上实际执行这两项：executed=2、failed=2；实现后全部通过。未改动或删除既有测试。

定向命令：

```sh
dotnet test RollbackArena.sln -c Release --filter 'FullyQualifiedName~MissingLocalActualInputIsNeverPredicted|FullyQualifiedName~LockstepWaitsForCurrentRemoteActual|FullyQualifiedName~ActualInputsDelayExactlyOnceAndPreserveGlobalSlots|FullyQualifiedName~ActualInputAdvanceTests'
```

输出：`Failed: 0, Passed: 10, Skipped: 0, Total: 10`。

恢复全部探针后，重新构建并执行：

```sh
.ai/verify.sh
```

输出：生成表 4097 样本一致，构建 0 warnings / 0 errors；`Failed: 0, Passed: 680, Skipped: 0, Total: 680`，本机 12.54 秒。680=原有 629+准备阶段 49+本轮补充 2；没有旧测试减少。

```sh
dotnet test RollbackArena.sln -c Release --no-build --filter TestCategory=HumanTodo
```

实际执行 16 项：9 passed、7 failed。9 项通过是上述 8 个阶段目标加原重复/冲突测试。fixture 尚未整体验收，暂保留 HumanTodo 分类；默认检查仍排除并列出该组及 V5，不把整组都称作失败或都称作通过。

7 项未完成按名字核对如下：

| Case | 本轮实际失败原因 |
| --- | --- |
| `MatchingLateActualDoesNotRollback` | 远端预测 NotImplementedException |
| `EndpointLateCorrectionDoesNotAdvanceBeyondCapturedTail` | 同上 |
| `EarliestMismatchAndFutureActualDoNotLeakIntoPastPrediction` | 同上 |
| `FullPredictionWindowStillExecutesCorrectionOnlyBatch` | 同上 |
| `OutOfOrderActualDoesNotCrossConfirmationGap` | 该集成场景先推进缺远端的帧，因此仍需要预测 |
| `HeldAttackAcrossRecoveryRollbackDoesNotCreateAnotherPress` | 远端预测 NotImplementedException |
| `RetiredSlotsDoNotAliasNewFrames` | 历史未退休，覆盖活槽抛 HistoryOverflow |

```sh
dotnet run --project tools/headless -c Release --no-build -- --rollback-bots
```

实际退出 3，输出 `ROLLBACK_NOT_READY: user RollbackSession scheduling is not implemented; V5 has not passed.`。本轮未执行完整 V5 种子验收。

## 反向探针与审查

仅在本轮新增推进代码中临时注入，实际运行断言失败后逐字节恢复；不修改用户输入登记正文、乘法或守卫规则。

| 故意破坏 | executed / failed |
| --- | ---: |
| 不累计 Stalls | 1 / 1 |
| 绕过容量预检，让 Save 先进入过小缓冲 | 1 / 1 |
| 颠倒本地槽位对应的全局输入 | 6 / 6 |
| 省略 Step 前的 Save | 6 / 6 |

4 项均捕获并恢复，编译失败不作为探针成功。随后重新构建再运行定向、默认、HumanTodo 与 CLI 检查。

独立审查只读取冻结 diff、规则、规格、源码和测试；未发现范围内正确性 bug、无效验证或调用者回归。静态审查不替代未实现部分的动态验收。

## 边界与工作区

- 输入历史仍固定在最初一段目标帧，不能跨 HistoryCapacity 持续运行；尚无动态退休。
- 尚无实际使用的远端预测历史、最早错帧追踪、Load/重模拟请求和纠正后停等。
- 本轮未改用户已有 CaptureLocalInput、ReceiveRemoteInput、确认维护正文，也未改 Fixed64.Mul、S2 或旧测试。仅修改本轮推进逻辑、说明注释、新测试、检查输出提示及相关文档。
- 最终检查前后工作树文件 SHA-256 一致。无提交、推送、分支切换；探针与 TRX 在系统临时目录，生产源码无故障开关。
- V5 快速/完整种子、完整 V5 CI、V6 Unity/IL2CPP/WebGL、V7/V8/V9 未验收；Unity 与手感还需编辑器或人工。
