# S3 完整验收（2026-10-19）

## 结论与归属

S3 的本机 .NET 验收已通过：完整会话行为、独立 V4、真实双端 V5 1000 seeds、反向探针、默认检查少于一分钟。准备期的 HumanTodo / RequiresRollbackScheduler 分类与默认过滤全部移除；push/PR CI 已配置自动执行完整 V5。

用户本轮明确授权“S3 全部由你实现，我只做后续人工验收”，覆盖原来的人工调度边界。用户此前参与构造、输入登记与概念学习；本轮完整预测、纠正与退休由 AI 实施，应如实记录归属。Fixed64.Mul、S2 模拟及原架构守卫源码无 diff。

没有本轮提交/推送，因此没有远端 CI 实际执行记录；E 阶段的远端 CI 实际跑满1000仍待证实，不能把配置完成当作执行通过。Unity Mono / IL2CPP / WebGL（V6）、性能预算（V7）、长时浸泡（V8）和手感（V9）尚未验收；S3 不代替它们。

## 实施

- `RollbackSession` 分开保存实际值与模拟所用远端值。迟到输入与后者比较，取最早错帧；前一时间线的后继预测丢弃，依重模拟的新前驱重新预测。
- 先输出完整纠正批次，再决定能否新增一步。本地实际输入缺失和预测窗口已满时仍可以纠正过去，终点不新增没有本地输入的帧。
- 请求容量写入前整批预检。全局 P1/P2 槽位固定，本地身份只决定来源；输入延迟只映射一次。
- 上一批次决定安全退休下界，下一入口才应用，避免收包后尚未纠正就删掉重模拟输入。保留已确认且已执行的最后一帧作为预测前驱。
- 新增 9 个边界 case（P2 迟到纠正 delay=0/2/4、移动保留窗口、long 极值与后继帧检查）。会话原有 26 个 case 保留，合计35；实际推进补充2项不变。
- 默认脚本不再排除分类；完整 V5 自动 CI job 不再只限 workflow_dispatch。旧 CLI 未实现提示分支移除。

## 实际执行的检查

| 命令 / 检查 | 观察结果 |
| --- | --- |
| `dotnet test RollbackArena.sln -c Release --filter 'FullyQualifiedName~RollbackSessionContractTests\|FullyQualifiedName~RollbackSessionEdgeTests\|FullyQualifiedName~ActualInputAdvanceTests'` | 37 passed，0 failed/skipped |
| `.ai/verify.sh` | 734 项 .NET +16 项 Python passed；表4097样本一致；0 warnings/errors；14.53s，无分类排除 |
| `bash tools/verify_v5.sh` | fresh build + fresh TRX，1000 passed /0 failed /0 skipped；测试约26s；`V5_RESULTS_OK seeds=1000 passed=1000` |
| `git diff -- src/Arena.Sim tests/Arena.Tests/Architecture tests/Arena.Tests/Math` | 无输出，用户乘法、S2与旧守卫未改 |
| 逐文件 SHA256 检查 | 默认检查前后所有 tracked+untracked 非忽略文件相同；探针涉及源码逐字节恢复后重新构建 |

默认数量从准备阶段683项增加到734：纳入原来排除的26会话+16快速V5，再加9边界case。16个 Python 检查器自测单独计数。完整 V5 的 TRX 由标准库检查器核对：Completed、total/executed/passed=1000、其他错误/跳过计数为0、每项Passed、种子精确覆盖0–999且不重复。不是仅发现1000个用例或仅检查进程退出码。

最终文档更新后的重复检查记录在交接文件；上述14.53s为取消排除后的首次完整运行，不以新文档反写历史时间。

## 实际 CLI

执行正式 Release 程序（等价于 `dotnet run --project tools/headless -c Release -- ...`）：

```text
SYNC_TEST_OK frames=1000 distance=8 checksum=AFD2C47A3AAF7FF5
ROLLBACK_EQUIVALENT frames=2400 ticks=3004 rollbacks=24 resimulated=180 stalls=607 maxRollback=8 fight=580 inputEdges=28 sent=6008 delivered=5920 dropped=620 duplicated=534 checksum=8F598103D8A8BDCB
ROLLBACK_EQUIVALENT frames=2402 ticks=3013 rollbacks=1412 resimulated=10720 stalls=619 maxRollback=8 fight=502 inputEdges=1745 sent=6026 delivered=5944 dropped=632 duplicated=559 checksum=FDF3244888ABF129
ROLLBACK_EQUIVALENT frames=2404 ticks=3007 rollbacks=1447 resimulated=10973 stalls=604 maxRollback=8 fight=1214 inputEdges=4206 sent=6014 delivered=5958 dropped=563 duplicated=514 checksum=0584174056D44755
```

V4 distance=8，后三行分别 seed=0/1/2，delay=0/2/4。所有命令退出0；完整V5比较每个确认帧与独立oracle，并覆盖尾部 NoLocalInput 下纠正，不只比较最终hash或自报统计。

## 反向探针：真实测试先红，再恢复

每个探针只临时改变 AI 实现，独立 build/test 生成新 TRX；核对 executed>0、至少一个 assertion/runtime failure，而非编译错误。每次 finally 恢复原字节，最后 fresh build 全套转绿。探针源与结果位于会话临时目录，未加入生产配置。

| 故意破坏 | 实际执行 / 失败 | 抓住它的检查 |
| --- | --- | --- |
| Load 后清空 P1 PreviousInput | 9 /4 | V4 held-attack 窗口重模拟哈希不等；distance=2/4/8 等用例实际失败 |
| 忽略所有迟到预测错误 | 1 /1 | 真实V5 seed0，tick114、peer0、phase=advance；状态91 Player2.X 首次不同，期望10334765056，实际10737418240 |
| 缺失远端改为 None，不重复前驱 | 1 /1 | MissingRemoteRepeatsPreviousTimelineValueRatherThanFutureActual，期望Right得到None |
| 所有 localSlot 都当作P1 | 3 /3 | P2迟到纠正 delay0/2/4，与独立顺序状态不等 |
| 当前实际值已到就绕过更早缺口的窗口上限 | 1 /1 | ActualForCurrentFrameCannotBypassEarlierGapAtPredictionLimit，期望停等却新增一步 |
| Receive 后立即按新确认前沿删本地输入 | 3 /3 | P2迟到纠正缺少仍需重模拟的本地输入 |
| 允许远端目标帧long.MaxValue | 1 /1 | RemoteFrameWithoutRepresentableSuccessor，未在入口按约定拒绝 |
| 捕获目标后继溢出检查少减1 | 4 /2 | CaptureRejectsUnrepresentableTargetOrSuccessor，long极值入口检查失败 |

V5 故障诊断实际包含：seed、network tick、peer、phase、执行器帧号、delay/prediction、Save/Advance 请求轨迹、不同状态帧、期望/实际hash、首个字段差异。此前准备阶段对帧标签、Save/Load、包拷贝、ACK、尾包重发及结果核对器的探针历史分别见旧验证记录，本轮不冒称重新执行这些旧探针。

## 独立审查

1. 新上下文只读核心审查：完整 Session、FrameRing、Executor、契约/edge、可靠通道与 V5 runner 的交互；无可复现正确性缺陷。未运行测试，运行证据由主会话采集。
2. 新上下文只读交付审查：无过滤默认脚本、自动完整 V5、严格 TRX 检查、学习导读与 S4 草稿；无正确性缺陷、关键遗漏或无效验收设计。另复核移除旧 CLI 未实现分支的最后增量。

审查均不代表远端 CI 或 Unity 已执行。本轮未有核心实施错误被检查抓到；表中错误是为验证检查有效性主动注入。独立增量复核指出方案把 E 整体写为完成与未运行远端 CI 矛盾，已修正文档，保留待证实状态。用时及费用未精确统计。

## 最终重复验收

最终源码（含删除准备期 CLI 提示）重新构建后，默认734项.NET+16项Python再次全绿，11.61s；完整1000种子再次全绿，命令总耗时32.22s，测试约28s，严格TRX核对通过。两个检查各自前后工作树哈希均相同。最终追加本段只记录观察结果，没有改生产源码。独立审查指出的E/远端CI表述已更正；远端CI仍待授权推送后验证。
