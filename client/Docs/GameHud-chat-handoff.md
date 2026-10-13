# 게임 화면 해적 HUD: 채팅 영역 인계

게임 화면(GameScene)을 해적 HUD 목업에 맞춰 다시 배치했습니다. 씬 파일과 프리팹은 고치지 않았고,
`GameScreen`이 실행할 때 `GameHudLayout`(`Assets/02_Scripts/Game/Screen/GameHudLayout.cs`)과
`GameHudChat`(같은 폴더, 채팅 창)이 위치와 모양을 바꿉니다. 채팅 기능(조회·전송)은 그대로 채팅 담당 코드가 맡습니다.

## 화면 배치 (1080 기준, `GameHudMetrics`)

| 영역 | 위치 |
| --- | --- |
| 상단 HUD (페이즈, 남은 시간, 설정·지도 버튼) | 위에서 0–236 |
| 무대 (낮 원탁, 밤 방) | 위에서 240–974 |
| 공지 두루마리 ("공지" 패 + 두 줄) | 위에서 256–432, 무대 윗부분 위에 잠깐(3–6초) 떴다가 사라짐 |
| 채팅 창 (금테 하나: 탭·필터 / 채팅 기록 / 입력 바) | 위에서 978 ~ 아래에서 250 (화면이 길면 채팅 기록이 늘어남) |
| 입력 바 (하단 시트의 TabBar, 채팅 창 맨 아래 줄) | 아래에서 252–392 |
| 내 역할 패널 | 아래에서 0–252 |

서랍(밤 능력·낮 넘기기·채팅 키보드)은 무대 아래 끝(위에서 978)까지만 올라옵니다. 열리면 시트가 화면 맨 아래까지 내려오고
내 역할 패널은 아래로 빠지며, 서랍이 채팅 창과 내 역할 패널 자리를 덮습니다(채팅 창은 흐려져 사라짐). 무대(캐릭터)는 가리지 않습니다.

## 채팅 창 (`GameHudChat`)

- **탭**: 전체 / 시스템(시스템·안내·알림 줄) / 귓속말(귓속말 줄. 서버 기능이 없어 지금은 늘 비어 있음).
- **채팅 필터 ▼**: 채팅한 적이 있는 플레이어 목록. 고르면 전체 탭에서 그 사람의 채팅만 보이고 버튼에 닉네임이 나옵니다.
  "모든 플레이어"로 되돌립니다.
- **메시지 줄**: 이름패(아바타 + 닉네임) + 내용. 휴대폰에서 읽히게 내용 30, 닉네임 26(긴 이름은 22까지 줄임)이고,
  내용은 폰트 머티리얼을 조금 굵게(Face Dilate 0.12) 그립니다. 캔버스가 가로 1080이라 휴대폰에서 1칸은 약 0.37pt입니다.
  - 아바타: 그 플레이어가 테이블에서 입은 스킨의 흉상(`Resources/GameHud/Portraits`). 스킨을 모르면 닉네임 첫 글자.
  - 내 줄은 금색 이름패, 사망자 채팅은 회색, 밤 해적 채팅은 주황 내용, 시스템 줄은 종 아이콘 + 초록 내용.
  - 시간은 보이지 않습니다(내용 폭을 넓히려고 뺐습니다). `ChatEntry.Time`은 그대로 채워집니다.
- **입력 바**: 양피지 알약 모양 입력칸(왼쪽 말풍선 버튼, 오른쪽 웃는 얼굴) + 보내기 버튼.
  - [+] 버튼은 숨겼습니다. 서랍은 내 역할 패널을 눌러 엽니다(밤·낮 처음에는 저절로 열림).
  - 왼쪽 말풍선 버튼: 서랍이 열려 있으면 아래 화살표로 바뀌고 누르면 서랍을 닫습니다. 닫혀 있으면 입력칸을 누른 것과 같습니다.
  - 웃는 얼굴은 아직 표시만 합니다(이모지 기능 없음).

## 채팅 코드에 바뀐 것

- **`ChatLogView`**: 줄을 `ChatEntry`(종류·보낸 사람·닉네임·내용·시각·리치 텍스트 한 줄)로 받습니다(`AddEntry`).
  - `RowFactory`가 있으면(게임 화면) 그 줄 모양으로, 없으면(대기실 등) 예전처럼 줄 프리팹에 리치 텍스트 한 줄로 그립니다.
  - `SetFilter(탭, 닉네임)`, `Speakers()`, `VisibleCount`, `RowsChanged`를 추가했습니다. `AddLine`·`AddSystemMessage`·
    `AddPlayerMessage`는 그대로 쓸 수 있고, 안내·오류 줄용 `AddNotice(이름, 내용, 색)`를 추가했습니다.
- **`GameChatController.Render`**: 리치 텍스트 대신 `ChatEntry`를 넘깁니다(리치 텍스트 한 줄도 같이 넣어 대기실 화면은 그대로).
  메시지를 처음 받은 시각을 기억합니다.
- **ChatLogPanel**: 매 프레임 `offsetMin = (34, 396)`, `offsetMax = (-34, -1078)`로 고정하고, 배경 Image는 투명,
  Content의 줄 간격·여백은 줄 모양에 맞게 줄입니다.
  - `BottomTabController.pushTargets`가 채팅 아래 끝을 시트 윗변에 맞추지만 위 고정이 덮어씁니다.
- **GameBottomSheet**: `BottomSheetLift`(아래 252 비운 전체 영역) 안으로 옮겨 내 역할 패널 위로 올렸습니다. 슬라이드 동작은 그대로입니다.
  - `BottomTabController.SetDrawerHeight`로 서랍 높이를 화면에 맞춥니다(무대 아래 공간, 최대 800·최소 360. 1080×1920이면 800).
    서랍이 열리는 만큼 `BottomSheetLift`의 아래 여백이 252→0으로 줄고 내 역할 패널이 아래로 빠집니다.

## 아직 남은 것 (서버·채팅 쪽)

1. **귓속말**: 서버 기능이 생기면 `ChatEntryKind.Whisper`로 넘기면 귓속말 탭에 나옵니다.
2. **이모지**: 입력칸의 웃는 얼굴 자리.
3. **보낸 시각**: 메시지 목록 조회 응답에 `createdAt` (줄에 시간을 다시 보이게 하면 필요).

## 다른 배치의 게임 씬 (GameScene2·3·4)

`GameScreen`의 `layout` 값만 다른 GameScene 복사본입니다. 실행할 때 `GameLayoutBody`(`Assets/02_Scripts/Game/Screen/GameLayout*.cs`)가
채팅·능력 서랍·투표·내 역할 자리를 배치에 맞게 다시 놓습니다. 상단 HUD와 공지는 모든 배치가 같이 씁니다.

| 씬 | layout | 배치 |
| --- | --- | --- |
| GameScene | Frame | 지금 배치 (무대 / 금테 채팅 창 / 내 역할 패널) |
| GameScene2 | Stage (A) | 큰 무대 위에 떠 있는 UI: 왼쪽 위 역할 배지, 아래 반투명 채팅(머리 줄을 누르면 무대 아래 끝까지 펼침), 오른쪽 아래 [능력] 버튼 |
| GameScene3 | Tabs (B) | 갑판 위 원탁 무대 / 채팅(탭 없이 늘 보임) / 행동 한 줄 / 맨 아래 플레이어 명단. 밤 능력과 투표 대상은 명단을 눌러 고르고, 행동 줄의 작은 버튼으로 확정 (투표 카드패·하단 서랍은 숨김) |
| GameScene4 | Sheet (C) | 위에 페이즈 단계와 역할 칩, 큰 무대, 아래 끌어 올리는 시트(채팅·능력). 시트는 낮게 / 무대 바로 아래 / 무대 위까지 세 자리에 멈춤 |

- 채팅은 모든 배치에서 같은 `ChatLogView`와 줄 모양(`GameHudChat`)을 쓰고, 위치만 배치가 정합니다(`GameHudChat.BuildBare`).
- GameScene3의 원탁은 `DayRoundTableView.UseSnugLayout()`: 의자 수 = 인원수(게임이 시작되면 다시 만듦), 테이블은 화면 폭의
  약 2/3, 캐릭터는 크게, 의자는 그린 테이블 상판 둘레에 붙여 놓습니다(뒷줄은 상판 뒤, 옆자리는 테두리 위, 앞줄은 등받이가
  테이블 옆면에 닿게). 이름은 어두운 알약 위에 씁니다. 갑판 배경은 위로 올려 바다가 상단 HUD 뒤에만 보이게 합니다.
  다른 씬의 원탁은 그대로입니다.
- GameScene3에서 하단 서랍(Drawer)과 그 안의 패널(밤 능력·낮 넘기기·투표·키보드 자리)은 보이지 않는 곳으로 옮겨 두고,
  행동 줄이 `NightActionPanel.Select/Confirm/Skip`, `DaySkipPanel.Skip`, `VoteCardHandController.Choose/Confirm`으로 같은 일을 합니다.
- 씬 파일은 복사본이라 GameScene을 고쳐도 나머지에 저절로 들어가지 않습니다. 복사할 때의 GameScene 설정(가짜 서버 켜짐 등)을 그대로 갖고 있습니다.
- Build Settings에는 넣지 않았습니다.

### GameScene3: 화면을 에디터에서 고치기 (굽기 + 원탁 미리보기)

- **게임 화면 굽기** (Tools > Game Scene > 게임 화면 굽기 (GameScene3), `Assets/Editor/GameLayoutBakeTool.cs`): Play 때 코드가
  만들던 고정된 틀(상단 HUD, 공지, 내 역할 칩, 무대 아래 바탕·채팅 상자·행동 줄·명단 칸, 채팅 기록 자리, 입력 바)을 씬
  오브젝트로 한 번 만들어 넣습니다. 저장하면 Play 때는 만들지 않고 그 오브젝트에 연결만 하므로(`GameHudLayout`·`GameLayoutTabs`·
  `GameHudChat`이 캔버스에 붙어 참조를 기억합니다) 위치·크기·색을 Scene 뷰에서 고치면 그대로 나옵니다.
  - 코드가 계속 만드는 것: 원탁·캐릭터·밤 방, 명단 안의 사람 칸(명단 칸 폭에 맞춰 나눔), 내 역할 칩의 초상(초상 칸 크기에 맞춤),
    채팅 줄, 투표 카드, 컷신, 팝업. 행동 줄의 글자와 버튼 표시도 Play 때 바뀝니다(굽기 직후의 글자는 배치용 견본).
  - 공지(Notice)는 꺼진 채로 구워집니다. 켜서 자리를 고쳐도 Play 때 다시 끄고, 그 자리에서 떨어지듯 나타납니다.
  - 하단 시트가 채팅 기록을 밀어 올리지 않게 GameBottomSheet의 `pushTargets`를 비웁니다. ChatLogPanel·GameBottomSheet를 고친 것은 그
    프리팹 인스턴스의 오버라이드로 씬에 남습니다.
  - 둥근 사각형·원 그림은 에셋입니다(`Resources/GameHud/HudRounded*.png`, `HudCircle.png`, `Tools/GameHudArt/MakeHudShapes.ps1`).
    실행 중에 만든 그림은 씬에 저장되지 않기 때문입니다.
- **원탁 미리보기** (Tools > Game Scene > 원탁 미리보기 (Edit 모드), `Assets/Editor/RoundTablePreview.cs`): Play 전에도 갑판과
  원탁, 견본 인원을 Play 때 자리에 띄웁니다. 씬에 저장되지 않고, 눌러도 선택되지 않습니다. 무대(갑판·원탁)의 자리와 크기는
  `Resources/DayTable/RoundTableLayout`에서 고치며 바꾸는 즉시 다시 그려집니다(`previewPlayers`: 견본 인원).

## 에셋

`Assets/Resources/GameHud/*.png`는 `Tools/GameHudArt/CutHudSheet.ps1`이 에셋 시트(`Tools/GameHudArt/HudSheet.png`)에서 잘라 만듭니다.
늘려 쓰는 프레임(ChatFrame, InputFrame, NoticeScroll, ParchmentPanel)은 9-slice 테두리가 .meta에 들어 있고,
가운데 장식은 따로 잘라(`*Crest*`) 위에 다시 얹습니다. 시트를 새로 그리면 같은 배치로 그린 뒤 스크립트를 다시 실행하세요.
탭·이름패·입력칸·"공지" 패·시간 막대의 둥근 사각형은 아트 없이 코드로 만듭니다(`HudShapes`).

한글 줄바꿈: `TMP Settings`의 `Use Modern Hangul Line Breaking Rules`를 켜 두어, 한글이 글자 중간이 아니라 띄어쓰기에서
줄을 바꿉니다(프로젝트 전체 TextMeshPro에 적용).
