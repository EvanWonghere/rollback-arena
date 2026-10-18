# rollback-arena

1v1 3D 擂台对战小游戏，用确定性模拟 + 回滚（GGPO 式）做网络同步。纯逻辑层是 C#（.NET），表现层是 Unity（S4 才建）。
方案与阶段划分见 `docs/specs/0001-rollback-arena.md`，动手前先读当前阶段那一行的 done-when。

## 命令

| 做什么 | 命令 | 备注 |
| --- | --- | --- |
| 构建 | `dotnet build RollbackArena.sln -c Release` | 警告即错误（`Directory.Build.props`） |
| 测试 | `dotnet test RollbackArena.sln -c Release` | S1/S2 全部测试已纳入默认检查；按区域筛选用 `--filter FullyQualifiedName~Gameplay` 或 `~Simulation` |
| 只跑乘法测试 | `dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~Fixed64MulTests` | 用户已完成 `Fixed64.Mul`，197 个乘法测试通过 |
| 本项目的「完成」检查 | `.ai/verify.sh` | 生成表一致性 + 构建 + 全部测试；S2 验收 629 项，本机本轮约 10 秒；CI（`.github/workflows/ci.yml`）跑同一个脚本 |
| 无头机器人对战 | `dotnet run --project tools/headless -c Release -- --bots` | 正常结束输出 MATCH_OVER；可加 `--record inputs.csv --hashes live.csv` |
| 输入回放 | `dotnet run --project tools/headless -c Release -- --replay inputs.csv --hashes replay.csv` | 校验和日志记录每步完成后的状态，帧号从 1 开始 |

SDK 由 `global.json` 固定在 9.0.1xx；测试项目是 net9.0。

## 不知道就会出错的约束

- `Arena.Sim`、`Arena.Rollback` 要原样放进 Unity：只能 `netstandard2.1` + C# 9，不能引用 UnityEngine 或任何 NuGet 包。
  判据：`GuardedAssemblyOnlyReferencesAllowedAssemblies` 通过。不要在这两个项目里用 C# 10+ 语法（file-scoped namespace、global using、record struct 等）。
- 这两个程序集里不能出现 `float`/`double`/`Half` 和 `System.Numerics` 的浮点向量、`System.Random`、
  `DateTime`/`DateTimeOffset`/`Stopwatch`/`Environment.TickCount`、`HashCode`、`Guid.NewGuid`，
  也不能遍历 `Dictionary`/`HashSet`（含 `.Keys`/`.Values`/`CopyTo`），不能用 `Hashtable`/`ConcurrentDictionary`。完整规则表在扫描器开头。判据：V3 架构守卫
  `tests/Arena.Tests/Architecture/DeterminismGuardTests.cs` 通过。守卫扫描的是编译出的 IL，不看源码；不要为了让它变绿去改守卫的规则表。
- 守卫的盲区，审查时要人工看：把哈希容器当成 `IEnumerable`/`ICollection`/`ISet` 等通用接口传出去再遍历（比如交给 LINQ）；
  `string.GetHashCode()`（每个进程种子不同，IL 里和 `object.GetHashCode` 分不开）。
- 给守卫加新规则时，必须在 `GuardFixtures.cs` 加一个故意违规的夹具和对应的 `ScannerCatchesFixture` 用例，先看它变红。
- 状态只用值类型和定长数组（快照要整块复制）；模拟只接受「帧号 + 输入」，不读任何外部状态。
- **定点乘法和回滚调度由人先写**（方案里的对照实验）。AI 不要实现 `Fixed64` 的乘法、`RollbackSession` 的调度逻辑，除非我在当次对话里明说。
- 由人实现的部分，测试先写好并标上 `[Category("HumanTodo")]`，`.ai/verify.sh` 暂时排除这个分类。
  **人实现 `Fixed64.Mul` 之后，删掉 `Fixed64MulTests` 上的 `HumanTodo` 分类，并去掉 `.ai/verify.sh` 里的 `--filter "TestCategory!=HumanTodo"`。**
- `Fixed64`（Q32.32）的规则：溢出一律饱和（`-MinValue == MaxValue`）；乘法四舍五入，恰好一半时远离零。
  参考实现在 `tests/Arena.Tests/Math/Fixed64Reference.cs`（BigInteger，只能放在测试项目里）。
- 测试项目里不要用 `Arena.Tests.Math` 这个命名空间：它会遮住 `System.Math`（`GuardFixtures.cs` 里用到了）。`Math/` 目录下的测试用 `Arena.Tests.FixedMath`。
- NUnit 固定在 4.4.0，4.6.1 在 SDK 9.0.100 下编译不过（见 `docs/pitfalls.md`）。

## 验证

- 完成的判据：`.ai/verify.sh` 退出码为 0，并且测试总数没有意外变少（看输出里的 `Total`）。
- 每一项新的确定性检查（V1–V8）都要做反向探针：故意注入不确定性，看它变红，再撤掉。结果写进汇报。
- 跑不了的检查：Unity EditMode / IL2CPP / WebGL（V6）、手感（V9）需要编辑器或人，交付时必须明说没跑。

## Git

- 从 `main` 开分支，经 PR 合回 `main`。提交信息用 conventional commits（`feat:`、`test:`、`docs:`、`ci:` …）。
- 提交、合并、推送都要我当次明确同意。仓库是公开的，不要提交任何密钥或本机路径。

## 进展记录

- 每个阶段结束时，在方案文件的「进展记录」里追加一行：用时、AI 做了什么、我做了什么、AI 犯的错被哪项检查抓到。

## 文档地图

- `docs/specs/0001-rollback-arena.md` —— 总方案（已定稿）：架构、验证设计 V1–V9、阶段与 done-when。
- `docs/pitfalls.md` —— 踩坑库：现象、根因、判据。动相关模块前先搜一下。
- `docs/specs/0002-gameplay.md` —— 用户定稿的 S2 玩法、数值与 16 组场景。
- `docs/specs/0003-s2-implementation.md` —— S2 实施选择；验收数据见 `docs/verification/s2-simulation.md`。
- `docs/handoff-*.md` —— 交接文档，只有最新一份有效：`docs/handoff-2026-10-18-s2.md`（S2 完成，下一步 S3）。
