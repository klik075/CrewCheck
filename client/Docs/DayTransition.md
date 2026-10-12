# 밤 결과·처형 컷신 (GameScene3)

밤 결과(NIGHT_RESULT)와 처형(EXECUTION) 때 전체 화면 컷신을 보여 줍니다. 스토리보드는 `Docs/UI/DayTransition_*.png`입니다.
`GameScreen`이 결과를 받으면 `DayTransitionView`(`Assets/02_Scripts/GameUI/DayTransitionView.cs`)를 실행할 때 만들어 재생합니다.
씬 파일은 고치지 않았습니다. 지금은 GameScene3(`GameLayoutTabs.PlaysDayTransitions`)에서만 켜져 있고,
다른 배치에서 쓰려면 그 배치의 `GameLayoutBody`에서 `PlaysDayTransitions`를 `true`로 바꾸면 됩니다.

| 결과 | 장면 | 멈춤 |
| --- | --- | --- |
| 밤사이 사망 | 밤이 걷힘 → 붉은 칼자국 → 희생자 수배서(입은 스킨의 흉상, 이름) → 새벽 + "N일차 낮" | 4.0초 |
| 선의 보호 | 칼자국까지 사망과 같음 → 금빛 고리가 끊음, "선의의 보호" → 새벽 | 3.7초 |
| 아무 일 없음 | 밤이 걷힘 → 고요한 밤바다 → 해돋이 + "N일차 낮" | 2.6초 |
| 처형 | 테이블 개표(0.8초, 게임 화면 그대로) → 판결문 + "처형" 도장 → 널빤지(입은 스킨) → 첨벙 → "N+1일차 밤" | 4.5초 |
| 처형 없음 | 테이블 개표 → 판결문 "판결 불발" + "무효" 도장이 갈라짐 → "N+1일차 밤" | 2.8초 |

- **다음 페이즈와 맞물림**: 마지막 명패에서 멈춰 있다가 다음 페이즈(낮·밤)가 오면 레터박스가 열리며 걷힙니다.
  다음 페이즈가 늦으면 최대 2.5초 기다린 뒤 걷힙니다. 화면을 누르면 마지막 명패로 건너뛰고 바로 걷힙니다.
- **재생 조건**: 결과가 그 결과 페이즈 안에서 왔을 때만. 재접속해 들어온 페이즈의 결과는 공지·채팅으로만 보여 줍니다.
- **게임이 끝난 결과**(ENDED에서 받은 결과): 다음 날 명패 없이 걷히고, 게임 결과 화면은 컷신이 끝난 뒤 뜹니다.
- **공개 정보만**: 죽은 사람의 닉네임과 테이블에서 입은 스킨만 씁니다. 직업, 공격한 사람, 보호받은 사람은 보여 주지 않습니다.
  내가 죽거나 처형되면 "당신은 사망했습니다" 띠와 "당신은 처형되었습니다" 문구로 바뀝니다.
- 컷신이 재생되는 동안에도 공지·채팅·명단은 그대로 갱신됩니다(컷신 아래에서).

## 그림

| 파일 | 내용 |
| --- | --- |
| `Resources/DayTransition/ExecutionPlank.png` | 널빤지 장면 배경. 캐릭터는 코드가 널빤지 끝(가로 83%, 위에서 45%)에 세운다 |
| `Resources/DayTransition/ExecutionSplash.png` | 첨벙 장면. 아래 띠에 처형 문구를 쓴다 |
| `Resources/DayTable/Deck.png` | 새벽 배경 |
| 해적 공용 밤 방 (`NightSceneBackgrounds.pirateShared`) | 고요한 바다, 처형 뒤 밤 배경 |

처형 그림 두 장은 스토리보드를 바탕으로 Gemini로 다시 그린 것에서 메모·자막·캐릭터·Gemini 별 표시를 지우고 위아래 검은 띠를 잘라 낸 것입니다(576×812).
칼자국·금빛 고리·햇살·도장·물결·상어 지느러미는 그림 없이 코드로 만듭니다(`NightFxSprites`와 이 클래스의 지느러미).

## 검증

`Tools/DayTransitionAudit/CaptureTransitions.cs`가 별도 Unity 프로젝트에서 장면마다 프레임을 그려 저장합니다(README 참고).
그 프레임을 모은 것이 `Docs/UI/DayTransition_UnityPreview.png`입니다.
