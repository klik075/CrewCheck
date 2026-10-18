# CrewCheck Client

> 해적과 선원이 한 배를 타고 채팅으로 속고 속이는 **실시간 웹 마피아 게임 "CrewCheck"** 의 Unity 클라이언트입니다.
> 설치 없이 브라우저에서 링크 하나로 플레이할 수 있도록 **WebGL** 로 빌드해 itch.io에 배포합니다.

- 🎮 플레이: https://youjin-43.itch.io/crewcheck (PC 크롬 권장)
- 🌐 API 서버: https://3-38-222-51.sslip.io
- 📄 개인정보처리방침 · 홈페이지: https://doronyong.github.io/CrewCheck/
- 🖥 서버 저장소: TODO(서버 저장소 링크)

<!-- 사진: 타이틀 화면 → docs/images/title.png -->
<!-- ![타이틀 화면](docs/images/title.png) -->

## 목차

1. [게임 소개](#게임-소개)
2. [주요 기능](#주요-기능)
3. [기술 구성](#기술-구성)
4. [프로젝트 구조](#프로젝트-구조)
5. [시작하기](#시작하기)
6. [서버 주소 설정](#서버-주소-설정)
7. [소셜 로그인 설정](#소셜-로그인-설정)
8. [WebGL 빌드와 itch.io 배포](#webgl-빌드와-itchio-배포)
9. [테스트](#테스트)
10. [알려진 한계](#알려진-한계)
11. [팀](#팀)

## 게임 소개

- 4~12명이 한 배에 탑니다. **3개 진영, 14개 직업** (해적 / 선원 / 중립).
- `밤(직업 능력) → 밤 결과 → 낮(채팅 토론) → 투표(바다에 던질 한 명 선택) → 처형` 을 승리 조건이 충족될 때까지 반복합니다.
- 밤에는 해적끼리만, 죽은 사람은 유령 채팅으로만 대화합니다.
- 직업 대사는 직접 녹음했고, 밤 결과와 처형은 컷씬으로 보여 줍니다.
- 구조의 핵심: **서버가 판정하고, 클라이언트는 보여주기만 합니다.** Unity는 투표나 능력을 서버에 요청하고 서버가 내려주는 상태를 그리는 역할입니다.

<!-- 사진: 로비 화면 → docs/images/lobby.png -->
<!-- 사진: 대기실(방) 화면 → docs/images/room.png -->
<!-- 사진: 게임 진행 화면 (낮 토론) → docs/images/game-day.png -->
<!-- 사진: 밤 능력 사용 화면 → docs/images/game-night.png -->
<!-- 사진: 투표 화면 → docs/images/vote.png -->
<!-- 사진: 결과 화면 → docs/images/result.png -->

## 주요 기능

- 로그인: 게스트, 아이디 가입, 구글·카카오 소셜 로그인(웹), 게스트 → 소셜 계정 연동
- 로비: 방 만들기·검색, 비밀방, 로비 채팅, 친구, 프로필
- 대기실: 준비, 방장 위임·추방, 직업 구성 3종(추천·커스텀·랜덤)
- 인게임: 밤 능력, 밤 결과 컷씬, 낮 토론(넘기기 / +10초 연장), 투표, 처형 컷씬, 결과 화면
- 채팅: 전체 / 해적 / 유령 채팅, 서버 측 욕설 필터·도배 방지 안내
- 사운드: BGM, 효과음, 직업별 대사, 볼륨·해상도 설정
- 실시간: WebSocket "변경 신호" 수신 → REST 재조회, 연결이 끊기면 폴링으로 전환 후 자동 재연결

## 기술 구성

| 항목 | 내용 |
| --- | --- |
| 엔진 | **Unity 6000.3.11f1** (`ProjectSettings/ProjectVersion.txt`) |
| 렌더 파이프라인 | URP 17.3 |
| 입력 | Input System 1.19 |
| UI | uGUI, TextMesh Pro, DOTween(`Assets/Plugins/Demigiant`) |
| 웹 입력 | [WebGLInput](https://github.com/kou-yeung/WebGLInput) (한글 조합 입력용, UPM Git 패키지) + 자체 jslib |
| 통신 | REST(UnityWebRequest) + WebSocket (WebGL은 브라우저 WebSocket을 jslib으로 연결) |
| 배포 | WebGL 빌드 → itch.io |

REST는 조회·행동, WebSocket은 "상태가 바뀌었다"는 신호만 받는 구조입니다. 신호를 받으면 REST로 다시 조회합니다. 브라우저에서는 Unity 기본 WebSocket을 쓸 수 없어서 송수신(transport) 부분만 웹용(`BrowserWebSocketTransport`, `RealtimeWebSocket.jslib`)으로 분리했습니다.

## 프로젝트 구조

```text
Assets/
├── 01_Scenes/        씬
├── 02_Scripts/
│   ├── Auth/         로그인·회원가입·게스트·소셜 로그인(SocialAuthConfig, WebGLSocialAuthProvider)
│   ├── Network/      REST 클라이언트(ApiClient), ApiConfig, 토큰 갱신
│   │   └── Realtime/ WebSocket 신호 처리·재연결·폴링 전환 (Realtime.Core.asmdef)
│   ├── Title/ Lobby/ Room/   타이틀·로비·대기실 화면
│   ├── Game/         게임 진행 컨트롤러, 화면(Screen), Core(규칙·DTO, Game.Core.asmdef)
│   ├── GameUI/ Vote/ Chat/ Friend/ Profile/ Common/(오디오·씬 전환·표시 설정)
├── 03_Prefabs/ 04_UI/ 05_Audio/ 06_Data/ 07_Animations/ 08_Materials/ 09_Image/ 10_Shaders/
├── Plugins/WebGL/    RealtimeWebSocket.jslib, SocialAuthWeb.jslib
├── Editor/           씬·UI 세팅 에디터 도구
└── 99_Test/          테스트용 씬 (GameTest, LobbyTest, RoomTest)
Docs/                 UI·연출·사운드 작업 문서
Tools/                화면 점검 스크립트
```

### 씬 구성

Build Settings에 포함된 씬 (`ProjectSettings/EditorBuildSettings.asset` 기준):

| 순서 | 씬 | 역할 |
| --- | --- | --- |
| 0 | `Title` | 타이틀·로그인 |
| 1 | `Lobby` | 로비 (방 목록, 친구, 프로필) |
| 2 | `GameScene` (비활성) | 이전 게임 씬 |
| 3 | `GameScene3` | 인게임 |
| 4 | `Room` | 대기실 |

`AuthTestScene`, `LobbyTestScene`, `Title_test`, `Vote_test`, `GameScene2/4` 등은 개발·실험용입니다. TODO: 최종 빌드에서 쓰는 씬 구성 확인

## 시작하기

1. **Unity Hub** 에서 **Unity 6000.3.11f1** (WebGL Build Support 모듈 포함)을 설치합니다.
2. 이 저장소를 clone한 뒤 Unity Hub에서 `project_client` 폴더를 프로젝트로 추가해 엽니다. 처음 열 때 UPM 패키지(WebGLInput 등)를 Git에서 내려받습니다. (Git 설치 필요)
3. `Assets/01_Scenes/Title.unity` 를 열고 Play 하면 기본적으로 **배포 서버**에 접속합니다. 로컬 서버를 쓰려면 아래를 참고하세요.

## 서버 주소 설정

서버 주소는 `Assets/02_Scripts/Network/ApiConfig.cs` 의 `ApiConfig.DefaultBaseUrl` 에 모여 있습니다.

```csharp
public const string DefaultBaseUrl = "https://3-38-222-51.sslip.io";
public static string BaseUrl = DefaultBaseUrl;   // 모든 API 호출이 이 값을 사용
```

**로컬 서버로 테스트하기**

- WebGL: 게임 주소 뒤에 `?api=http://localhost:8080` 을 붙여 엽니다. (`ApiBaseUrlOverride`)
  예) `http://localhost:8000/?api=http://localhost:8080`
  보안상 **`localhost` / `127.0.0.1` 주소만** 받아들이고 그 밖의 값은 무시합니다.
- Unity 에디터: `ApiConfig.DefaultBaseUrl` 을 `http://localhost:8080` 으로 바꿔 실행합니다. (커밋하지 마세요) TODO: 에디터용 오버라이드 방법이 따로 있는지 확인
- 로컬 서버 실행: 서버 저장소 README 참고

## 소셜 로그인 설정

웹(WebGL)에서는 구글·카카오 로그인 팝업에서 **인가 코드**를 받아 서버(`/api/members/login/{google,kakao}/code`)에 보내고, 서버가 토큰으로 교환합니다.

설정은 `Assets/02_Scripts/Auth/SocialAuthConfig.cs` 에 있습니다.

| 항목 | 설명 |
| --- | --- |
| `GoogleWebClientId` | 구글 "웹 애플리케이션" OAuth 클라이언트 ID. 서버의 `GOOGLE_CLIENT_ID` 와 같은 값. Google Cloud Console의 "승인된 JavaScript 원본"에 게임 도메인(로컬은 `http://localhost:8000`)을 등록해야 함 |
| `KakaoRestApiKey` | 카카오 REST API 키. 서버의 `KAKAO_REST_API_KEY` 와 같은 앱의 키 |
| `KakaoCallbackPath` | `/kakao-callback.html` — **서버**가 서비스하는 콜백 페이지(itch.io는 게임 주소가 바뀔 수 있어 서버 주소를 사용). `ApiConfig.BaseUrl + 경로` 가 카카오 콘솔 Redirect URI와 서버 `KAKAO_ALLOWED_REDIRECT_URIS` 에 똑같이 등록되어 있어야 함 |

- 위 값은 브라우저 주소창에도 드러나는 **공개 값**이라 클라이언트에 둬도 됩니다. **클라이언트 시크릿은 절대 클라이언트에 넣지 마세요.** (서버 환경변수에만)
- 코드 흐름: `WebGLSocialAuthProvider` ↔ `Plugins/WebGL/SocialAuthWeb.jslib`
- 소셜 가입 닉네임은 구글 `G숫자`, 카카오 `K숫자` 로 서버가 자동 생성합니다.
- 에디터에서는 `MockSocialAuthProvider` 가 쓰입니다. TODO: 에디터에서 소셜 로그인을 시험하는 방법 확인

## WebGL 빌드와 itch.io 배포

1. `File > Build Profiles` 에서 **Web** 플랫폼을 선택하고 Scenes 목록을 확인합니다.
2. 압축 형식은 프로젝트 설정(Gzip)을 따릅니다. (`Player Settings > Publishing Settings`) 빌드 용량은 약 349MB(발표 자료 기준, TODO: 최신 용량 확인)
3. Build 하면 `index.html`, `Build/`, `TemplateData/` 가 만들어집니다.
4. 빌드 폴더 내용을 zip으로 묶고(`index.html` 이 zip 루트에 오도록), itch.io 프로젝트 페이지에 **HTML 프로젝트(Play in browser)** 로 업로드합니다.
5. itch.io는 Gzip 헤더를 자동 처리하지 않을 수 있습니다. TODO: 실제 배포 시 쓰는 압축·Decompression Fallback 설정 확인
6. itch.io는 HTTPS이므로 API/WebSocket도 HTTPS/WSS 여야 합니다. (`ApiConfig.DefaultBaseUrl` 이 `https://`)
7. 배포 전 체크리스트: 서버 주소가 배포 서버인지, 소셜 로그인 승인 도메인에 itch.io 도메인이 등록됐는지 확인

> TODO: 배포 담당자가 실제 절차(빌드 폴더 위치, butler 사용 여부, 업로드 채널 등)를 확인해 보완

<!-- 사진: itch.io 게임 페이지 → docs/images/itch.png -->

## 테스트

- 순수 C# 규칙·네트워크 로직은 asmdef로 분리되어 EditMode 테스트가 있습니다.
  - `Game.Core.Tests` (`Assets/02_Scripts/Game/Core/Tests`), `Realtime.Core.Tests` (`Assets/02_Scripts/Network/Realtime/Tests`)
- 실행: Unity 에디터 `Window > General > Test Runner > EditMode > Run All`
- 서버 없이 게임 흐름을 확인하려면 `FakeGameApi` 를 사용합니다. TODO: 사용 방법 확인
- 클라이언트 UI는 플레이 테스트와 버전별 빌드로 검증했습니다.

## 알려진 한계

(발표 자료 기준)

- 모바일 브라우저는 검증하지 못했습니다. PC 크롬을 권장합니다.
- 브라우저 탭을 오래 숨기면 ping이 끊겨 탈락할 수 있습니다.
- 직업 튜토리얼이 없습니다.
- WebGL 빌드 용량이 큽니다 (이미지·사운드 지연 로딩은 개선 과제).

## 팀

6조 "저진짜 시민이조" (내일배움캠프 단기심화 9기 최종 프로젝트)

5명 전원이 서버와 클라이언트를 함께 맡아, 기능 단위로 서버 API부터 Unity 화면까지 끝까지 책임졌습니다. (튜터: 하승우)

| 이름 | 역할 | 담당 | GitHub |
| --- | --- | --- | --- |
| 박재영 | 팀장 | 채팅: 로비·룸·게임 채팅, 욕설 필터, 도배 방지, 낮 토론 넘기기, 투표 기권·결정 | TODO |
| 정원우 | 팀원 | 실시간: 게임 상태·잠금·타이머·접속 기록 Redis 외부화, Redisson 분산 잠금, WebSocket 신호(`/ws/v1`), WebGL 브라우저 WebSocket 전송층 | TODO |
| 정지용 | 팀원 | 게임 진행: 직업 능력, 신규 직업 5종, 직업 배정(추천·커스텀·랜덤), 게임씬 레이아웃, 투표·처형 컷씬 | TODO |
| 장현교 | 팀원 | 인증·소셜: 로그인, JWT·Refresh Token, 게스트, 구글·카카오 로그인·계정 연동, 프로필, 친구, 타이틀 씬 | TODO |
| 김유진 | 팀원 | 로비·룸·사운드·웹 빌드: 방 관리, 대기실 접속 기록, 사운드 시스템, WebGL 빌드·한글 입력 | TODO |

> 출처: 결과보고서 팀 구성 및 역할 슬라이드. GitHub 계정은 확인되지 않아 TODO로 남겼습니다.

## 라이선스

TODO — 사용한 외부 에셋(Artsystack GUI 등)의 라이선스 확인 필요
