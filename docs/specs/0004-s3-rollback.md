# S3 实施方案：回滚会话与模拟网络

> 状态：**S3 已完成 .NET 验收**（2026-10-19）。用户明确授权“S3 全部由你实现，我只做后续人工验收”，覆盖下文初始亲写分工。原始分工与阶段红灯保留作历史；当前结果见 `../verification/s3-rollback.md`。
> 前置：S2 已完成 629 项验收并提交为 `7d9396c`。接手从本文件、`0001-rollback-arena.md` 的 S3 行及 `0002-gameplay.md` 开始。

## 目标

同一份真实双人输入，在离线顺序运行与两个经历预测、迟到输入纠正的对端上，所有已确认状态的校验和一致。S3 完成意味着 V4 和 V5 实际通过，**不是**“接口已搭好”或“排除了待实现测试后全绿”。

沿用总方案 done-when：V4 每帧强制回滚检查通过；V5 至少 1000 个固定种子通过；故意破坏状态恢复或确认/纠正行为时，两项检查都会失败；默认检查保持一分钟内，完整 V5 进入 CI。

## 不做

- 初始约定由用户亲写生产回滚调度；2026-10-19 用户明确转交全部 S3 实施，AI 已完成预测、确认、纠正和退休。此授权限于 S3，不改变其他阶段的人工边界。
- 不改 `Fixed64.Mul`、S2 玩法与状态时序；不接 Unity、真实 socket、中继、UDP/WebSocket、房间、断线重连或反作弊。
- 不引入 GGPO/GGRS/Backdash 为依赖；不在纯逻辑层加 NuGet、浮点或真实时间。
- 不在 S3 宣称 V6 跨运行时、V7 性能预算、V8 长时浸泡或 V9 手感完成。

## 背景与成熟做法

现有代码已提供 `ArenaGame.Create/Step`、完整值类型 `GameState/GameSnapshot`、显式 45 叶字段 `StateChecksum`、输入回放和 headless。批准前 `Arena.Rollback` 只有 AssemblyMarker；A/B 已增加对 Arena.Sim 的引用与下述准备设施。现有调用者只有场景/模拟测试和 headless，S3 不改变它们的默认行为。

参考只用于选择边界，下面的具体协议是本项目提案：

- [GGPO Developer Guide](https://github.com/pond3r/ggpo/blob/master/doc/DeveloperGuide.md)：确定的单帧推进与状态保存/恢复是集成基础；达到预测限制时不推进游戏。
- [GGRS 0.13 的请求接口](https://docs.rs/ggrs/0.13.0/ggrs/enum.GgrsRequest.html)：会话发出保存、加载、推进请求，调用方必须执行。
- [GGRS SyncTestSession](https://docs.rs/ggrs/0.13.0/ggrs/struct.SyncTestSession.html)：无需网络，以固定回滚距离重模拟并比较校验和。
- [GGRS 主循环](https://github.com/gschup/ggrs/blob/main/docs/main-loop.md)：网络轮询与游戏推进可分开，等待期间仍须处理网络。

## 初始分工（历史，已被 2026-10-19 明确授权覆盖）

| 内容 | 所有者 | AI 可写到哪里 |
| --- | --- | --- |
| 输入延迟映射、实际/预测记录、预测按住值 | 用户 | 定义数据结构、测试输入与期望；不实现策略 |
| 连续确认前沿、最早错误预测帧 | 用户 | 只提供按帧存取容器；不提供寻找错误帧/确认前沿的便利方法 |
| 是否回滚、从哪帧加载、重模拟哪些帧 | 用户 | 请求格式与执行器；执行器不生成或补充请求 |
| 预测窗口、停等、异步纠正后恢复推进 | 用户 | 状态枚举、统计字段、黑盒测试 |
| `RollbackSession` 构造与入口 | AI 先放骨架，用户填行为 | 未实现的方法明确抛 NotImplementedException，没有锁步/无回滚的临时替代 |
| 有帧标签的环形存储、快照槽 | AI | 纯存取、越界/覆盖检查，不解释输入或时间线 |
| 数据包编码、可靠重发、模拟传输 | AI | 搬运实际输入与 ACK，不判断预测或游戏确认 |
| V4 固定脚本验证器、V5 harness、诊断、headless | AI | 生成测试数据并验证结果，不成为生产调度后门 |

## 设计

### 1. 帧号先统一

| 名称 | 精确定义 |
| --- | --- |
| 输入帧 `f` | 提供给执行第 f 步的按住输入 |
| 状态帧 `F` | `GameState.Frame`，下一次将执行的输入帧；初始 F=0 |
| 快照 `Snapshot(F)` | 执行输入 F 之前的完整状态；加载后 Frame 必须等于 F |
| `H(F)` | 状态帧 F 的校验和；执行输入 f 后比较的是 H(f+1) |
| `LastConfirmedInputFrame=C` | 两名玩家的实际输入从 0 到 C **连续齐全**；初始无输入时为 -1，不能取“收到过的最大帧号” |
| 可发布的确认状态 | 纠正请求全部执行完后，状态帧不超过 `min(NextFrame,C+1)`；预测状态不得冒充确认结果 |

两端都用全局槽位 0=P1、1=P2；本地/远端身份只影响输入来源，不能交换 `FrameInputs.Player1/Player2` 的游戏含义。重新模拟必须恢复 PreviousInput、阶段、冷却和动作等整个 GameState，不能只恢复位置。

### 2. 默认值与输入采样契约（批准后固定）

| 配置 | 默认值 / 约束 |
| --- | --- |
| `InputDelayFrames` | 2；测试覆盖 0、2、4。对捕获帧 c 的输入，实际目标帧为 c+d，只延迟一次 |
| `MaxPredictionFrames` | 8；测试覆盖 0（锁步）、1、8 |
| `HistoryCapacity` | 256；快照、输入和哈希槽均有完整 long 帧标签，不能仅按取模索引命中 |
| 请求缓冲 | 默认 32 个值元素，容量至少 `2*MaxPredictionFrames+3`，不足时明确失败，不能截断 |
| 数据包输入块 | 最多 32 个连续目标帧；重发优先从最早未 ACK 的帧开始 |
| 会话身份 | caller 提供固定 ulong `SessionId`；S3 harness 给双方同一配置，真实协商留待 S5 |

构造时拒绝负 delay/窗口、非正容量及不能容纳窗口与输入延迟的配置；统一历史容量至少 `MaxPredictionFrames+InputDelayFrames+2`，请求容量满足上表。容量计算须先验证整数范围，不能因溢出通过检查。

- 开局目标帧 `[0,d)` 的输入双方均预先知道为 None；d=0 时没有该前缀。oracle 也使用这一前缀，不得把原始输入直接拿来比较延迟后的会话。
- 捕获帧是本地模拟逻辑帧，不是网络 tick。每个新逻辑帧最多捕获一次；停等重试不重新采样、不增加捕获帧号。重复提交相同捕获帧/相同值幂等，改值为 ConflictingInput；新按键留待下个可推进帧。
- 远端提交的是已经映射好的**目标输入帧**，不能再次加 delay。
- **只预测远端，从不预测本地。** 新增一步必须已有 NextFrame 对应的本地实际输入（约定启动前缀也算实际）；缺少时返回 Stalled/NoLocalInput，不能用 None、旧输入或未初始化环槽冒充实际值。该返回仍可包含纠正过去时间线的请求。
- 远端预测只重复本时间线前一帧所用的按住状态；无历史时用 None。有该帧实际输入时优先用实际值；不能用未来帧的输入预测过去，也不生成“刚按下”。
- 重复且相同的实际输入幂等；同一帧不同的实际值是协议错误，不能按到达顺序取最后一个。
- 拟推进后的未确认领先为 `(NextFrame+1)-(C+1)`，只有不超过 MaxPredictionFrames 才允许新增一步；因此初始可预测 8 帧，之后停等。MaxPrediction=0 时只有当前实际输入齐全才推进。
- 停等不停止网络 tick、收包、ACK 或重发；一个停等结果也可能带有纠正过去预测所需的请求，执行器不能忽略该批请求。

### 3. 请求边界与拟定入口

`Arena.Rollback` 增加对 Arena.Sim 的项目引用，保持 C# 9 / netstandard2.1。下列契约已在 A/B 落代码：

| 入口 | 职责 |
| --- | --- |
| `RollbackSession(options, localSlot)` | 从状态帧 0 开始；两个全局槽位之一为本地 |
| `CaptureLocalInput(captureFrame, held)` | 实现延迟映射、重复检查与本地实际输入登记，返回映射后的 `FramedInput` 供网络发送 |
| `ReceiveRemoteInput(targetFrame, held)` | 实现实际输入登记、确认与纠正相关状态维护 |
| `AdvanceFrame(requestBuffer)` | 返回有序请求与 ForwardAdvanced/Stalled/错误状态及停等原因；一次最多新增一个游戏步，且必须有该帧本地实际输入；允许多个重模拟步，NoLocalInput 也不阻止纠正历史 |
| 会话只读观测 | NextFrame、LastConfirmedInputFrame、远端连续实际输入前沿、回滚次数/重模拟帧数/最大回滚距离/停等数 |

请求只有三类值记录：`SaveState(F)`、`LoadState(F)`、`Advance(F, FrameInputs, isResimulation)`。

AI 的 `RollbackRequestExecutor` 持有 GameState、带帧标签的快照与哈希历史，严格按顺序执行：Save 校验当前 Frame 并保存；Load 校验槽标签并完整恢复；Advance 校验 Frame 后调用 S2 Step 并记录 H(F+1)。每个 Advance 前必须已有本批次或此前有效的 SaveState(F)。缺快照、帧号不符和缓冲不足都明确报错，不能默默跳过。

请求生成后必须同步执行完整批次，才可再调用会话入口；异常即该会话失败，不支持半批重试。NextFrame 与执行器状态在批次完成后必须一致。执行器可以机械清除 Load 帧之后的旧推测哈希，但不能自行选择回滚点或补做任何 Step。确认哈希在整个纠正批次完成后才发布，禁止发布旧的预测结果。

快照槽与输入槽是独立有界数组。覆盖尚需加载的快照或尚未确认的输入必须停止并报 HistoryOverflow/Backpressure，不可静默覆盖。仍保留的旧帧可检查相同重复或冲突；已经确认且超出保留范围的帧返回 IgnoredRetired，不再声称可以检查已丢弃值的冲突，也不改变状态或确认前沿。未确认但所需历史缺失是错误，不能按退休帧忽略。过远未来帧、frame+count 溢出必须有确定的拒绝结果。

### 4. 模拟网络与可靠输入

新增 `Arena.Net`（默认 netstandard2.1 / C# 9、无 NuGet），引用 Rollback 的协议契约；Rollback 不反向引用网络实现。`ITransport` 只收发有界字节包，采用调用方缓冲，接收不得把内部可变数组借给调用方。模拟传输在发送时复制有效字节。

所有网络测试用显式整数 `NetworkTick`，60 tick/s；0–200ms 单向基础延迟按 0–12 tick 表示，附加抖动 0–3 tick，丢包 0–10%，并覆盖重复、乱序及单向连续丢包。概率使用固定 seed 和整数阈值；不读时钟、不用 float/double/System.Random。网络 tick 与游戏帧独立，模拟停等时仍推进网络。

同一到达 tick 按 `(dueTick, sendOrdinal)` 确定交付顺序，不靠哈希容器遍历。配置的随机丢包不能作为“本次没发成”的提示泄露给会话。

拟定 v1 包头：版本、SessionId、SenderSlot、StartInputFrame、Count、AckThrough；payload 为 Count 个连续帧的 byte 输入。Count=0 是 ACK-only，StartInputFrame 固定为 0；正常 Count 为 1–32，保留 bit7 必须为 0。所有整数显式 little-endian，不序列化内存布局。解码检查长度、版本、会话、发送者、帧范围和整数溢出，失败不能把半个包送入 Session。

- ACK 表示接收方已经保存的**连续实际目标输入前沿**，不是游戏状态已执行，也不是双方校验和一致。
- ReliableInputChannel 不计算确认前沿，只使用用户会话给出的连续实际前沿生成 ACK。确认状态判定仍属于用户的调度器。
- 包内总是冗余重发最早未 ACK 的连续输入块；停等或输入流末尾也继续重发/发送 ACK-only，直到全部送达或测试超时。
- 只接受不超过本地已发送/双方约定启动空前缀的 ACK；乱序旧 ACK 不倒退已知进度。释放重发缓冲不等于丢掉 Session 的回滚输入历史。
- 确定性的有限队列用满则报错或回压，不把队列溢出伪装成配置的网络丢包。永久丢包是测试超时，不声称可保证进展。

### 5. V4 与 V5 的独立判据

**V4：** AI 可写 SyncTestSession 作为固定测试脚本执行器，不依赖或替代生产 RollbackSession。对同一录制输入先正常推进，再每帧从固定距离 k 的快照恢复、重放并比较窗口内每个状态哈希；k=1/2/4/8，另测开局不足 k、跨 Countdown/Fight/Outro/MatchOver、按住攻击的输入沿。原始历史与重模拟输出必须分开保存，不得覆盖后再自比。

**V5：** 两个真正的生产 RollbackSession + 两条模拟链路；独立 oracle 只用 S2 的 ArenaGame，按双方真实输入及一次 delay 前缀顺序推进，不调用调度器、预测或其输入历史。主测试用先录好的输入，不让两边根据各自预测状态生成不相同的“同一份输入”。

每个 seed 默认至少捕获 `M=2400` 帧，覆盖环形存储多次回绕；包含 S2 机器人实战录制、精确动作边界和固定 seed 输入。不能只测倒计时或 MatchOver 后冻结：报告 Fight 帧、真实输入沿、纠正次数、重模拟帧数、停等、最大回滚距离与网络交付计数，专门的迟到错预测场景必须确实发生纠正。执行器独立统计真实请求轨迹，并与 Session 报告核对，不能只信调度器自己的回滚计数。

双方在相同确认状态帧 F 的 H(F) 必须相同且等于 oracle；不能比较不同帧或仍未纠正的预测哈希。两名玩家各捕获 c=0..M-1，共映射到目标输入帧 d..M+d-1；加上 `[0,d)` 启动前缀，最终目标状态帧为 `N=M+d`。捕获结束后仍可执行已经入队的 delay 尾巴，直到 N；不捕获 c>=M，故没有目标帧 N 的本地实际输入，AdvanceFrame 到 N 后必须返回 NoLocalInput 且不能输出 Advance(N)。此时继续接收、重发和调用 AdvanceFrame 执行迟到纠正，直到收敛。设置明确 tick 上限，最终两端所有目标输入 0..N-1 连续确认、状态帧 N，逐帧确认日志与 oracle 一致，否则失败，不允许因缺数据而少比几帧。不能仅因预测状态进入 MatchOver 就停止收包和纠正。

快速套件为固定 16 seeds，完整为 0–999 共 1000 seeds。测试侧环境变量 `ARENA_V5_SEEDS` 必须严格解析正数；无效值或 0 直接失败。CI 的完整 V5 必须报告实际执行 1000 个 case，不能只看退出码。

### 6. 用户调度验收清单

| 情况 | 可观察结果 |
| --- | --- |
| 零延迟，全部实际输入先到 | 与离线每帧一致，回滚计数 0 |
| 预测值与迟到实际值相同 | 不回滚，确认前沿推进 |
| 迟到值不同 | 从最早不同输入帧的快照纠正，所有受影响的后继帧采用更新后的时间线 |
| 同批多个错预测 | 最早错误帧决定起点；更晚真实输入不丢失 |
| 窗口已满且收到了旧帧纠正 | 允许纠正但不新增游戏步，Frame 不向前走；网络仍推进 |
| 输入 f+1 先到、f 未到 | 不能跨过缺口宣称连续确认 |
| 本地为 P2 | FrameInputs 的全局两槽位仍正确 |
| 按住攻击跨回滚边界 | PreviousInput 随快照恢复，不额外生成按下沿 |
| 输入延迟 0/2/4 | 开局前缀、目标帧和 oracle 一致，不重复延迟 |
| 相同重复包 / 冲突值 | 前者幂等，后者明确错误，不采用“最后到的值” |
| 环形槽回绕、过旧/过远帧 | 帧标签匹配才可读取，绝不读到同索引的新一轮数据 |
| 两端推进速度不同 | 领先端受预测上限约束；落后端/网络继续推进后可恢复，无永久停等 |
| 丢包及末包丢失 | 重发和 ACK-only 使真实输入最终齐全；超时明确失败 |
| 已到 N，末帧远端实际值迟到且预测错 | NoLocalInput 下照常完成纠正批次；不输出 Advance(N)，两端仍停在 N 且 H(N) 等于 oracle |
| MatchOver 被预测错或跨回合回滚 | 完整状态恢复后与 oracle 一致，不能把已预测终局当成不可更改 |

## 分阶段

A/B/C/D 已完成本机验收；E 的反向探针、独立审查、默认全量检查与 CI 配置已完成，远端 CI 实际执行仍待提交/推送授权后验证。下表保留原批准计划的分工，C/D 后续按用户明确授权由 AI 实施。每阶段保持可构建和可复现的交付，不靠跳过 AI 基础设施测试保持绿色。

| 阶段 / 所有者 | 内容 | done-when | 验证命令 |
| --- | --- | --- | --- |
| A / AI | 契约、会话空骨架、帧标签环形存储、执行器、编解码；先写调度行为测试 | 基础设施测试全绿；用户待实现组实际因 NotImplementedException 失败且非零用例；629 项原测试完整保留 | `dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~RollbackInfrastructureTests`；单独跑 HumanTodo 组 |
| B / AI | 固定脚本 V4、模拟网络与重发单元测试、V5 harness 和故障输出 | V4 与网络原语通过反向探针；V5 runner 不能绕过调度；依赖调度的集成检查仍明确待验收 | `dotnet test RollbackArena.sln -c Release --filter 'FullyQualifiedName~SyncTestTests\|FullyQualifiedName~SimulatedTransportTests\|FullyQualifiedName~ReliableInputChannelTests'` |
| C / 用户 | 填写 RollbackSession 的全部预测、确认、纠正和停等行为 | 逐项黑盒契约测试通过，轨迹和状态均正确，移除相应待实现标签 | `dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~RollbackSessionContractTests` |
| D / AI 配合用户 | 双端等价性与 headless 网络演示，修复仅在本人责任范围内 | 快速 16 seeds 与完整 1000 seeds 通过；用户调度问题由 AI 给证据/提示，用户修改 | `ARENA_V5_SEEDS=1000 dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~RollbackEquivalenceTests` |
| E / 双方 | 反向探针、独立审查、默认检查和进展记录 | V4/V5 故意故障均变红；全部代码恢复；无分类排除；默认检查 <60s，CI 完整 V5 实际跑满 1000 | `.ai/verify.sh`；完整 V5；`dotnet run --project tools/headless -c Release -- --rollback-bots` |

### 准备期的红灯与 CI

阶段 A/B 中，仅用户会话行为 fixture 标 `HumanTodo`；AI 写的依赖调度集成 fixture 标 `RequiresRollbackScheduler`。两组分别列名称、数量和失败原因，原有乘法不得重新标记。

准备期默认检查可临时排除这两个组，但基础设施、V4、网络原语和原有 629 项必须全部运行。完整 V5 在调度可用前保持明确“未验收”，不能通过空 case、Ignore 或伪会话得到绿灯。阶段 C/D 后移除全部标签与过滤，再开完整 V5 CI 步骤；最终 S3 完成不能留下任何排除。

### 反向探针

- V4：临时在恢复时丢掉 PreviousInput，或引入未被快照的可变计数；窗口哈希比较必须实际失败。
- V5：人为注入“迟到不同输入却不纠正”或“跨缺口确认”的测试替身故障；独立 oracle 比较或进展断言必须失败。不能为探针未经授权修改用户调度文件，可使用明确的测试故障开关，发布配置不包含该开关。
- 基础设施：错误帧标签、Save/Load 次序、传输借出复用数组、ACK 越过缺口、停止末包重发、错 global slot 分别由针对性用例抓到。
- 每次确认 executed>0 且断言失败，恢复源码后重新构建；完整对比前后文件哈希。报告种子、配置、网络 tick、状态/输入帧号、请求轨迹和首个不同字段，不只输出最终 hash。

## 质疑与取舍

下述分工风险按原批准方案保留；2026-10-19 用户已明确转交全部 S3，当前不会再以亲写边界阻止 AI 完成核心。

- **核心风险是 AI 越过分工。** 最早错帧、确认、预测、停等都留在用户文件；辅助类 API 审查必须防止把调度搬走。V4 固定验证脚本可以回退重放，但不能当作生产会话的替代。
- **8 帧窗口小于最大单向延迟。** 12 tick 延迟加抖动时停等是预期行为，正确性与可恢复进展才是 S3 判据；不宣称这已证明手感可用。
- **有丢包的传输不等于可靠输入。** 冗余、ACK 和末包重发不能省略，否则 V5 的尾部可能永远不确认。无限持续丢包不保证进展，用有界超时显式失败。
- **稀疏快照、变输入延迟、压缩和真实时间同步暂不做。** 每帧保存和固定 delay 让用户先验证核心；S3 用预测上限和不同推进速度测试约束领先，真实显示帧节奏留到 S4。
- **避免伪 oracle。** 预录真实输入、独立延迟映射和 S2 顺序模拟才是基准；不能让双方共用同一个输入缓存对象，也不能拿重模拟覆盖后的数组与自身比较。
- **默认数值不是性能证明。** 256 槽、32 请求/输入块、16/1000 seeds 先按此实施并测量；若触发容量或时间预算，说明具体输入与证据，再讨论调整，不能静默减少覆盖。
- **回退路径**：S2 已推送并可独立运行。S3 只增量加入新程序集内容与 headless 模式，不破坏 `--bots/--replay`。不做 reset、整目录覆盖或分支切换来回退。

## 当前学习入口

用户已明确授权完整 S3，`RollbackSession.cs` 已实现。沿 `CaptureLocalInput → ReceiveRemoteInput → AdvanceFrame → RollbackRequestExecutor.Execute` 阅读；详见 `docs/s3-human-guide.md` 的数据流图和逐步示例。完整证据见 `docs/verification/s3-rollback.md`。默认检查没有待实现分类排除，1000 seeds 命令会重新构建并严格核对真实 TRX。

## 进展记录

- 2026-10-18：阅读当前全部 S2 入口与测试、总方案 S3 完成标准及 GGPO/GGRS 主文档；S2 提交推送后拟定本草稿。实际 baseline 629 项通过。S3 生产代码、V4/V5 和待实现调度测试均尚未创建。
- 独立只读审查发现尾部纠正缺少明确停止新增游戏步的契约；已补上“必须有本地实际输入、只预测远端”、NoLocalInput 下允许历史纠正及对应终点用例。该修订不提供用户调度实现。

- 2026-10-18 A/B 准备完成：契约、帧标签存储、纯请求执行器、编解码、可靠输入与模拟网络、固定 V4、V5 oracle/harness 和待实现黑盒测试已落地。准备期 678 项通过（保留原 629 项），用户契约 16 项 + V5 16 项实际均因 NotImplementedException 失败；1000 seeds 仅发现未执行。14 项反向探针捕获并恢复；独立审查发现并修复跳过批次仍发布/退休、未实际观察终点 NoLocalInput 两处 harness 问题。S3 整体未完成。

- 2026-10-18 实际输入推进学习阶段：用户在逐步实现构造和本地登记后，要求 AI 展示远端登记示例并明确授权“你实施一下，我看你实施后的代码学习”。本轮只落实实际输入 Save/Step 推进、零预测停等与统计，预测分支仍明确未实现，不扩大到回滚纠正与退休。8 项阶段目标 + 2 项补充检查通过，默认 680 项通过；会话契约 16 项中 9 通过、7 未通过。4 个反向探针捕获并恢复，独立审查无范围内发现。详见 `docs/verification/s3-actual-input.md`。原有整体 S3 done-when 不变。

- 2026-10-18 AI 准备收尾：用户补充排除仍需本人实现的部分后，调度归属澄清尚待回复；本轮按原分工保留 RollbackSession 全文未改。AI 增加 10 个 HumanTodo 边界 case、3 个运行阶段诊断测试、严格 TRX 检查器及 16 项 Python 自测、全新结果目录的 V5 命令和仅手动触发的 CI 入口。默认 683 项 .NET 与 16 项 Python 通过；会话 26 项中 13 passed、13 failed。真实 seed=0 因预测未实现失败，检查器正确拒绝；未实际通过1000 seeds，未启用 push/PR 完整 V5。详见 `docs/verification/s3-readiness.md`。

- 2026-10-19 S3 本机验收完成：用户明确转交完整 S3，保留后续人工验收。AI 补齐 RollbackSession 的时间线预测、最早错帧、先纠正后新增、停等纠正、延后安全退休及注释；补充 9 个边界 case，移除两类排除。37 项定向检查与完整 V5 1000/1000 通过；默认 734 项 .NET +16 项 Python，14.53 秒，0 warnings/errors，树哈希不变。8 组故障探针均捕获并恢复；独立核心审查无发现。CI 已配置 push/PR 自动完整 V5，远端尚未运行（本轮无提交推送）。详细证据及后续限制见 `../verification/s3-rollback.md`。用时未精确统计，费用未统计；本轮未有核心实施错误被检查抓到，探针为主动注入；独立文档复核指出将 E 整体写成完成与尚无远端 CI 运行矛盾，已明确本机完成与远端待证实的边界。
