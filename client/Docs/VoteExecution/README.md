# 투표 처형 연출용 캐릭터 20종

기존 스킨 디자인을 유지한, 몸에 선박용 밧줄이 감긴 전신 SD 이미지입니다. built-in imagegen으로 제작했습니다. 각 스킨의 원본을 정체성 참고로, 첫 번째 첩자 이미지를 밧줄과 연출 통일용 참고로 사용했습니다.

## 파일과 미리보기

- [20종 미리보기](preview.html): 체크 배경 / 밝은 배경 / 어두운 배경에서 확인하고 클릭하면 확대합니다.
- 이미지: `Assets/09_Image/Characters/<스킨>_SD/<스킨>_Bound.png` (예: `CrewCaptain_SD/CrewCaptain_Bound.png`). 다른 캐릭터 그림과 같은 폴더입니다.
- [제작 프롬프트 및 파일 명세](manifest.json): 입력 이미지, 공통 프롬프트와 스킨별 추가 문구, 생성 파일명, 해상도, 피벗 및 알파 경계를 기록했습니다. 해상도·피벗·알파 경계는 아래처럼 자른 뒤의 값입니다.
- 생성 결과(투명 RGBA, 1024 × 1535–1536px)를 알파 8 이상 영역 + 여백 4px로 잘랐습니다. 기존 `Standing` 스프라이트처럼 발끝이 이미지 아래 끝에 오고, 가로는 알파 영역 중앙입니다. 보이는 픽셀(알파 8 이상)은 그대로이고, 잘린 바깥의 생성 잔여물(알파 8 미만)만 버렸습니다.
- 가로·세로는 블록 압축이 되도록 4의 배수로 맞췄습니다. 남는 칸은 좌우·위쪽에 투명으로 채웠습니다.
- 20종 모두 완전 투명 픽셀이 있고, 알파 16 초과 기준 불투명 영역이 이미지 외곽에 닿지 않는 것을 확인했습니다.

## Unity 사용

`.meta`에 Sprite (2D and UI) / Single / Bilinear / Clamp / 알파 사용 / Mip Maps 끔 / 압축(Normal Quality, 다른 캐릭터 그림과 같음) / Max Size 2048을 지정했습니다. PPU는 256, 피벗은 하단 중앙(Bottom Center)입니다.

각 스킨 에셋(`ChibiCharacterSkin`)의 `bound` 칸에 연결돼 있습니다. 스킨 에셋은 `Assets/Resources/DayTable/Skins`(게임 테이블이 씀)와 `Assets/09_Image/Characters/<스킨>_SD`(캐릭터 프리팹이 씀) 두 벌이고, 둘 다 연결했습니다. `bound`가 비어 있으면 `Bound`는 `Standing`을 돌려줍니다.

```csharp
Sprite boundSprite = skin.Bound; // bound가 없으면 Standing
```

스킨 디자인 기준이며 실제 직업 ID와는 무관합니다. 그림이 알파 영역으로 잘려 있어서 표시 높이를 맞추면 캐릭터 키가 맞습니다.

투표 처형 컷신의 널빤지 장면에 연결했습니다(`GameScreen.DescribeDead` → `DayTransitionInfo.Figure`). 연출 시간 창(`DayTransitionTuner`) 미리보기도 같은 그림을 씁니다. Unity 에디터에서 실제 씬 재생 검증은 아직 하지 않았습니다.

## 목록

| 스킨 디자인 | 이미지 |
|---|---|
| 해적 첩자 | [PIRATE_SPY](../../Assets/09_Image/Characters/PirateSpy_SD/PirateSpy_Bound.png) |
| 의사 | [CREW_DOCTOR](../../Assets/09_Image/Characters/CrewDoctor_SD/CrewDoctor_Bound.png) |
| 크라켄 | [PIRATE_KRAKEN](../../Assets/09_Image/Characters/PirateKraken_SD/PirateKraken_Bound.png) |
| 세이렌 | [CONCEPT_SIREN](../../Assets/09_Image/Characters/ConceptSiren_SD/ConceptSiren_Bound.png) |
| 포수 | [CREW_GUNNER](../../Assets/09_Image/Characters/CrewGunner_SD/CrewGunner_Bound.png) |
| 해적 약탈자 | [PIRATE_RAIDER](../../Assets/09_Image/Characters/PirateRaider_SD/PirateRaider_Bound.png) |
| 선원 | [CREW_SAILOR](../../Assets/09_Image/Characters/CrewSailor_SD/CrewSailor_Bound.png) |
| 갑판장 | [CREW_BOATSWAIN](../../Assets/09_Image/Characters/CrewBoatswain_SD/CrewBoatswain_Bound.png) |
| 유령 선원 | [CREW_GHOST](../../Assets/09_Image/Characters/CrewGhost_SD/CrewGhost_Bound.png) |
| 선장 | [CREW_CAPTAIN](../../Assets/09_Image/Characters/CrewCaptain_SD/CrewCaptain_Bound.png) |
| 원숭이 항해사 | [CREW_MONKEY](../../Assets/09_Image/Characters/CrewMonkey_SD/CrewMonkey_Bound.png) |
| 망루지기 | [CREW_LOOKOUT](../../Assets/09_Image/Characters/CrewLookout_SD/CrewLookout_Bound.png) |
| 주정뱅이 | [CREW_DRUNK](../../Assets/09_Image/Characters/CrewDrunk_SD/CrewDrunk_Bound.png) |
| 해적 앵무새 | [PIRATE_PARROT](../../Assets/09_Image/Characters/PirateParrot_SD/PirateParrot_Bound.png) |
| 금화 원숭이 | [CREW_MONKEY_ALT](../../Assets/09_Image/Characters/CrewMonkeyAlt_SD/CrewMonkeyAlt_Bound.png) |
| 유령 선장 | [CONCEPT_GHOST_CAPTAIN](../../Assets/09_Image/Characters/ConceptGhostCaptain_SD/ConceptGhostCaptain_Bound.png) |
| 남성 요리사 | [PIRATE_COOK](../../Assets/09_Image/Characters/PirateCook_SD/PirateCook_Bound.png) |
| 남성 인어 | [THIRD_MERMAID_MALE](../../Assets/09_Image/Characters/ThirdMermaidMale_SD/ThirdMermaidMale_Bound.png) |
| 여성 인어 | [THIRD_MERMAID](../../Assets/09_Image/Characters/ThirdMermaid_SD/ThirdMermaid_Bound.png) |
| 여성 요리사 | [PIRATE_COOK_FEMALE](../../Assets/09_Image/Characters/PirateCookFemale_SD/PirateCookFemale_Bound.png) |
