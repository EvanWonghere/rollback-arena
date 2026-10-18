# S1 确定性数学契约

生产程序集 `Arena.Sim`：netstandard2.1、C# 9，无 NuGet 包、UnityEngine、浮点数或外部状态。所有数值方法只读取参数、显式随机状态和私有只读查找表。

## 标量

`Fixed64` 用有符号 long 保存 Q32.32，实际值为 `RawValue / 2^32`。最小步长 `u = 2^-32`，范围为 `[-2147483648, 2147483647.999999999767...]`。

| API | 行为 |
| --- | --- |
| `FromRaw` / `FromInt` | 分别保留 raw / 将 int 精确缩放为 raw |
| `Add`、`Sub`、`Negate` | 溢出饱和；`-MinValue == MaxValue` |
| `Mul`、`Div` | 最近舍入，半途远离零；舍入后的结果溢出时饱和 |
| `Div`、`/` | 除数为 0 时抛 `DivideByZeroException`，包括 0/0 |
| `Sqrt` | 非负数的最近可表示平方根；负数抛 `ArgumentOutOfRangeException` |
| `Abs` | `Abs(MinValue) == MaxValue` |
| `Min` / `Max` | 按 raw 比较 |

`Mul` 由用户实现并拥有；本轮 AI 仅增加其余 API，保留乘法方法的原始文本。`operator *` 继续进入这一个方法。

乘法和除法非饱和时的舍入误差不超过 `u/2`；平方根也不超过 `u/2`。饱和后的值只保证范围和方向，不能再用该误差界描述原来的数学结果。

除法先求整数商，再逐位求 32 位小数，余数始终小于除数幅值且除数不超过 `2^63`，余数左移一位放得进 ulong。平方根对概念上的 `raw << 32` 逐两位求根，根不超过 48 位。两者均不调用浮点、宽整数库或乘法参考实现。

## 向量

`FixVector3` 是只含三个 `Fixed64` 字段的值类型，无共享引用状态。

- 分量加减、取负、标量乘除复用标量语义。
- `Dot(a,b)` 精确定义为 `(a.X*b.X + a.Y*b.Y) + a.Z*b.Z`，每次乘法和加法分别舍入、饱和。`LengthSquared` 是 `Dot(v,v)`。
- 饱和算术不满足结合律；点积在极大项相消时不等于“无限精度求和后只饱和一次”。S2 应给游戏坐标和速度设定实际范围，不能用代数重排悄悄改变语义。
- `Length` 和 `Normalized` 先除以最大分量幅值，再计算缩放向量的模。这样不会因原始平方饱和而失真，也不会因微小分量平方被舍入为零而丢失非零方向。
- 零向量的长度和归一化均为零。向量除以零标量仍抛异常。

全分量范围内，令理想长度 `L` 为精确欧氏长度夹到可表示范围：

```text
abs(actualLength - L) <= 16*u*max(1,L)
每个 normalized 分量与理想单位向量对应分量的绝对误差 <= 16*u
```

`Abs(MinValue)` 的饱和不破坏上述界；含该值的向量单独覆盖。测试参考对 raw 分量用 BigInteger 精确平方、求和，再用独立 Newton 整数根计算长度；归一化参考根额外保留 64 位二进制小数，不复用生产缩放路径。比较长度时给予 oracle 最后量化的 1 raw 裕量。

## 三角函数

`FixMath.Sin/Cos` 输入为弧度。`Pi`、`HalfPi`、`TwoPi` 各自量化到最近的 raw，因此三个常量不是严格的 raw 整数倍关系。

运行时将角度按 `TwoPi.RawValue` 归一化后，查询 4096 区间的正弦表并作整数线性插值。余弦通过索引移动四分之一周求值，不对原始角度作可能饱和的加法。插值不调用 `Fixed64.Mul`。

- `[-1024,1024]` 弧度上，与真实 Sin/Cos 的绝对误差目标为 `1e-6`。
- 全 raw 范围的输入都应返回 `[-One,One]` 中的确定结果，但巨大角度按量化周期取模会累积相位误差，**不承诺**整个 raw 范围都满足 `1e-6`。
- 游戏中应将朝向角保持在单个周期，避免无限累积角度。
- 表为私有数组，不提供可写引用，运行时不生成浮点数据。

生成器只用 Python 标准库 Decimal，80 位工作精度，固定 pi 常量和半途远离零的舍入，输出 UTF-8/LF：

```sh
python3 tools/generate_trig_table.py
python3 tools/generate_trig_table.py --check
```

`--check` 不写文件；默认检查和 CI 均运行它。表一致性检查与独立 `System.Math.Sin/Cos` 的百万组精度检查互补，避免将表与自身比较当作精度证明。

## 随机数

`DeterministicRandom` 使用 [Vigna 的 SplitMix64 原始算法](https://prng.di.unimi.it/splitmix64.c)，见 `third-party-notices.md`。这是显式种子的模拟随机源，不用于安全用途。

- 只有一个 ulong 状态；任意 seed（包括 0）有效。默认结构体与 seed=0 一致。
- `State` 返回下一次采样前的状态；以该值构造新实例可恢复序列。
- 结构体复制后两份状态独立演进。模拟快照必须包含这个状态，不能把可变随机数保存在只读字段上而意外对副本采样。
- `NextUInt64` 的整数回绕属于算法，显式 `unchecked`。
- `NextInt(bound)` 返回 `[0,bound)`，用拒绝采样消除模偏差；`bound <= 0` 抛异常，且不消耗状态。拒绝采样可能消耗多次原始采样，因此不能假定每次 `NextInt` 都恰好推进一次状态。

黄金序列来自另行编译的无符号 C 参考，不是两个生产实例彼此比较。测试用例生成器保持独立，不改成调用生产随机数。

## 验收与边界

```sh
dotnet test RollbackArena.sln -c Release --filter FullyQualifiedName~FixedMath
.ai/verify.sh
```

默认检查不再排除 `HumanTodo`，乘法与向量直接参与验收。V1 分别覆盖百万组乘法、除法、平方根、向量、三角角度；边界用例、oracle 自检和分布计数防止随机测试只走饱和路径。失败可用固定种子和用例序号重现。

这些检查证明当前 .NET 运行时的数值实现和 V3 架构边界；Unity EditMode、IL2CPP、WebGL 的 V6 和人工手感 V9 属于后续阶段，尚未验收。数学完成也不代表 S2 游戏模拟或 S3 回滚调度已完成。
