using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Lobby
{
    /// <summary>방 설정 보기/편집. 서버 응답을 받은 뒤에만 저장 완료로 닫는다.</summary>
    public sealed class RoomSettingsPanel : MonoBehaviour
    {
        private GameObject sheet;
        private TMP_InputField titleInput, passwordInput;
        private TMP_Text countLabel, privacyLabel, notice;
        private Button minus, plus, privacyButton, save, close;
        private int capacity, currentPlayers;
        private bool privateRoom, wasPrivate, canEdit, busy;
        private Action<UpdateRoomSettingsRequest> onSave;
        public bool IsOpen => sheet != null && sheet.activeSelf;

        public void Build(RoomLobbyLayout ui)
        {
            var blocker = RoomLobbyLayout.Box("RoomSettingsModal", ui.Root, null, new Color(0, 0, 0, .7f));
            RoomLobbyLayout.Fill(blocker.rectTransform); blocker.raycastTarget = true; sheet = blocker.gameObject;
            var card = RoomLobbyLayout.Panel("SettingsCard", blocker.transform, 100, 440, 880, 1000, null, new Color32(239, 220, 181, 255));
            var frame = RoomLobbyLayout.Box("BrassFrame", card, ui.Frame, Color.white);
            RoomLobbyLayout.Fill(frame.rectTransform);
            RoomLobbyLayout.Panel("TitleRule", card, 76, 180, 728, 1, null, RoomLobbyLayout.Gold);
            ui.Label("Title", card, 76, 100, 650, 70, "방 설정", 44, RoomLobbyLayout.Ink).fontStyle = FontStyles.Bold;
            ui.Label("TitleLabel", card, 76, 200, 710, 42, "방 이름", 28, RoomLobbyLayout.Ink);
            titleInput = Input(ui, card, "RoomTitleInput", 76, 250, 728, 74, "방 이름을 입력하세요");
            titleInput.characterLimit = 60;
            ui.Label("CapacityLabel", card, 76, 362, 480, 48, "최대 참가 인원", 28, RoomLobbyLayout.Ink);
            minus = ui.MakeButton("Minus", card, 478, 350, 78,  70, "−", 32);
            countLabel = ui.Label("Capacity", card, 562, 350, 150, 70, "12 명",  30, RoomLobbyLayout.Ink);
            countLabel.alignment = TextAlignmentOptions.Center;
            plus = ui.MakeButton("Plus", card, 718, 350, 78, 70, "+", 32);
            minus.onClick.AddListener(() => { capacity = Mathf.Max(Mathf.Max(4, currentPlayers), capacity - 1); Refresh(); });
            plus.onClick.AddListener(() => { capacity = Mathf.Min(12, capacity + 1); Refresh(); });
            ui.Label("PrivacyLabel", card, 76, 456, 390,  50, "방 공개 설정", 28, RoomLobbyLayout.Ink);
            privacyButton = ui.MakeButton("Privacy", card, 478, 442, 318, 76, "공개", 28);
            privacyLabel = privacyButton.GetComponentInChildren<TMP_Text>();
            privacyButton.onClick.AddListener(() => { privateRoom = !privateRoom; Refresh(); });
            passwordInput = Input(ui, card, "Password", 76, 550, 728, 74, "비밀번호 · 숫자 4자리 이상");
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.characterValidation = TMP_InputField.CharacterValidation.Digit;
            notice = ui.Label("Notice", card, 76, 656, 728, 126, "", 25, RoomLobbyLayout.Ink);
            notice.textWrappingMode = TextWrappingModes.Normal;
            close = ui.MakeButton("Close", card, 76, 802, 300, 100, "닫기", 30);
            close.onClick.AddListener(Close);
            save = ui.MakeButton("Save", card, 420, 802, 384, 100, "변경 저장", 30, true);
            save.onClick.AddListener(Save);
            sheet.SetActive(false);
        }

        public void Open(RoomDetailResponse room, bool editable, Action<UpdateRoomSettingsRequest> saveAction)
        {
            if (room == null) return;
            capacity = room.maxPlayers; currentPlayers = room.players?.Count ?? 0;
            privateRoom = wasPrivate = room.privateRoom; canEdit = editable; busy = false; onSave = saveAction;
            titleInput.text = room.title; passwordInput.text = "";
            sheet.SetActive(true); sheet.transform.SetAsLastSibling(); Refresh();
        }

        public void RefreshPermissions(bool editable)
        {
            if (!IsOpen || canEdit == editable) return;
            canEdit = editable; Refresh();
        }

        public void SetBusy(bool value) { busy = value; Refresh(); }
        public void ShowError(string message) { busy = false; Refresh(); notice.text = message; notice.color = new Color32(153, 38, 24, 255); }
        public void Close() { if (!busy && sheet != null) sheet.SetActive(false); }

        private void Refresh()
        {
            bool enabled = canEdit && !busy;
            titleInput.interactable = enabled; passwordInput.interactable = enabled;
            passwordInput.gameObject.SetActive(privateRoom);
            minus.interactable = enabled && capacity > Mathf.Max(4, currentPlayers); plus.interactable = enabled && capacity < 12;
            privacyButton.interactable = enabled; save.gameObject.SetActive(canEdit); save.interactable = enabled;
            close.interactable = !busy;
            countLabel.text = $"{capacity} 명"; privacyLabel.text = privateRoom ? "비공개" : "공개";
            notice.color = RoomLobbyLayout.Ink;
            notice.text = busy ? "변경한 설정을 저장하고 있어요..." : !canEdit ? "방장만 설정을 변경할 수 있어요." :
                privateRoom && wasPrivate ? "비밀번호를 비워 두면 기존 비밀번호를 유지합니다." :
                privateRoom ? "비밀번호는 숫자 4자리 이상으로 설정해 주세요." : "공개방은 누구나 입장할 수 있어요.";
        }

        private void Save()
        {
            if (!canEdit || busy) return;
            string title = titleInput.text.Trim(), password = passwordInput.text;
            if (string.IsNullOrEmpty(title)) { ShowError("방 이름을 입력해 주세요."); return; }
            if (privateRoom && (!wasPrivate || !string.IsNullOrEmpty(password)) && !RoomPasswordRule.IsValid(password))
            { ShowError("비밀번호는 숫자 4자리 이상이어야 합니다."); return; }
            onSave?.Invoke(new UpdateRoomSettingsRequest { title = title, maxPlayers = capacity, privateRoom = privateRoom, password = password });
        }

        private static TMP_InputField Input(RoomLobbyLayout ui, Transform parent, string name, float x, float y, float w, float h, string placeholder)
        {
            var background = RoomLobbyLayout.Panel(name, parent, x, y, w, h, null, new Color32(255, 244, 219, 255));
            background.GetComponent<Image>().raycastTarget = true;
            var viewport = RoomLobbyLayout.Rect("TextArea", background); RoomLobbyLayout.Place(viewport, 18, 8, w - 36, h - 16);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = ui.Label("Text", viewport, 0, 0, w - 36, h - 16, "", 30, RoomLobbyLayout.Ink);
            text.richText = false; text.enableAutoSizing = false;
            var hint = ui.Label("Placeholder", viewport, 0, 0, w - 36, h - 16, placeholder, 28, new Color32(128, 100, 74, 255));
            var input = background.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport; input.textComponent = text; input.placeholder = hint; input.targetGraphic = background.GetComponent<Image>();
            input.lineType = TMP_InputField.LineType.SingleLine; return input;
        }
    }
}
