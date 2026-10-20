# 게임용 Redis 연결에 지연을 넣거나 뺀다 (4-2단계). Toxiproxy 관리 API(localhost:8474)를 부른다.
# 사용 (PowerShell, loadtest 폴더):
#   powershell -ExecutionPolicy Bypass -File .\redis-latency.ps1 -Ms 30     # Redis 응답마다 30ms(±5ms) 지연
#   powershell -ExecutionPolicy Bypass -File .\redis-latency.ps1 -Off       # 지연 제거
#   powershell -ExecutionPolicy Bypass -File .\redis-latency.ps1            # 지금 상태 보기
# 전제: 서버를 GAME_REDIS_PORT=26379로 켰다 (게임 상태·잠금만 프록시를 거친다)
param(
  [int]$Ms = 0,
  [int]$Jitter = 5,
  [switch]$Off
)
$api = "http://localhost:8474/proxies/game_redis/toxics"

# 이미 있는 지연을 먼저 지운다 (없으면 404라 무시)
try { Invoke-RestMethod -Method Delete "$api/latency" | Out-Null } catch {}

if ($Ms -gt 0 -and -not $Off) {
  $body = @{
    name = "latency"; type = "latency"; stream = "downstream"; toxicity = 1.0
    attributes = @{ latency = $Ms; jitter = $Jitter }
  } | ConvertTo-Json -Depth 3
  Invoke-RestMethod -Method Post $api -ContentType "application/json" -Body $body | Out-Null
  Write-Host "게임 Redis 지연 켜짐: ${Ms}ms (±${Jitter}ms)"
} elseif ($Off) {
  Write-Host "게임 Redis 지연 꺼짐"
}

$toxics = Invoke-RestMethod "$api"
if ($toxics.Count -eq 0) { Write-Host "현재: 지연 없음" } else { $toxics | ForEach-Object { Write-Host ("현재: {0} {1}ms" -f $_.name, $_.attributes.latency) } }
