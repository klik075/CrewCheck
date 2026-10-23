# 직업 구성 항해일지 적용

## 진입 및 동작

- 로비 → 방 만들기 → 직업 구성 수정하기에서 새 화면을 연다.
- `CreateRoomRoleEditor`가 원본 설정을 복제해 편집하며, 구성 적용 시에만 원본에 반영한다. X/취소/ESC는 이번 편집을 버린다.
- `RoleCompositionLayout`이 최상위 Canvas 아래에 전체 화면 오버레이를 만들며 안전 영역에 맞춘다. 닫기/부모 비활성화 시 오버레이도 숨긴다.
- 추천은 선택한 인원수만 복원한다. 다른 인원수의 직접 구성은 보존한다.
- 수량 변경은 기존 서버 규칙에 맞춰 남는 선원 자리를 다른 직업과 교환한다. 선원 수는 자동 계산한다.
- 랜덤을 켜면 직접 수량 조정을 잠그고 직업 수를 ‘—’로 표시한다. 진영별 인원은 해당 인원의 추천 배분을 사용한다.
- 랜덤을 끄면 직접 편집하던 구성을 복원한다. 후보 체크는 두 방식 모두에서 편집할 수 있다.
- 선원과 해적은 항상 후보이며, 중립은 직접 설정에서만 배정한다.
- 직업 목록은 서버 options.roles에서 생성한다. 현재 14종 모두 초상화를 연결하며 새 직업도 누락하지 않는다.
- 스크롤과 별개로 하단 합계/검증/적용 버튼을 고정한다. 현재 인원 구성과 다른 인원의 직접 구성 오류 모두 표시하고 적용을 막는다.

## 검증 결과

- 전체 C# 빌드 성공: 오류 0개, 경고 13개.
- 별도 Unity 프로젝트에서 실제 Lobby.unity의 Popup_JobCustom 구조로 렌더링하고 14개 직업/초상화, 버튼 raycast, 원본과 임시 편집 분리, 취소, 적용 콜백 1회, 랜덤 전환과 직접 구성 복원, 후보 저장, 인원별 구성, 현재/다른 인원의 잘못된 구성 차단을 검증했다.
- 세로·작은 모바일 화면·가로 화면을 포함한 7장 렌더링을 생성했다.
- 재현: `Tools/RoomLayoutAudit/CaptureRoles.cs`, 결과: `Tools/RoomLayoutAudit/RoleCompositionResult.txt`.
- 실제 서버 저장 및 기기에서의 터치/키보드 플레이 검증은 수행하지 않았다.
- 실제 적용 미리보기: `RoleCompositionVoyageLogPreview.png`, `RoleCompositionVoyageLogRandomPreview.png`, `RoleCompositionVoyageLogNeutralPreview.png`.

## 이미지와 폰트

- 룸씬의 CabinRefined / FrameRefined / ParchmentRefined / HelmRefined / Button / CabinFont를 재사용한다.
- 새 자산: `Assets/Resources/RoomLobby/VoyageLogbook.png`.
- 초상화: 기존 `Resources/GameHud/Portraits`.
- 승인 시안: `RoleCompositionVoyageLogConcept.png`.
- 내장 image_gen으로 시안에서 빈 항해일지 표지를 생성했다. 원본은 보존하고 PNG를 프로젝트에 복사했다.

### 최종 자산 프롬프트

Use case: background-extraction. Input is the approved pirate role configuration UI. Create ONLY its blank reusable leather-bound logbook panel sprite for a Unity game. Remove every letter, number, icon, heading, table line, section color, button, counter, helm and all UI content. Keep a single tall rectangular blank ivory parchment page inside its dark walnut-brown leather cover with sewn binding and several thin antique brass binding rings down the LEFT edge, thin elegant brass corner fittings, tiny very restrained worn edge detail. Flat orthographic FRONT view with no perspective and no spine curving into the page. The entire central 88 percent of the page must be clean very pale warm ivory with delicate homogeneous grain, no stains, no watermark, no large compass or motif. Parchment is smooth, not torn or crumpled. Slim cover/binding so UI can occupy almost all of the page. True transparent background outside the book, no cabin or table, no cast shadow beyond the cover. Portrait 2:3 ratio, tightly framed, only small transparent outer margins. High fidelity hand-painted premium pirate game material, matches provided reference.
