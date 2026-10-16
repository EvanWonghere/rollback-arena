# 踩坑库

每条：日期、现象、根因、下次怎么发现（判据）。

## 2026-10-15 NUnit 4.6.1 在 SDK 9.0.100 下编译不过

- 现象：所有 `Assert.That(actual, constraint, message)` 报 `CS1503: cannot convert from 'int' to 'System.Action'`。
- 根因：未深究；NUnit 4.6.1 的 `Assert.That` 重载在 SDK 9.0.100 自带的编译器下解析失败，4.4.0 正常。
- 判据：升级 NUnit 或 SDK 后跑 `.ai/verify.sh`；本机与 CI 的 SDK 由 `global.json` 约束。

## 2026-10-15 `dotnet test -v q` 会吞掉失败信息

- 现象：守卫变红时只显示 `Failed: 1`，看不到哪一行违规。
- 根因：`-v q` 连失败测试的 Error Message 也不打印。
- 判据：`.ai/verify.sh` 里的 `dotnet test` 不要加 `-v q`；改动后做一次反向探针，确认输出里有具体的违规位置。
