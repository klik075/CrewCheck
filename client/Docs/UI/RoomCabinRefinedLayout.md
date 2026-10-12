# 다듬은 Room 선실 시안 적용

## 구성

- 참조: `RoomCabinRefinedConcept.png`.
- 통합 원목 프레임, 깨끗한 양피지 카드, 원형 황동 초상화, 방장/본인 배지.
- 준비 상태를 왕관·초록 체크·황색 점과 텍스트로 함께 표시.
- 하단 방 나가기·방 설정·초대하기 및 붉은 게임 시작/준비 버튼.
- 실제 방 인원과 준비 상태, 방장 권한 및 기존 채팅 기능에 연결.
- 레이아웃은 `RoomLobbyLayout`이 씬 진입 시 SafeArea 안에 생성.

## 확인 결과

- 전체 C# 프로젝트 빌드 성공: 오류 0개, 경고 13개.
- 실제 Room 씬·뷰 소스·리소스를 사용한 별도 Unity 렌더링에서 9개 화면 검증 통과.
- 4/6/12명, 인원 제한 변경, 빈자리·스크롤, 방장/참가자 관리 권한, 설정 권한, 채팅 열기/닫기, 긴 본인 닉네임, 원형 마스크 및 상태 아이콘 확인.
- 실제 서버 연결과 여러 클라이언트 간 동기화는 이번 디자인 검증에서 실행하지 않음.
- 실제 렌더링: `RoomCabinRefinedPreview.png`, `RoomCabinRefinedSettingsPreview.png`, `RoomCabinRefinedChatPreview.png`.
- 검증 결과: `Tools/RoomLayoutAudit/RefinedResult.txt`.

## 이미지 자산과 가져오기

`Assets/Resources/RoomLobby`에 `CabinRefined.png`, `ParchmentRefined.png`, `FrameRefined.png`, `HelmRefined.png`를 추가했다. 내장 image_gen으로 승인된 시안을 참조하여 생성했다. 기존 아트와 원본 생성 PNG는 보존했다. 초상화는 기존 게임 카탈로그에서 가져온다.

투명 여백 제거와 9-slice는 Unity Sprite Importer의 rect/border 메타데이터로 적용한다. PNG 픽셀은 변경하지 않는다. `Tools/RoomLayoutAudit/PrepareRoomArt.cs`로 가져오기 설정을 재현할 수 있다.

### 생성 프롬프트

#### CabinRefined

Use case: precise-object-edit. Input is the approved pirate waiting-room mockup. Create the BACKGROUND ONLY for this exact screen. Remove ALL interface elements including upper room title plaque, status strip, giant player list and frame, portraits, every text and button. Reconstruct the ship captain cabin behind them. Preserve the illustrated premium style, dark walnut beams, brass lanterns left and right, skull flag upper right, blue ocean and mountains and sailing ship visible through the wide windows in upper third, right ship wheel, map telescope compass coins at the bottom. Quiet dark wood where the center panel used to be. Preserve camera perspective, warm ambient lighting with cool blue daylight outside and reference composition. Full bleed portrait 9:16, no text, no UI, no characters.

#### ParchmentRefined

Use case: stylized-concept. Input is the approved pirate waiting-room interface. Extract/recreate ONLY one blank participant row plaque in its refined style as a reusable game UI sprite. A single horizontal rectangular smooth ivory parchment card, small clipped/beveled rounded corners, straight clean top and bottom edges, delicate narrow brown line edging with subtle thin antique brass fittings and tiny gold rivets at the four corners. Very subtle warm paper grain and a faint ghosted compass rose near the right third. Large clean center for dynamic text. Even lighting. Flat orthographic front view, no perspective. NO ripped/torn/crumpled scroll edges, no thick curls, no portraits, no icons, no letters or text. Transparent background outside plaque. Almost no outer transparent margins. Wide 6:1 aspect ratio.

#### FrameRefined

Use case: stylized-concept. Reference is an approved premium pirate game UI. Create ONLY a reusable square frame sprite for its wood-and-brass UI panels. Perfectly front-facing orthographic empty frame. A narrow dark walnut wood frame with continuous thin satin antique brass inlay lines and small elegant sculpted brass corner fittings. Reference's roster panel style. CORNERS ONLY have restrained decoration. Completely transparent hollow center, transparent exterior, NO interior content or background. Center hole occupies at least 88 percent of width and height. Straight slim rails with subtle walnut grain, no ripples or repeating ornament along middle of rails; suitable for 9-slice resizing. Elegant and crafted. No text, no icons, no people, no extra objects. Square 1024x1024.

#### HelmRefined

Use case: stylized-concept. Reference is approved pirate cabin game UI. Create ONLY the antique golden brass ship steering wheel emblem from the upper left room title plaque, isolated on a true transparent background. Elegant circular helm, eight wooden/brass spokes and eight small polished handles, sophisticated engraved concentric rings and center hub, subtle highlights warm gold and dark brass shadows. Front-facing symmetrical orthographic view, no perspective. High fidelity painted game UI asset matching the reference. No text, no plaque, no ropes, no extra objects, no outside glow or drop shadow. Square tight framing.
