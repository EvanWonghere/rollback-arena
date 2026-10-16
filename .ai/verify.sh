#!/usr/bin/env bash
# 本项目的「完成」检查。Stop hook 会在 agent 结束一轮之前运行它；CI 也跑同一个脚本。
# 只放一分钟内能跑完、结果确定的检查。退出码非 0 = 没完成。
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

dotnet build RollbackArena.sln -c Release -nologo -v q
dotnet test RollbackArena.sln -c Release --no-build -nologo
