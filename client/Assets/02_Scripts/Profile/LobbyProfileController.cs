using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Common;

namespace WhoisntCitizen.Profile
{
    /// <summary>
    /// 로비 프로필 표시 + 내 프로필 팝업 담당 (Manager 오브젝트 등 아무 곳에나 부착).
    ///
    /// 동작
    ///   1) 로비 씬 시작 시 GET /api/users/me 로 내 프로필을 불러와 캐싱(Current)한다.
    ///   2) Canvas/SafeArea/TopBar/Profile_Button 에 닉네임/레벨을 표시한다.
    ///      (불러오기 전·실패 시에는 로그인 때 받은 닉네임만 표시)
    ///   3) Profile_Button 클릭 → 내 프로필 팝업 열기 (닉네임, 레벨, 골드, 전적, 승률, 닉네임 변경)
    ///   4) 닉네임 변경 성공 → 프로필 재조회 → 버튼/팝업/AuthSession 닉네임 갱신
    ///
    /// 인스펙터 연결
    ///   Profile Button      ← 로비 상단 정보 패널(UserInfoArea)에 붙인 Button
    ///   Profile Button Text ← (선택) 닉네임/레벨을 표시할 TMP 텍스트
    /// </summary>
    public class LobbyProfileController : MonoBehaviour
    {
        /// <summary>내 프로필 캐시. 로비에서 한 번 불러오면 다른 UI(친구 팝업 등)도 같이 쓴다. 불러오기 전에는 null.</summary>
        public static UserProfileResponse Current { get; private set; }

        /// <summary>Current가 갱신될 때마다 호출된다.</summary>
        public static event Action<UserProfileResponse> ProfileChanged;

        [Header("Profile Button (상단 정보 패널 UserInfoArea)")]
        [Tooltip("누르면 프로필 팝업을 여는 버튼. 로비 상단 정보 패널(UserInfoArea)에 붙인 Button 을 연결한다.")]
        [SerializeField] private Button profileButton;
        [Tooltip("(선택) 버튼에 닉네임/레벨을 표시할 TMP 텍스트. 비워 두면 표시하지 않는다.")]
        [SerializeField] private TMP_Text profileButtonText;
        [Tooltip("버튼 표시 형식. {nickname} {level} 을 쓸 수 있다.")]
        [SerializeField] private string buttonFormat = "{nickname}\nLv.{level}";

        [Header("Profile Popup")]
        [Tooltip("내 프로필 팝업 루트 오브젝트. 시작할 때 닫힌다.")]
        [SerializeField] private GameObject profilePopup;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text nicknameText;

        [Header("Avatar (프로필 이미지)")]
        [Tooltip("(선택) 로비 상단 정보 패널(UserInfoArea)의 아바타 이미지")]
        [SerializeField] private Image profileButtonAvatar;
        [Tooltip("(선택) 프로필 팝업 안의 내 아바타 이미지")]
        [SerializeField] private Image avatarImage;
        [Tooltip("(선택) 내 아바타를 누르면 아바타 선택 창을 여는 버튼")]
        [SerializeField] private Button avatarEditButton;
        [Tooltip("(선택) 아바타 선택 창 루트. 시작할 때 닫힌다.")]
        [SerializeField] private GameObject avatarPopup;
        [Tooltip("(선택) 아바타 선택 창의 확인 버튼. 고른 아바타를 서버에 저장하고 창을 닫는다.")]
        [SerializeField] private Button avatarConfirmButton;
        [Tooltip("(선택) 아바타 선택 창의 취소 버튼. 저장하지 않고 창을 닫는다.")]
        [SerializeField] private Button avatarCancelButton;
        [Tooltip("(선택) 아바타 선택 창 안의 메시지 텍스트. 비우면 프로필 팝업의 메시지를 쓴다.")]
        [SerializeField] private StatusMessageView avatarStatusMessage;
        [Tooltip("(선택) 아바타 선택 버튼들. 순서가 곧 아바타 번호(0부터). AvatarCatalog.Count개까지 사용")]
        [SerializeField] private Button[] avatarButtons;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text goldText;
        [Tooltip("전적. 예) 12전 7승 5패")]
        [SerializeField] private TMP_Text recordText;
        [Tooltip("승률. 예) 58.3%")]
        [SerializeField] private TMP_Text winRateText;

        [Header("Nickname Change")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button changeNicknameButton;

        [Header("Status")]
        [Tooltip("팝업 안의 메시지 텍스트")]
        [SerializeField] private StatusMessageView statusMessage;

        [Header("Rules")]
        [Tooltip("닉네임 최소/최대 글자 수 (클라이언트 1차 검증. 최종 검증은 서버)")]
        [SerializeField] private int nicknameMinLength = 2;
        [SerializeField] private int nicknameMaxLength = 10;

        // 상단 패널(UserInfoArea/Info/MatchHistory)의 "전체/선원/해적 전적" 값 텍스트. 제목 글자로 찾는다.
        private TMP_Text topTotalRecord, topCrewRecord, topPirateRecord;
        private bool topRecordsBound;
        // 프로필 팝업의 진영별 전적 줄 (전적 줄을 복제해서 만든다)
        private TMP_Text factionValueText;

        private bool isLoading;     // 프로필 조회 중
        private Action<bool> pendingDone; // 조회 중에 들어온 LoadProfile 호출의 콜백 (같은 응답으로 함께 완료한다)
        private bool isChanging;    // 닉네임/아바타 변경 중
        private int pendingAvatarId = -1; // 선택 창에서 고른(아직 저장 전인) 아바타 번호

        private void Awake()
        {
            // 인스펙터 OnClick에 직접 연결한 게 없을 때만 코드로 연결 (두 번 호출 방지)
            BindIfEmpty(profileButton, OnProfileButtonClicked);
            BindIfEmpty(closeButton, ClosePopup);
            BindIfEmpty(changeNicknameButton, OnChangeNicknameClicked);
            BindIfEmpty(avatarEditButton, OpenAvatarPopup);
            BindIfEmpty(avatarConfirmButton, OnAvatarConfirmClicked);
            BindIfEmpty(avatarCancelButton, CloseAvatarPopup);
            if (avatarButtons != null)
            {
                for (int i = 0; i < avatarButtons.Length; i++)
                {
                    int id = i; // 클로저용 복사
                    if (avatarButtons[i] == null) continue;
                    avatarButtons[i].gameObject.SetActive(id < AvatarCatalog.Count);
                    if (id >= AvatarCatalog.Count) continue;
                    AvatarCatalog.Apply(avatarButtons[i].targetGraphic as Image, id);
                    avatarButtons[i].onClick.AddListener(() => OnAvatarSelected(id));
                }
            }

            if (nicknameInput != null)
            {
                nicknameInput.lineType = TMP_InputField.LineType.SingleLine;
                nicknameInput.characterLimit = nicknameMaxLength;
                nicknameInput.onSubmit.AddListener(_ => OnChangeNicknameClicked());
            }
        }

        private void Start()
        {
            if (profilePopup != null) profilePopup.SetActive(false); // 씬에 켜 둔 채 저장했어도 닫고 시작
            if (avatarPopup != null) avatarPopup.SetActive(false);
            EnsureFactionRow();

            // 로그인하지 않았으면 LobbyUIController가 타이틀로 보내므로 요청하지 않는다.
            if (!AuthSession.IsAuthenticated) return;

            // 다른 계정으로 다시 로그인했으면 이전 캐시는 버린다.
            if (Current != null && Current.userId != AuthSession.UserId) Current = null;

            RenderButton(Current); // 캐시가 있으면 바로, 없으면 로그인 닉네임으로 표시
            LoadProfile(null);
        }

        private void OnDestroy()
        {
            if (profileButton != null) profileButton.onClick.RemoveListener(OnProfileButtonClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(ClosePopup);
            if (changeNicknameButton != null) changeNicknameButton.onClick.RemoveListener(OnChangeNicknameClicked);
            if (avatarEditButton != null) avatarEditButton.onClick.RemoveListener(OpenAvatarPopup);
            if (avatarConfirmButton != null) avatarConfirmButton.onClick.RemoveListener(OnAvatarConfirmClicked);
            if (avatarCancelButton != null) avatarCancelButton.onClick.RemoveListener(CloseAvatarPopup);
        }

        // ------------------------------------------------------------------
        // 프로필 불러오기
        // ------------------------------------------------------------------

        /// <summary>내 프로필을 서버에서 다시 불러와 버튼/팝업을 갱신한다.</summary>
        /// <param name="onDone">완료 콜백 (성공 여부). 필요 없으면 null</param>
        public void LoadProfile(Action<bool> onDone)
        {
            if (isLoading)
            {
                pendingDone += onDone;
                return;
            }

            isLoading = true;
            pendingDone += onDone;
            UserApi.GetMe(result =>
            {
                if (this == null) return; // 응답 전에 씬이 바뀐 경우

                isLoading = false;
                Action<bool> done = pendingDone;
                pendingDone = null;

                if (!result.success)
                {
                    Debug.LogWarning($"[Profile] 내 프로필 조회 실패: {result.message}");
                    if (IsPopupOpen) statusMessage?.ShowError(result.message);
                    done?.Invoke(false);
                    return;
                }

                SetCurrent(result.data);
                done?.Invoke(true);
            });
        }

        private void SetCurrent(UserProfileResponse profile)
        {
            Current = profile;
            if (!string.IsNullOrEmpty(profile.nickname)) AuthSession.SetNickname(profile.nickname);

            RenderButton(profile);
            if (IsPopupOpen) RenderPopup(profile);
            ProfileChanged?.Invoke(profile);
        }

        // ------------------------------------------------------------------
        // 화면 표시
        // ------------------------------------------------------------------

        private void RenderButton(UserProfileResponse profile)
        {
            if (profile != null) AvatarCatalog.Apply(profileButtonAvatar, profile.profileImageId);
            if (profile != null) RenderTopRecords(profile);
            if (profileButtonText == null) return;

            profileButtonText.text = profile == null
                ? AuthSession.Nickname
                : buttonFormat.Replace("{nickname}", profile.nickname).Replace("{level}", profile.level.ToString());
        }

        private void RenderPopup(UserProfileResponse p)
        {
            if (nicknameText != null) nicknameText.text = p.nickname;
            AvatarCatalog.Apply(avatarImage, p.profileImageId);
            HighlightAvatar(p.profileImageId);
            if (levelText != null) levelText.text = $"Lv.{p.level}";
            if (goldText != null) goldText.text = p.gold.ToString("N0");
            if (recordText != null) recordText.text = $"{p.playCount}전 {p.winCount}승 {p.lossCount}패";
            if (winRateText != null) winRateText.text = $"{p.WinRatePercent:0.#}%";
            if (factionValueText != null) factionValueText.text = FactionRecord.Lines(p);
        }

        // ------------------------------------------------------------------
        // 진영별 전적 (상단 패널 / 프로필 팝업)
        // ------------------------------------------------------------------

        // 상단 패널의 전체/선원/해적 전적 칸을 찾아 "00승 00패" 자리에 값을 넣는다.
        // 각 칸은 Item(Title/Text + Text (TMP) (1)) 구조이고, 제목 글자("전체 전적" 등)로 어느 칸인지 구분한다.
        private void RenderTopRecords(UserProfileResponse p)
        {
            if (!topRecordsBound)
            {
                topRecordsBound = true;
                Transform area = profileButton != null ? profileButton.transform.parent : null;
                if (area != null)
                {
                    foreach (TMP_Text title in area.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (title.transform.parent == null || title.transform.parent.name != "Title") continue;
                        Transform item = title.transform.parent.parent;
                        TMP_Text value = null;
                        foreach (TMP_Text t in item.GetComponentsInChildren<TMP_Text>(true))
                            if (t != title) { value = t; break; }

                        string label = title.text.Trim();
                        if (label == "전체 전적") topTotalRecord = value;
                        else if (label == "선원 전적") topCrewRecord = value;
                        else if (label == "해적 전적") topPirateRecord = value;
                    }
                }
            }

            if (topTotalRecord != null) topTotalRecord.text = FactionRecord.WinLoss(p.playCount, p.winCount);
            if (topCrewRecord != null) topCrewRecord.text = FactionRecord.WinLoss(p.crewPlayCount, p.crewWinCount);
            if (topPirateRecord != null) topPirateRecord.text = FactionRecord.WinLoss(p.piratePlayCount, p.pirateWinCount);
        }

        // 프로필 팝업의 승률 줄 아래에 "선원/해적/중립" 세 줄을 복제해서 만든다. (씬을 고치지 않아도 되게)
        private void EnsureFactionRow()
        {
            if (factionValueText != null || winRateText == null) return;
            Transform winRow = winRateText.transform.parent;
            if (winRow == null || winRow.parent == null) return;

            GameObject row = Instantiate(winRow.gameObject, winRow.parent, false);
            row.name = "Row_진영별";
            row.transform.SetSiblingIndex(winRow.GetSiblingIndex() + 1);

            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length < 2) { Destroy(row); return; }
            TMP_Text title = texts[0], value = texts[1];

            title.text = FactionRecord.Titles();
            title.fontSize = 30f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            value.fontSize = 30f;
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.text = string.Empty;

            LayoutElement le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 140f;
            LayoutElement titleLe = title.GetComponent<LayoutElement>();
            if (titleLe != null) titleLe.preferredHeight = -1f;

            factionValueText = value;
        }

        // ------------------------------------------------------------------
        // 팝업 열기 / 닫기
        // ------------------------------------------------------------------

        public bool IsPopupOpen => profilePopup != null && profilePopup.activeSelf;

        // Profile_Button 클릭 (인스펙터 OnClick에 직접 연결해도 된다)
        public void OnProfileButtonClicked()
        {
            if (SceneLoader.IsLoading || profilePopup == null) return;

            profilePopup.SetActive(true);
            statusMessage?.Clear();
            SetChanging(false);
            if (nicknameInput != null) nicknameInput.text = string.Empty;

            if (Current != null) RenderPopup(Current); // 캐시를 먼저 보여 주고 최신 값으로 갱신한다.
            else statusMessage?.ShowInfo("프로필을 불러오는 중...", keep: true);

            LoadProfile(ok =>
            {
                if (ok && statusMessage != null && statusMessage.CurrentMessage == "프로필을 불러오는 중...")
                    statusMessage.Clear();
            });
        }

        public void ClosePopup()
        {
            if (isChanging) return;
            statusMessage?.Clear();
            if (avatarPopup != null) avatarPopup.SetActive(false);
            if (profilePopup != null) profilePopup.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 닉네임 변경
        // ------------------------------------------------------------------

        // 닉네임 변경 버튼 (또는 입력칸에서 Enter)
        public void OnChangeNicknameClicked()
        {
            if (isChanging || SceneLoader.IsLoading) return;

            string nickname = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;

            // 클라이언트 1차 검증. 실패하면 서버에 보내지 않는다.
            if (nickname.Length < nicknameMinLength || nickname.Length > nicknameMaxLength)
            {
                statusMessage?.ShowError($"닉네임은 {nicknameMinLength}~{nicknameMaxLength}자로 입력하세요.");
                return;
            }
            if (Current != null && Current.nickname == nickname)
            {
                statusMessage?.ShowError("현재 닉네임과 같습니다.");
                return;
            }

            SetChanging(true);
            statusMessage?.ShowInfo("닉네임을 변경하는 중...", keep: true);

            UserApi.ChangeNickname(nickname, result =>
            {
                if (this == null) return;

                if (!result.success)
                {
                    SetChanging(false);
                    // 욕설·금칙어(400 NICKNAME_NOT_ALLOWED)는 경고로 알리고 닉네임은 바뀌지 않는다. 입력칸은 그대로 둬 고쳐 쓸 수 있다.
                    string warning = result.IsNicknameNotAllowed
                        ? "사용할 수 없는 닉네임입니다. 욕설이나 부적절한 단어가 포함되어 있습니다."
                        : result.message; // 중복 닉네임(409) 등 서버 메시지
                    statusMessage?.ShowError(warning);
                    return;
                }

                // 변경됐으니 최신 프로필을 다시 받아 버튼/팝업을 갱신한다.
                if (nicknameInput != null) nicknameInput.text = string.Empty;
                LoadProfile(ok =>
                {
                    SetChanging(false);
                    if (ok) statusMessage?.ShowSuccess("닉네임이 변경되었습니다.");
                    else statusMessage?.ShowError("변경은 되었지만 프로필을 다시 불러오지 못했습니다.");
                });
            });
        }

        // ------------------------------------------------------------------
        // 프로필 이미지(아바타) 선택
        // ------------------------------------------------------------------

        // 선택 창에서 아바타를 고른다. 저장은 확인 버튼을 눌렀을 때 한다.
        private void OnAvatarSelected(int id)
        {
            if (isChanging || SceneLoader.IsLoading || id < 0 || id >= AvatarCatalog.Count) return;
            pendingAvatarId = id;
            AvatarStatus?.Clear();
            HighlightAvatar(id);
        }

        // 확인: 고른 아바타를 서버에 저장하고 선택 창을 닫는다. 바뀐 게 없으면 그냥 닫는다.
        public void OnAvatarConfirmClicked()
        {
            if (isChanging || SceneLoader.IsLoading) return;

            int id = pendingAvatarId;
            if (id < 0 || id >= AvatarCatalog.Count || (Current != null && Current.profileImageId == id))
            {
                CloseAvatarPopup();
                return;
            }

            SetChanging(true);
            AvatarStatus?.ShowInfo("프로필 이미지를 변경하는 중...", keep: true);

            UserApi.ChangeProfileImage(id, result =>
            {
                if (this == null) return;

                if (!result.success)
                {
                    SetChanging(false);
                    AvatarStatus?.ShowError(result.message); // 선택 창은 열어 둔다. 다시 확인하거나 취소할 수 있다.
                    return;
                }

                // 서버에 저장됐으니 바로 화면에 반영하고, 최신 프로필도 다시 받아 둔다.
                if (Current != null) Current.profileImageId = id;
                RenderButton(Current);
                if (Current != null) RenderPopup(Current);
                LoadProfile(ok =>
                {
                    SetChanging(false);
                    if (avatarPopup != null) avatarPopup.SetActive(false);
                    statusMessage?.ShowSuccess("프로필 이미지가 변경되었습니다.");
                });
            });
        }

        // 선택 창 안의 메시지 영역. 없으면 프로필 팝업의 메시지를 쓴다.
        private StatusMessageView AvatarStatus => avatarStatusMessage != null ? avatarStatusMessage : statusMessage;

        // 선택된 아바타는 밝게, 나머지는 어둡게 표시한다.
        private void HighlightAvatar(int selectedId)
        {
            if (avatarButtons == null) return;
            if (selectedId >= 0) selectedId = AvatarCatalog.NormalizeId(selectedId);
            for (int i = 0; i < avatarButtons.Length; i++)
            {
                if (avatarButtons[i] == null || !(avatarButtons[i].targetGraphic is Image img)) continue;
                Color c = img.color;
                c.a = i == selectedId ? 1f : 0.45f;
                img.color = c;
            }
        }

        // 현재 아바타를 누르면 선택 창을 연다.
        public void OpenAvatarPopup()
        {
            if (isChanging || SceneLoader.IsLoading || avatarPopup == null) return;
            avatarPopup.SetActive(true);
            AvatarStatus?.Clear();
            pendingAvatarId = Current != null ? AvatarCatalog.NormalizeId(Current.profileImageId) : -1;
            HighlightAvatar(pendingAvatarId);
        }

        // 취소(또는 바뀐 게 없는 확인): 저장하지 않고 닫는다.
        public void CloseAvatarPopup()
        {
            if (isChanging) return;
            pendingAvatarId = -1;
            if (avatarPopup != null) avatarPopup.SetActive(false);
        }

        // 변경 요청 중에는 버튼/입력칸을 잠가 중복 요청을 막는다.
        private void SetChanging(bool value)
        {
            isChanging = value;
            if (changeNicknameButton != null) changeNicknameButton.interactable = !value;
            if (nicknameInput != null) nicknameInput.interactable = !value;
            if (avatarButtons != null)
                foreach (Button b in avatarButtons) if (b != null) b.interactable = !value;
            if (closeButton != null) closeButton.interactable = !value;
            if (avatarConfirmButton != null) avatarConfirmButton.interactable = !value;
            if (avatarCancelButton != null) avatarCancelButton.interactable = !value;
        }

        // ------------------------------------------------------------------
        // 유틸
        // ------------------------------------------------------------------

        private static void BindIfEmpty(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && button.onClick.GetPersistentEventCount() == 0) button.onClick.AddListener(action);
        }
    }
}
