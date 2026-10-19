# S3 A/B 准备阶段验证（2026-10-18）

结论：**A/B done-when 已满足，S3 整体未完成**。用户生产调度仍是空骨架；不能把准备期过滤后的绿色当作 V5 通过。

## 交付与边界

- `Arena.Rollback`：配置/输入/请求契约，完整帧标签环形存储、纯请求执行器、little-endian 编解码及固定脚本 V4。
- 新增 `Arena.Net`：显式整数 tick、固定种子、延迟/抖动/丢包/重复/乱序、有限队列，以及冗余重发、连续实际前沿 ACK 和末包重发。
- headless：`--sync-test`、真实会话 `--rollback-bots`；独立顺序 S2 oracle、逐帧确认轨迹、终点检查、字段及请求诊断。
- `RollbackSession` 仅构造时校验配置/槽位；4 个行为属性和 3 个行为方法仍明确抛 `NotImplementedException`。预测、输入延迟映射、确认、最早错误帧、纠正请求和停等均留给用户。
- `src/Arena.Sim`（含用户乘法）、原数学/架构/玩法/模拟测试与 IL 守卫规则均无 diff；没有新增 NuGet。

## 实际命令与输出

最终源码恢复后重新构建，执行：

```sh
.ai/verify.sh
```

```text
PASS: sine table matches all 4097 generated samples
Build succeeded.
    0 Warning(s)
    0 Error(s)
Passed! - Failed: 0, Passed: 678, Skipped: 0, Total: 678
```

本机总用时 12.05 秒。678 = S1/S2 原有 629 + 本轮 AI 准备 49；无原测试减少。新增分组如下：

| Fixture | 实际通过 case |
| --- | ---: |
| `RollbackInfrastructureTests` | 12 |
| `SyncTestTests` | 9 |
| `InputPacketCodecTests` | 6 |
| `SimulatedTransportTests` | 5 |
| `ReliableInputChannelTests` | 5 |
| `EquivalenceHarnessTests` | 12 |

V4 测试覆盖完整机器人录制的 k=1/2/4/8、短窗口、跨阶段及按住攻击输入沿。网络单测使用真实编码包与双向重发链路，包含末包丢失后不再捕获输入的恢复。

```sh
dotnet run --project tools/headless -c Release --no-build -- --sync-test --distance 8
```

退出 0，输出：

```text
SYNC_TEST_OK frames=1000 distance=8 checksum=AFD2C47A3AAF7FF5
```

```sh
dotnet run --project tools/headless -c Release --no-build -- --rollback-bots
```

退出 3，输出：

```text
ROLLBACK_NOT_READY: user RollbackSession scheduling is not implemented; V5 has not passed.
```

## 32 项实际红灯

```sh
dotnet test RollbackArena.sln -c Release --no-build --filter 'TestCategory=HumanTodo|TestCategory=RequiresRollbackScheduler'
```

退出 1：`Failed: 32, Passed: 0, Skipped: 0, Total: 32`。逐项解析 TRX，32 项错误消息全部包含 `NotImplementedException`；没有因编译失败或零用例获得预期红灯。

`RollbackSessionContractTests`（HumanTodo）16 个 case：

- `ActualInputsDelayExactlyOnceAndPreserveGlobalSlots`：delay=0/2/4 × slot=0/1，共 6 个。
- `MissingLocalActualInputIsNeverPredicted`
- `MatchingLateActualDoesNotRollback`
- `EndpointLateCorrectionDoesNotAdvanceBeyondCapturedTail`
- `EarliestMismatchAndFutureActualDoNotLeakIntoPastPrediction`
- `FullPredictionWindowStillExecutesCorrectionOnlyBatch`
- `LockstepWaitsForCurrentRemoteActual`
- `OutOfOrderActualDoesNotCrossConfirmationGap`
- `RepeatedCapturesAndPacketsAreIdempotentButConflictsFail`
- `HeldAttackAcrossRecoveryRollbackDoesNotCreateAnotherPress`
- `RetiredSlotsDoNotAliasNewFrames`

`RollbackEquivalenceTests`（RequiresRollbackScheduler）16 个 case：`ConfirmedFramesEqualIndependentOfflineOracle(seed=0)` 到 `seed=15`。

`.ai/verify.sh` 暂用 `TestCategory!=HumanTodo&TestCategory!=RequiresRollbackScheduler`，并在通过输出后用 `--list-tests` 列明这 32 项。仅这两组被排除，乘法仍执行。C/D 完成后须移除分类及过滤，E 才能判 S3 整体完成。

```sh
ARENA_V5_SEEDS=1000 dotnet test RollbackArena.sln -c Release --no-build --list-tests --filter FullyQualifiedName~RollbackEquivalenceTests
```

实际发现恰好 1000 个 seed=0..999 case，**没有执行，更没有通过完整 V5**。分别设置 `ARENA_V5_SEEDS=0`、`1001`、`bad` 后实际跑该 fixture，均退出 1、执行 1 个失败 case；前两者报 `ArgumentOutOfRangeException`，后者报 `FormatException`，不会静默得到零测试绿灯。

## 14 项反向探针

逐项只修改本轮新文件，运行针对性测试，解析 TRX 确认 executed>0、failed>0，最后逐字节恢复。编译错误不算探针成功；未修改用户乘法、调度骨架或守卫规则。

| 故意破坏 | 实际 executed / failed |
| --- | ---: |
| 按取模槽命中，忽略完整帧标签 | 1 / 1 |
| 执行器信任错误重模拟标记 | 1 / 1 |
| Step 注入未纳入快照的可变计数，V4 原始/重模拟不再一致 | 4 / 4 |
| 编码整数使用错误字节顺序 | 1 / 1 |
| 解码绕过整包输入验证，允许部分写入 | 1 / 1 |
| 传输发送时省略有效字节复制 | 1 / 1 |
| 相同到达 tick 的发送序号顺序反转 | 1 / 1 |
| 发送一次后停止重传未确认尾包 | 1 / 1 |
| 放过超出已发送输入范围的 ACK | 1 / 1 |
| oracle 省略 expected hash 比较 | 1 / 1 |
| oracle 允许不完整确认日志 | 3 / 3 |
| 未执行纠正批次也发布/退休 | 2 / 2 |
| 终点不检查 ForwardAdvanced / NoLocalInput | 3 / 2 |
| 终点允许夹带 Advance(N) 请求 | 3 / 1 |

前 11 项与审查后 3 项均捕获且恢复；随后重新构建再运行 678 项和真实 CLI。完整 V5 的调度故障探针尚未执行，留到 C/D/E，不能用这里的 oracle 单元探针代替真实用户调度验收。

## 独立审查与 AI 错误

两份独立只读审查只获得冻结 diff、规格、规则和必要源码，不继承作者实施讨论。基础设施/V4 审查确认没有把生产调度藏到辅助类；两份都指出以下第 1 项，网络/harness 审查另指出第 2 项：

1. peer 1 偶尔跳过调度 tick，却仍按刚收包推进的确认前沿发布旧哈希并退休历史。Fight 中会误判 desync；Countdown 中状态可能没变，但会删掉下次纠正需要的快照。修复为 `PublishAfterBatch` 在完整批次后统一发布/退休，并以 Countdown/Fight 两个实际回退重放回归和反向探针验证。
2. 首次到达 N 即可成功，未必真正观察到 N 上的 `NoLocalInput`。修复为双方必须完成终点批次验证、逐帧确认到 N 才能返回成功；3 个回归覆盖错误推进、错误原因和额外 Step，反向探针均抓到。

复核四份最终 harness/CLI/tests 文件后，审查者确认两项修正、计数及请求诊断补齐，无新增确定问题。该审查是静态核对，不能代替未实现调度的集成运行。

另一次测试夹具错误：首字段差异测试手动改 Frame，却未执行 S2 Step，导致真实首差异是 PhaseFrame。定向运行抓到后，改用真实 Step 建立基准再只改 X，保留对 `state.Player1.X` 的断言。

## 工作区与未验证项

最终检查前后，对 `git ls-files --cached --others --exclude-standard` 中所有存在文件计算 SHA-256 并比较，完全一致；构建/测试没有改动工作树。生成文件和 TRX 均在被忽略目录或临时目录。核对旧模拟及所有旧测试源码无 diff；无提交、推送、分支切换或合并。

尚未验证：生产 RollbackSession、V5 快速/完整种子成功、完整 V5 CI 与调度故障探针、V6 Unity EditMode/IL2CPP/WebGL、V7 性能、V8 长时浸泡、V9 手感。接手入口见 [用户指南](../s3-human-guide.md)。
