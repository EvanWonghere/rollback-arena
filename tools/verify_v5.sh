#!/usr/bin/env bash
# 单独执行并核对完整 V5。每次使用全新的结果目录，避免把旧 TRX 当成本次通过。
# 核对真实调度与独立 oracle，拒绝少跑、重复、跳过和失败用例。
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

result_parent="${1:-${TMPDIR:-/tmp}}"
mkdir -p "$result_parent"
result_directory="$(mktemp -d "$result_parent/rollback-arena-v5.XXXXXX")"
seed_count="${ARENA_V5_SEEDS:-1000}"
echo "V5 results: $result_directory"

dotnet build RollbackArena.sln -c Release -nologo -v q
ARENA_V5_SEEDS="$seed_count" dotnet test RollbackArena.sln -c Release --no-build -nologo \
  --filter FullyQualifiedName~RollbackEquivalenceTests \
  --logger 'trx;LogFileName=v5.trx' --results-directory "$result_directory"
python3 -B tools/verify_v5_results.py "$result_directory/v5.trx" --expected-seeds "$seed_count"
