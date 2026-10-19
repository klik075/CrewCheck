#!/usr/bin/env bash
# 1단계(관측 장치) 확인 스크립트. Git Bash에서 loadtest 폴더에서 실행:  bash check-step1.sh
#  1) Prometheus가 서버 지표를 긁어 가고 있는지 (targets UP)
#  2) Grafana가 떠 있는지
#  3) 게임을 하나 만들고 상태를 5번 조회한 뒤, 게임 잠금 보유 시간(game.lock.hold)을 출력
# 준비: 서버(SERVER_MODE=multi, 프로필 local,loadtest)와 docker-compose.loadtest.yml이 켜져 있어야 한다
base=${BASE_URL:-http://localhost:8080}

echo "== 1. Prometheus targets"
health=$(curl -s http://localhost:9090/api/v1/targets | grep -o '"health":"[a-z]*"' | head -1)
echo "   ${health:-응답 없음 (docker compose -f docker-compose.loadtest.yml up -d 했는지 확인)}"

echo "== 2. Grafana"
code=$(curl -s -o /dev/null -w '%{http_code}' http://localhost:3000/api/health)
echo "   HTTP $code (200이면 정상)"

echo "== 3. 게임 하나 만들고 상태 5번 조회"
token=""; players=""
for i in 1 2 3 4; do
  res=$(curl -s -X POST "$base/api/members/guest" -H "Content-Type: application/json" \
        -d "{\"guestUuid\":\"lock-check-$RANDOM$RANDOM\"}")
  uid=$(echo "$res" | grep -o '"userId":[0-9]*' | cut -d: -f2)
  if [ -z "$uid" ]; then echo "   게스트 로그인 실패: $res"; exit 1; fi
  [ -z "$token" ] && token=$(echo "$res" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)
  players="$players{\"playerId\":$uid,\"nickname\":\"p$i\"},"
done
game=$(curl -s -X POST "$base/api/v1/games" -H "Authorization: Bearer $token" \
       -H "Content-Type: application/json" \
       -d "{\"roomId\":\"lock-check\",\"players\":[${players%,}]}")
gameId=$(echo "$game" | grep -o '"gameId":"[^"]*"' | cut -d'"' -f4)
if [ -z "$gameId" ]; then echo "   게임 생성 실패: $game"; exit 1; fi
echo "   gameId: $gameId"
for i in 1 2 3 4 5; do curl -s -o /dev/null -H "Authorization: Bearer $token" "$base/api/v1/games/$gameId"; done

echo "== 4. 게임 잠금 보유 시간 (game.lock.hold, 서버를 켠 뒤 누적)"
m=$(curl -s "$base/actuator/metrics/game.lock.hold")
stat() { echo "$m" | grep -o "\"statistic\":\"$1\",\"value\":[0-9.eE+-]*" | cut -d: -f3; }
count=$(stat COUNT); total=$(stat TOTAL_TIME); max=$(stat MAX)
if [ -z "$count" ]; then echo "   지표 없음: $m"; exit 1; fi
awk -v c="$count" -v t="$total" -v m="$max" 'BEGIN {
  printf "   COUNT      %d번\n", c
  printf "   TOTAL_TIME %.3f초\n", t
  printf "   MAX        %.1fms (최근 몇 분 안의 최댓값)\n", m*1000
  if (c > 0) printf "   평균       %.2fms\n", t/c*1000
}'
