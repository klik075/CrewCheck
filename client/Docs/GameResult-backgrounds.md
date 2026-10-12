# 게임 종료 화면 배경

원본 `D:/dev/1`의 PNG 4장을 수정 없이 `Assets/Resources/GameResult`로 복사했습니다.
`GameResultPanel.Show()`에서 로드하므로 기존 GameScene / GameScene2 / GameScene3 / GameScene4의 결과 패널에 공통 적용됩니다.

| 내 결과 | 파일 |
| --- | --- |
| CREW 승리 + 내 진영 CREW | CrewVictory.png |
| PIRATE 승리 + 내 진영 PIRATE | PirateVictory.png |
| NEUTRAL 승리 + 내 진영 NEUTRAL | PersonalVictory.png |
| 승리 진영과 내 진영이 다름 | Defeat.png |
| 취소, 미종료, 승리 진영 또는 내 진영 정보 없음 | 승패 배경 숨김, 기본 결과 패널 사용 |

- 이미지 비율을 유지하며 화면을 채웁니다. 화면 비율에 따라 양옆 또는 상하가 일부 잘릴 수 있습니다.
- 결과 정보는 아래쪽 반투명 패널에 표시하며 최대 12인 목록에 맞춰 글자 크기를 조절합니다.
- 게임 취소 시 앞서 표시한 승리/패배 배경을 제거합니다.
- 개인 승리 판정은 기존 게임의 `winner`와 `/me.faction` 비교와 같습니다. 현재 DTO에는 개인별 승자 ID가 없습니다. 서로 다른 중립 직업의 개인 승패를 구분하는 서버 결과가 추가되면 이 분기도 함께 확장해야 합니다.

## 검증

`Tools/ResultScreenAudit/CaptureResults.cs`는 별도 Unity 프로젝트의 Editor에서 실행하는 검증 스크립트입니다.
프로젝트의 실제 GameResultPanel, GameConstants, GameDtos, GameScreenText, ReportFormatter, GameStateQueries, AbilityRules, PhaseTracker와 Resources/GameResult, Fonts, TextMesh Pro 리소스를 사용합니다.

9가지 진영 비교, 취소 3종, 데이터 누락/미종료, 4개 PNG 로드, 12인 목록 넘침, 배경 채움, 중복 생성, 화면 닫기/재표시, 복귀 버튼 이벤트를 확인하고 4개 미리보기를 저장합니다.
