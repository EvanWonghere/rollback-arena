# S2 无头模拟验收（2026-10-18）

## 范围与来源

权威玩法来自用户指定的本地分支 `docs/s2-gameplay-spec`，文档提交 `c384874`。将其 `docs/specs/0002-gameplay.md` 加入当前分支时先确认目标文件不存在；未切换分支，未覆盖或检出其他分支的整目录。实施选择见 `../specs/0003-s2-implementation.md`。

S1 已提交为 `e7e4c01370477b713da6b198128e3f1fd6c2001b` 并推送到 `origin/feat/s1-fixed64-tests`，用 `git ls-remote` 确认远端 hash 相同。S2 是之后的本地改动，没有另行提交或推送；没有改动 `Fixed64.Mul` 或实现回滚调度。

## 实际检查

工具环境 PATH 未包含 SDK，执行时使用本机已安装 SDK，或仅在子进程中前置其目录，未更改用户 shell、Cursor 设置。

| 检查 | 命令与观察 |
| --- | --- |
| 16 组定稿场景 | `dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~Gameplay` 的首轮场景子集：29 项通过；随后补充斜向、严格竖直边界、空中限制、落下操作和超时优先级等边界 |
| S2 定向检查 | `dotnet test RollbackArena.sln -c Release --filter 'FullyQualifiedName~Gameplay\|FullyQualifiedName~Simulation'`：202 项全部通过，0 失败/跳过 |
| V2 | 2 个固定种子，每个 6 次 × 8000 帧，共 96,000 步；每步与首轮校验和一致。另测输入改变后在对应帧立即有不同校验和 |
| 状态字段 | GameState 共有 45 个叶字段；每个字段都有 checksum 敏感性、快照捕获和快照恢复用例；状态与快照递归检查不含引用类型 |
| 独立 hash 黄金值 | Python 按 little-endian 显式编码初始状态的 189 个字节再算 FNV-1a，得到 `AC32AB911BBFFD1D`；生产与黄金值一致 |
| 真实 CLI | 录制、回放、文件比较、帧预算退出码与坏输入退出码均实际运行，见下文 |
| 默认验收 | `.ai/verify.sh`：Passed 629, Failed 0, Skipped 0, Total 629；0 warnings / 0 errors；退出 0；本机总耗时 10.36 秒，测试约 6 秒 |

最终默认检查对所有已跟踪和未忽略的新工作区文件做运行前后 SHA-256 比较，`WORKING_FILES_UNCHANGED=True`。之后仅写回本次文档结果。解决方案原先为 CRLF，SDK 加项目后保持该格式；默认 git 空白检查把行尾 CR 报成尾空白，使用保留原有空白规则并加 `cr-at-eol` 的一次性检查后通过，未改 Git 配置或统一改写文件换行。

以上是在当前 .NET 运行时执行的检查；Unity EditMode / IL2CPP / WebGL 的 V6、人工手感 V9 未运行。S3 的 SyncTest、回滚等价性、网络条件模拟、V7/V8 尚未实施。

## 真实机器人与回放

```sh
dotnet run --project tools/headless -c Release -- --bots --record inputs.csv --hashes live.csv
dotnet run --project tools/headless -c Release --no-build -- --replay inputs.csv --hashes replay.csv
```

实际输出：

```text
MATCH_OVER frames=1000 score=0:2 rounds=2 hits=12 checksum=AFD2C47A3AAF7FF5
REPLAY_OK frames=1000 score=0:2 rounds=2 hits=12 checksum=AFD2C47A3AAF7FF5
LIVE_REPLAY_HASH_FILES_IDENTICAL: True; hash rows=1000
```

机器人只是输入生成器，和用户输入走同一个 Step，没有直接改位置或判胜入口。不同攻击节奏避免完全镜像导致无限平局。测试同时要求 MatchOver、2 胜、实际受击和非零帧数，不能用单纯走出平台当作战斗验收。

额外真实 CLI：`--bots --max-frames 1` 输出 FRAME_LIMIT 并退出 1；空回放退出 2；保留输入位为 1 的回放退出 2。临时输入/哈希文件在检查后删除。

## 反向探针

每次只临时修改本轮新增文件，重新编译，并在 TRX 中检查实际执行与失败数量；随后恢复原始字节。用户乘法没有参与探针。

| 故意破坏 | 被哪项检查抓到 |
| --- | --- |
| 起手 6 帧改为 5 | 场景 #2：2/2 失败 |
| 跳跃先减一次重力再积分 | 最高点/落地时序：1/1 失败 |
| 用按住输入直接代替按下沿 | 场景 #16：1/1 失败 |
| 第一方生效后才计算第二方命中 | 同帧互中/玩家交换：1/1 失败 |
| 在 Step 增加隐藏可变静态计数影响位置 | V2 的两个种子均失败；这不是用 V3 的禁用 API 代替 V2 |
| 校验和漏掉 PreviousInput | 45 个字段覆盖用例中 2 个失败 |
| 快照恢复强制清零一方 PreviousInput | 45 个快照捕获用例中 1 个失败 |
| 回放读取不检查逐帧序号 | 6 个坏输入用例中 1 个失败 |
| 极小非零中心差误判为完全重合 | 极小距离推开方向测试：1/1 失败 |

所有 9 项探针均捕获并恢复。最终检查须从恢复后的源码重建，不能复用探针编译出的 DLL。

## 独立审查与修复

两个全新只读上下文从冻结文件包审查，避免读到探针临时破坏的代码。玩法审查未发现确定规则偏差。工具审查发现 AI 把 `InvalidDataException` 当作 `IOException` 的派生类型，导致坏表头、空回放、坏行没有被 Main 的错误处理捕获。

先新增 3 个经过 `Program.Main` 的回归用例，原实现实际 3/3 失败，均以未捕获 InvalidDataException 报错；随后显式捕获该异常，202 项定向检查通过，真实 CLI 空回放退出 2。原审查者复核该修复通过。这里的回归失败是实际缺陷证据，未删除或放宽断言。

## 状态、快照与校验和边界

- State 是两个 PlayerState 值字段和全局值字段，没有数组，因此快照值复制无共享引用。未来新增引用字段会使架构测试失败；不能在未调整快照设计前加入。
- 输入是 byte 按住状态；前有效输入必须随快照保存。MatchOver 后只有 Frame 增加，其余状态保持不变。
- 校验和按显式字段和字节顺序计算，不读取 padding，不用运行时 GetHashCode；新字段必须明确加入布局、黄金值和字段覆盖验收。
- 规格中“第 6 次命中从任意位置都出界”是动机说明而非完整几何保证；实现遵循精确速度和平台判据，没有按受击次数直接判输。
- 所有 S2 新生产逻辑均在 Arena.Sim 中，保持 C# 9 / netstandard2.1、无 NuGet、UnityEngine 或浮点；文件、CSV、机器人和命令行留在 headless 工具。
