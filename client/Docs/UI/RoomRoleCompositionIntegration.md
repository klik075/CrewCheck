# 룸씬 직업 구성 연결 수정

## 원인과 적용 경로

로비의 `CreateRoomPopup`에만 항해일지 연결 코드가 있었고, 룸씬은 별도 `RoomSettingsPopup`을 사용하고 있었다. 룸씬의 `GameMode`, `ModeInfoText`, `Button_Custom`도 비활성 상태였다. 이후 로비 경로까지 확인하니 기존 코드가 과거 `Background/TitleImage` 경로를 사용해 현재 `Background` 바로 아래에 있는 버튼을 찾지 못하고 있었다.

룸씬 **방 설정 → 직업 구성 수정하기**에서 공용 `CreateRoomRoleEditor` / `RoleCompositionLayout`을 연다. 기존 모드 탭과 임시 `SetActive` 호출은 해제하고 하나의 진입 버튼을 표시한다. 항해일지의 4–12명 구성, 추천 복원, 랜덤 후보, 직접 수량 편집과 전체 직업 목록을 그대로 공유한다.

로비 **방 만들기 → 직업 구성 수정하기**도 같은 `RoleCompositionLayout.BindEntry`로 연결한다. 로비에서는 구성 적용 시 방 생성용 초안에 반영하고, 실제 방 생성 후 기존 직업 저장 API로 전달한다. 직업 목록 로딩 실패는 버튼에서 재시도할 수 있고, 창을 닫았다 다시 열었을 때 이전 로딩 응답은 무시한다. [로비 적용 화면](LobbyRoleCompositionVoyageLogPreview.png).

## 저장과 닫기

- 방 상세 응답의 `roleSetup`을 읽어 편집 복사본을 만든다. 필드가 없는 구버전 응답은 추천 구성으로 표시한다.
- 룸씬의 구성 적용은 기존 `RoomApi.SetRoleSetup`을 호출하고 성공 응답 후에만 원본을 변경한다.
- 저장 중 중복 조작을 막고, 실패하면 입력과 오류 안내를 남겨 재시도할 수 있다.
- 직업 저장은 방 이름·최대 인원·비밀번호의 미저장 입력을 유지한다.
- ESC는 직업 편집부터 닫는다. 방장 변경·게임 전환·룸 이탈로 설정 창을 닫으면 오버레이도 닫고 늦은 응답을 무시한다.

## 확인 범위와 서버 연동 제한

`CaptureRoomRoles`는 실제 Room 씬의 설정 팝업과 공유 UI를 사용하고 API 응답만 스텁으로 대체한다. 실제 서버 저장 검증은 아니다. 화면: [RoomRoleCompositionVoyageLogPreview.png](RoomRoleCompositionVoyageLogPreview.png).

확인 결과: 클라이언트 빌드 오류 0개(경고 15개), 실제 로비 진입 경로 `CaptureLobbyRoles` PASS, 공통 버튼 적용 후 `CaptureRoomRoles` 회귀 검사 PASS. 공유 편집기 자체의 기존 `CaptureRoles` 검사도 통과한 상태다. 두 씬의 진입 버튼과 항해일지 실제 Unity 렌더링을 확인했다.

2026-10-13 확인한 로컬 `project_server`에는 `GET /api/v1/role-setup/options`가 있지만, RoomController의 `PUT /api/v1/rooms/{roomId}/role-setup`과 RoomDetailResponseDto의 `roleSetup`이 없다. 클라이언트의 기존 직업 구성 계약을 서버에 연결해야 실제 방에서 저장/재조회/게임 배정까지 동작한다. 이 수정에서는 서버 프로젝트를 변경하지 않았다.
