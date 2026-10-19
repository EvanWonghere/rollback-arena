#!/usr/bin/env bash
# 本项目的「完成」检查。Stop hook 会在 agent 结束一轮之前运行它；CI 也跑同一个脚本。
# 只放一分钟内能跑完、结果确定的检查。退出码非 0 = 没完成。
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

python3 tools/generate_trig_table.py --check
python3 -B -m unittest discover -s tests/tools -p 'test_*.py'
dotnet build RollbackArena.sln -c Release -nologo -v q
# 默认运行完整套件，V5 使用固定 16 seeds；1000 seeds 由 tools/verify_v5.sh 单独核对。
dotnet test RollbackArena.sln -c Release --no-build -nologo
