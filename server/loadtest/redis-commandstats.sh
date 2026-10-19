#!/usr/bin/env bash
# 테스트 전/후 Redis 명령 횟수를 찍어 "목록 조회 1번당 GET 몇 번"을 계산할 때 쓴다.
#   ./redis-commandstats.sh before   →  k6 실행  →  ./redis-commandstats.sh after
#   diff 결과에서 cmdstat_get / cmdstat_smembers calls 증가량을 비교
set -euo pipefail
CONTAINER=${REDIS_CONTAINER:-whoisntcitizen-redis}
OUT_DIR="$(dirname "$0")/results"
mkdir -p "$OUT_DIR"
docker exec "$CONTAINER" redis-cli INFO commandstats | grep -E 'cmdstat_(get|set|smembers|hget|hgetall|hset|evalsha|eval|mget)' > "$OUT_DIR/redis_$1.txt"
echo "saved $OUT_DIR/redis_$1.txt"
if [[ "$1" == "after" && -f "$OUT_DIR/redis_before.txt" ]]; then
  awk -F'[:=,]' 'NR==FNR{b[$1]=$3; next} {print $1, "calls +" ($3-b[$1])}' "$OUT_DIR/redis_before.txt" "$OUT_DIR/redis_after.txt"
fi
