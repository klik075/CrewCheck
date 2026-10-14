# 연출 효과음 가이드 (IamREALcitizen client)

`PirateBGM.java sfx`로 생성한 연출용 효과음 83개(44.1kHz / 16-bit 스테레오 WAV)입니다.
현재 게임 씬의 연출은 소리를 하나도 내지 않습니다. 오디오 호출은 Title 씬의 버튼음뿐이라, 아래 표의 위치에 재생 호출을 넣으면 됩니다.

## 쓰는 법

1. WAV는 `Assets/05_Audio/SFX/`에 있습니다. 원본 생성기는 `bgm` 폴더의 `PirateBGM.java`입니다.
2. 파일마다 같은 이름의 `AudioClipContainer`를 만듭니다(Create > System/Audio/ClipContainer).
3. `AudioSystemManager`의 ContextMenu **Setup AudioClip Containers**로 등록합니다.
4. 코드에서 재생합니다.

```csharp
var sfx = AudioSystemManager.Play("SFX_Seq_NightKilled");
// 탭으로 스킵하면 남은 소리를 짧게 정리
sfx?.FadeOut(0.15f);
```

- **`SFX_Seq_*`**: 컷신 전체를 한 파일에 담은 버전입니다. 컷신이 시작할 때 한 번만 재생하면 됩니다. 타이밍은 지금의 `DayTransitionSettings.asset` 실제 값(코드 기본값의 약 1.25배) 기준입니다.
- **`SFX_Cut_*` 등 구간 파일**: 각 순간에 따로 재생하는 조각입니다. 에셋 타이밍이 바뀌었거나, 크라켄 희생자가 2명 이상이거나, 스킵을 정확히 처리하고 싶을 때 씁니다. 아래 표의 초는 컷신 시작 기준 실제 시간입니다.
- 볼륨은 큰 연출(컷신, 결과)이 크고 UI 소리(틱, 채팅)가 작도록 미리 맞춰 두었습니다. 세부 조정은 컨테이너의 `Volmue`로 하면 됩니다.

---

## 1. 밤 결과 · 처형 컷신 (`DayTransitionView`)

### 한 번에 재생하는 전체 버전

| 파일 | 연출 | 재생 시점 | 길이 |
|---|---|---|---|
| `SFX_Seq_NightKilled` | 밤사이 사망 | `GameScreen.PlayNightTransition` → Killed | 7.0s |
| `SFX_Seq_NightProtected` | 선의의 보호 | 〃 → Protected | 6.5s |
| `SFX_Seq_NightQuiet` | 아무 일 없음 | 〃 → Quiet | 5.0s |
| `SFX_Seq_NightKraken` | 크라켄 (희생자 1명 기준) | 〃 → Dragged | 7.5s |
| `SFX_Seq_Executed` | 투표로 처형 | `GameScreen.PlayExecutionTransition` → Executed | 8.0s |
| `SFX_Seq_NotExecuted` | 처형 없음 (동률·무투표) | 〃 → NotExecuted | 5.5s |

### 구간 파일과 실제 재생 시각(초)

| 파일 | 내용 (화면 의성어) | Killed | Protected | Quiet | Kraken | Executed | NotExec |
|---|---|---|---|---|---|---|---|
| `SFX_Cut_NightLift` | 밤이 걷힘: 바람·파도 | 0.00 | 0.00 | 0.00 | 0.00 | | |
| `SFX_Cut_Slash` | 칼자국 3줄 **촤악!** | 0.86 | 0.86 | | 0.88 (같은 밤 습격도 있을 때) | | |
| `SFX_Cut_Heartbeat` | 붉은 맥박 ×2 | 1.20 | 1.20 | | | | |
| `SFX_Cut_PosterDrop` | 수배서가 떨어짐 | 1.88 | | | 2.75 | | |
| `SFX_Cut_XMark` | 붉은 X 두 번 | 2.15 | | | | | |
| `SFX_Cut_DeathBell` | 조종 **댕-** | 2.38 | | | | | |
| `SFX_Cut_Shield` | 금빛 고리 **챙!** + 성가 | | 1.94 | | | | |
| `SFX_Cut_CalmSea` | 고요한 밤바다 **쏴아...** | | | 0.80 | | | |
| `SFX_Cut_KrakenRise` | 바다 밑 진동, 물결 고리 3개, 눈이 뜸 | | | | 0.88 | | |
| `SFX_Cut_KrakenStrike` | 크라켄 돌진 **쿠과광!** (두 번 강타) | | | | 1.63 | | |
| `SFX_Cut_DragUnder` | 끌려 들어감 **촤르륵** + 거품 | | | | 3.75 | | |
| `SFX_Cut_Dawn` | 새벽: 뱃고동·갈매기 | 4.00 | 3.50 | 2.00 | 4.50 | | |
| `SFX_Cut_DrumRoll` | 개표 **두구두구...** | | | | | 0.19 | 0.19 |
| `SFX_Cut_Scroll` | 판결문이 펼쳐짐 | | | | | 1.13 | 1.13 |
| `SFX_Cut_SealExecute` | 처형 도장 **쾅!** + 금관 | | | | | 1.75 | |
| `SFX_Cut_SealVoid` | 무효 도장 **쾅!** | | | | | | 1.75 |
| `SFX_Cut_SealCrack` | 도장 갈라짐 **뚝** | | | | | | 2.19 |
| `SFX_Cut_PlankTremble` | 널빤지·상어 지느러미 + **덜덜** | | | | | 2.25 | |
| `SFX_Cut_Fall` | 떨어지는 소리 | | | | | 3.40 | |
| `SFX_Cut_Splash` | **첨벙!** | | | | | 3.87 | |
| `SFX_Cut_Nightfall` | 밤이 찾아옴: 종 두 번·바람 | | | | | 4.87 | 2.75 |
| `SFX_Cut_Plaque` | "N일차 낮/밤" 명패 쿵 | 4.42 | 3.95 | 2.45 | 4.95 | 5.30 | 3.15 |

- **크라켄 희생자가 2명 이상이면** 희생자 구간이 ×min(1.8, 1+0.22·(n−1))로 길어집니다. 이때는 전체 버전 대신 구간 파일을 쓰고, `DragUnder`·`Dawn`·`Plaque`를 늘어난 만큼 뒤로 미루세요.
- 탭 스킵은 컷신을 마지막 명패로 바로 보냅니다. 전체 버전을 `FadeOut` 하고 `SFX_Cut_Plaque`만 재생하면 자연스럽습니다.
- 게임이 끝나는 컷신(`GameOver`)은 마지막 명패가 빠집니다. 구간 파일로 처리할 때 `Plaque`를 생략하세요.

## 2. 능력 컷인 (`AbilityCutInView`, 1.58s)

재생 위치: `NightRoomView.AbilityRoutine`에서 컷인이 시작될 때. 그 밤 첫 선택에서만 재생됩니다.

| 파일 | 대상 |
|---|---|
| `SFX_CutIn_Pirate` | 해적 · 앵무새 · 요리사 |
| `SFX_CutIn_Crew` | 선장 · 선의 · 망루지기 · 갑판장 · 주정뱅이 · 원숭이(변장) |
| `SFX_CutIn_Neutral` | 세이렌 · 크라켄 |

t=0에 섬광과 함께 아트가 오른쪽에서 들어오는 휙 소리와 진영별 효과음(해적 단조 금관, 선원 장조 팡파레, 중립 신비한 합창)이 나고, 1.3초에 왼쪽으로 빠지는 소리가 이어집니다.

## 3. 직업 모션 (`NightAbilityMotion`)

컷인 직후, 또는 다시 고를 때 단독으로 재생합니다.

| 파일 | 액션 (직업) | 길이 |
|---|---|---|
| `SFX_Motion_Attack` | SELECT_ATTACK_TARGET (해적): 칼 두 번 | 1.0s |
| `SFX_Motion_ParrotWatch` | WATCH_ACTION (앵무새): 날갯짓·울음·망원경 | 1.35s |
| `SFX_Motion_Compass` | INVESTIGATE_FACTION (선장): 나침반 래칫 → 차임 | 1.45s |
| `SFX_Motion_Shield` | PROTECT (선의): 빛의 방패·성가 | 1.5s |
| `SFX_Motion_Spyglass` | WATCH_VISITORS (망루지기): 망원경 펼침 + 빛줄기가 좌→우로 훑음 | 1.5s |
| `SFX_Motion_RopeSeal` | BLOCK (갑판장): 밧줄 조임 → 0.42초 탁! | 1.35s |
| `SFX_Motion_SpiritWisps` | READ_CORPSE_ROLE (주정뱅이): 딸꾹·술병·혼령 | 1.6s |
| `SFX_Motion_CookBan` | BAN_VOTE (요리사): 칼 가는 소리 두 번·냄비 뚜껑 | 1.0s |
| `SFX_Motion_SirenSeduce` | SEDUCE (세이렌): 노랫소리·하프 | 1.0s |
| `SFX_Motion_KrakenMark` | KRAKEN_MARK (크라켄 표식): 깊은 물방울·소나 | 1.0s |
| `SFX_Motion_KrakenStrike` | KRAKEN_STRIKE (크라켄 발동): 포효·진동 | 1.0s |

## 4. 선장 조사 보고 (`InvestigationRevealView`, 3.85s)

| 파일 | 재생 시각 |
|---|---|
| `SFX_Seq_InvestigatePirate` / `SFX_Seq_InvestigateCrew` | 0.00 (결과별 전체 버전) |
| `SFX_Report_Open` | 0.00 양피지 올라옴·초상 등장 |
| `SFX_Report_CompassSpin` | 0.25 바늘 3바퀴 (점점 느려짐) |
| `SFX_Report_StampPirate` / `SFX_Report_StampCrew` | 1.40 도장 **쾅** (해적 = 단조, 선원 = 장조) |
| `SFX_Report_Close` | 3.40 명단 칸으로 날아감 |

첫 탭은 1.45초(도장 직후)로 넘어갑니다. 이때 전체 버전을 `FadeOut` 하고 `Stamp`를 바로 재생하세요. 재접속으로 이미 찍힌 상태로 열릴 때는 소리를 내지 않는 것이 맞습니다.

## 5. 밤 개인 결과 (`NightReportRevealView`)

공통으로 `SFX_Report_Open`(0.00)과 `SFX_Report_Close`(holdEnd 시점)를 씁니다.

- **망루지기 (감시 보고)**
  - 방문자 i가 나타날 때(0.85 + 0.16·i)마다 `SFX_Report_Visitor`
  - 아무도 오지 않았으면 0.85에 `SFX_Report_NoVisitor`
- **주정뱅이 (시체 확인, 4.05s)**: 전체 버전 `SFX_Seq_CorpsePirate` / `Crew` / `Neutral`
  - 구간 파일로 할 때: `SFX_Report_Pour` 0.95 → `SFX_Report_CardFlip` 1.15 → `SFX_Report_RevealPirate` / `Crew` / `Neutral` 1.33 (카드 앞면이 보이는 순간)
- **앵무새 (관찰 보고)**
  - 0.55에 `SFX_Report_Feather`
  - 줄 i가 들어올 때(0.8 + 0.22·i)마다 `SFX_Report_Line`
- **차단당함 (BLOCKED, 1.45s)**: `SFX_Band_Blocked`

## 6. 세이렌

| 파일 | 연출 |
|---|---|
| `SFX_Seq_SirenSeduced` | 유혹당한 사람의 화면 (2.7s): 노랫소리 → 0.6초 세이렌 등장 → 1.5초 팀 패널, 초상 등장, 실 연결 |
| `SFX_Band_SeduceSuccess` | 세이렌 본인의 유혹 성공 띠 (1.45s) |
| `SFX_Band_SeduceFail` | 유혹 실패 띠 (1.25s) |

## 7·8. 투표

| 파일 | 재생 위치 |
|---|---|
| `SFX_Phase_Vote` | VOTE 시작 (`TryShowVoteHand`) |
| `SFX_Vote_HandShow` | 카드 패가 올라옴 (`VoteCardHandController.Show`) |
| `SFX_Vote_CardPick` | 카드 뽑기 (`Draw`) |
| `SFX_Vote_CardReturn` | 카드 되돌리기·기권 (`ReturnToHand`) |
| `SFX_Vote_Confirm` | [투표 완료] (`Lock`) |
| `SFX_Vote_CardThrow` | 내 카드가 대상에게 날아감 (`DayTableVoteCards` Throw, 0.55s 비행 + 착지) |
| `SFX_Vote_CardTap` | 카드가 테이블에 놓임: 첫 투척 때 내려놓기, 개표 더미(`ShowTally`)에서 카드 i마다 i·0.09초 |

## 10. 결과 화면 (`GameResultSequence`, 2s 인트로)

| 파일 | 배경 변형 |
|---|---|
| `SFX_Result_CrewVictory` | CrewVictory |
| `SFX_Result_PirateVictory` | PirateVictory |
| `SFX_Result_PersonalVictory` | PersonalVictory (세이렌·크라켄·유령 선장·인어) |
| `SFX_Result_Defeat` | Defeat (빗소리 포함) |

0.5초에 "승리/패배" 화음이 나고, 0.65 + 0.045·i초에 캐릭터 등장에 맞춘 반짝임이 이어집니다. BGM 세트의 `06/07_victory_*`는 이 인트로 뒤에 깔 음악으로 쓸 수 있습니다.

## 진행·HUD

| 파일 | 재생 위치 |
|---|---|
| `SFX_Game_Start` | GameScene3 진입 / 첫 밤 시작 |
| `SFX_Phase_Night` | `ShowPhase(NIGHT)`. 단, 처형 컷신이 방금 밤을 알렸으면 생략 (1일차 밤·재접속용) |
| `SFX_Timer_Tick` | 남은 5초 동안 초가 바뀔 때마다 (`GameScreen.Update`의 타이머 갱신). 결과 화면 자동 복귀 카운트다운에도 사용 가능 |
| `SFX_Timer_TimeUp` | 타이머가 0이 되어 "판정 중"이 될 때 |
| `SFX_UI_Notice` | 공지 박스 팝업 (`GameHudLayout.ShowNotice`) |
| `SFX_UI_Chat` | 다른 사람의 채팅 도착 (`GameChatController.LiveMessageReceived`) |
| `SFX_Game_MyDeath` | 내가 사망 (`GameScreen.ShowPlayerDied`) |

## 참고

- 사망·처형 컷신은 명패까지 5.0~5.6초가 걸려서, 5초짜리 결과 페이즈보다 길 수 있습니다(`Docs/DayTransition.md`). 다음 페이즈가 컷신 위로 시작되어도 효과음은 끝까지 재생됩니다.
- 다시 만들거나 고치려면 `bgm` 폴더에서 `java PirateBGM.java sfx`를 실행하세요. 연출별 효과음은 `PirateBGM.java`의 `SFX` 구역(`Part` 정의와 `sfxSet()`)에 있습니다.
