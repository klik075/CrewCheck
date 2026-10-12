# Room 선실 레이아웃

현재는 [다듬은 시안](RoomCabinRefinedLayout.md)의 이미지와 배지를 사용한다. 최신 실제 렌더링은 `RoomCabinRefinedPreview.png`이다.

## 적용

- `Assets/01_Scenes/Room.unity`의 RoomUIController에서 `useCabinLayout`을 사용한다.
- 씬 진입 시 `RoomLobbyLayout`이 SafeArea 안에 1080 × 1920 기준 UI를 만든다. 화면 비율이 달라도 조작 영역은 안전 영역 안에 맞추며, 배경은 화면 전체를 채운다.
- 상단 양피지 방 이름/번호, 인원·모드·공개 여부, 중앙 참가자 명단, 하단 액션 버튼 순서다.
- 최대 인원까지 빈자리를 표시하고 목록을 스크롤한다. 방장 배지, 본인 표시, 준비 상태는 실제 방 응답에 연결된다.
- 초상화는 기존 Portraits 카탈로그를 사용한 대기실 장식이다. 서버가 참가자별 외형을 주지 않으므로 userId 기반으로 고정 선택한다. 게임 직업 배정과는 관계없다.
- 네트워크 지연 데이터가 없으므로 예시 이미지의 신호 막대 위치에는 실제 준비 상태를 표시한다.

## 동작

- 방장은 게임 시작, 참가자는 준비/준비 취소. 기존 최소 인원·준비 완료 조건을 유지한다.
- 다른 참가자의 ··· 버튼은 방장에게 보이고 기존 방장 위임/추방 메뉴와 확인 팝업을 연다.
- 방 설정은 제목·최대 인원·공개 여부·비밀번호를 편집하고 기존 서버의 PUT /api/v1/rooms/{id}/settings를 호출한다. 참가자는 읽기만 가능하다.
- 초대하기는 방 이름과 번호, 입장 안내를 클립보드에 복사한다. 비밀번호는 복사하지 않는다.
- 채팅 버튼은 기존 채팅 로그/입력 기능을 담은 패널을 열고 닫는다.

## 아트

- `Assets/Resources/RoomLobby/Cabin.png`: 제공된 룸 디자인에서 UI를 제거하고 선실을 복원한 배경.
- `Assets/Resources/RoomLobby/Parchment.png`: 투명 배경의 빈 양피지 패널.
- `Frame.png`, `Button.png`: 프로젝트에 있는 Classic RPG GUI 자산을 복사하여 룸 전용 9-slice 설정으로 사용.
- `CabinFont.asset`: 기존 NanumGothic 폰트의 룸 전용 동적 SDF. 여러 아틀라스를 허용해 한글 닉네임/문구가 누락되지 않도록 한다. 원본 폰트와 OFL은 Assets/Fonts에 있다.
- 생성 도구: 내장 image_gen. 기존 원본 파일은 보존.

### 배경 프롬프트

Use case: precise-object-edit. Asset type: portrait 9:16 Unity game background. Input image is the edit target: pirate cabin lobby UI. Remove ALL UI from the image: room title plaque and its chains at upper left, information badges, the entire participant panel/frame/rows/portraits/text, bottom action buttons and icons. Reconstruct the continuous pirate ship captain cabin behind those UI areas. Preserve the reference illustration style, warm lanterns, wooden beams and planks, skull flag upper right, vivid turquoise ocean and islands through windows across the upper middle, ship wheel at right, map coins compass and telescope along the bottom. The central lower area should be quiet dark wooden cabin surfaces suitable behind a UI overlay. Keep ornate atmosphere at outer edges. No people, no text, no panels, no UI, no watermark. Full bleed highly detailed painted fantasy game background, portrait 9:16.

### 양피지 프롬프트

Use case: stylized-concept. Asset type: transparent UI sprite for a pirate game lobby. A single empty horizontal parchment plaque, aspect ratio 5:1, front facing flat orthographic. Honey golden cream aged paper with subtle grain, worn irregular curled corners, thin dark brown hand painted edge, small aged brass tacks at four corners. Center should be very light clean pale parchment, evenly lit, almost blank, excellent for dark readable text overlay. Style: beautifully detailed painted fantasy pirate mobile game, warm golden lantern light. No lettering, no symbols, no icons, no background shadow outside panel, no other objects. True transparent background around the parchment. Panel fills canvas with only small transparent margins.

## 확인

- C# 프로젝트 빌드.
- 별도 임시 Unity 프로젝트에서 실제 뷰 소스/리소스로 4·6·12명, 스크롤, 방장/참가자 권한, 설정 창, 화면 비율을 검증한다. 재현 코드: `Tools/RoomLayoutAudit/CaptureRoom.cs`.
- 실제 서버에 여러 클라이언트를 연결하는 종단 간 테스트는 이 검증에 포함되지 않는다.

적용 미리보기(샘플 참가자): `RoomCabinPreview.png`, `RoomCabinSettingsPreview.png`, `RoomCabinChatPreview.png`.
