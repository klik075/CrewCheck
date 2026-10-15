using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.Game;
using WhoisntCitizen.GameUI;
using static WhoisntCitizen.Lobby.RoomLobbyLayout;

namespace WhoisntCitizen.Lobby
{
    /// <summary>룸씬과 같은 자산을 사용하는 한 화면 항해일지 편집 뷰.</summary>
    public sealed class RoleCompositionLayout : MonoBehaviour
    {
        private sealed class RoleRow
        {
            public RoleDto Role;
            public RectTransform Rect;
            public TMP_Text Name, Count;
            public Button Minus, Plus;
            public Toggle Candidate;
            public CanvasGroup Amount, CandidateArea, Whole;
        }

        private readonly List<RoleRow> rows = new List<RoleRow>();
        private readonly Dictionary<string, TMP_Text> factionCounts = new Dictionary<string, TMP_Text>();
        private bool neutralRandom; // 서버가 중립 직업도 랜덤 후보 선택지로 주는지 (주지 않으면 중립은 직접 설정 전용)
        private TMP_FontAsset font;
        private Sprite paper, frame, buttonArt, cabin;
        private DesignSpriteCatalog portraits;
        private RectTransform safeArea, root, content;
        private ScrollRect scroll;
        private TMP_Text countLabel, modeLabel, rangeLabel, helper, summary, total, notice, catalogLabel, switchLabel;
        private Button previous, next, apply;
        private Toggle randomToggle;
        private RoomHelmGraphic switchBack, switchKnob, validity;
        private CanvasGroup applyGroup;
        private CanvasGroup controls;
        // 직업 줄의 초상화·이름 쪽을 누르면 그 줄 아래(자리가 없으면 위)에 뜨는 직업 설명
        private RectTransform viewport, tooltip;
        private TMP_Text tooltipTitle, tooltipBody, tooltipGoal;
        private RoleRow tipRow;
        private readonly Vector3[] corners = new Vector3[4];
        private const float TooltipWidth = 620, TooltipPad = 24;
        private Color32 muted = new Color32(119, 93, 63, 255);
        private static readonly Color Good = new Color32(41, 100, 70, 255);
        private static readonly Color Bad = new Color32(147, 45, 32, 255);

        /// <summary>로비/룸 설정의 기존 모드 탭을 항해일지 진입 버튼 하나로 교체한다.</summary>
        internal static Button BindEntry(Transform background, Action open, out TMP_Text modeInfo)
        {
            modeInfo = background?.Find("ModeInfoText")?.GetComponent<TMP_Text>();
            Transform modeBar = background?.Find("GameMode");
            Button entry = background?.Find("Items/Button_Custom")?.GetComponent<Button>();
            if (entry == null) return null;
            for (int i = 0; i < entry.onClick.GetPersistentEventCount(); i++)
                entry.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            entry.onClick.AddListener(() => open());
            if (modeBar != null)
            {
                foreach (Transform child in modeBar) child.gameObject.SetActive(false);
                modeBar.gameObject.SetActive(true);
                var group = modeBar.GetComponent<LayoutGroup>();
                if (group != null) group.enabled = false;
                var size = modeBar.GetComponent<LayoutElement>();
                if (size == null) size = modeBar.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = 520;
                size.minHeight = size.preferredHeight = 100;
                entry.transform.SetParent(modeBar, false);
                var rect = (RectTransform)entry.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(520, 90);
                rect.localScale = Vector3.one;
            }
            entry.gameObject.SetActive(true);
            Sprite art = Art("Button");
            var entryImage = entry.GetComponent<Image>();
            if (entryImage != null && art != null)
            {
                entryImage.sprite = art;
                entryImage.type = Image.Type.Sliced;
                entryImage.color = Color.white;
            }
            var label = entry.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "직업 구성 수정하기";
                label.font = Resources.Load<TMP_FontAsset>("RoomLobby/CabinFont");
                label.color = Cream;
                label.fontSize = 32;
                label.alignment = TextAlignmentOptions.Center;
                Fill(label.rectTransform);
            }
            if (modeInfo != null)
            {
                modeInfo.gameObject.SetActive(true);
                modeInfo.rectTransform.sizeDelta = new Vector2(560, 60);
                modeInfo.fontSize = 24;
                modeInfo.alignment = TextAlignmentOptions.Center;
                modeInfo.textWrappingMode = TextWrappingModes.Normal;
            }
            return entry;
        }

        public void Build(Canvas parent, Action<int> changeCount, Action recommend,
            Action<bool> randomChanged, Action cancel, Action confirm)
        {
            Fill((RectTransform)transform);
            var overlayCanvas = gameObject.AddComponent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingLayerID = parent.sortingLayerID;
            overlayCanvas.sortingOrder = parent.sortingOrder + 100;
            overlayCanvas.worldCamera = parent.worldCamera;
            gameObject.AddComponent<GraphicRaycaster>();
            font = Resources.Load<TMP_FontAsset>("RoomLobby/CabinFont");
            paper = Art("ParchmentRefined"); frame = Art("FrameRefined");
            cabin = Art("CabinRefined"); buttonArt = Art("Button");
            portraits = Resources.Load<DesignSpriteCatalog>("GameHud/Portraits");

            var background = Box("CabinBackground", transform, cabin, Color.white);
            Fill(background.rectTransform); background.raycastTarget = true;
            var aspect = background.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = cabin != null ? cabin.rect.width / cabin.rect.height : 9f / 16f;
            safeArea = Rect("SafeArea", transform); Fill(safeArea);
            root = Rect("VoyageLog", safeArea);
            controls = root.gameObject.AddComponent<CanvasGroup>();
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(1080, 1920);
            Panel("Logbook", root, 28, 142, 1024, 1560, Art("VoyageLogbook"), Color.white);
            var helm = Box("TitleHelm", root, Art("HelmRefined"), Color.white);
            Place(helm.rectTransform, 172, 185, 142, 142); helm.preserveAspect = true;
            Text("Title", root, 352, 199, 476, 78, "직업 구성", 54, Ink, true);
            Text("Subtitle", root, 380, 274, 450, 42, "해적 항해일지", 28, muted);
            Button("Close", root, 938, 184, 66, 66, "×", 40).onClick.AddListener(() => cancel());

            Panel("CountPlate", root, 116, 342, 884, 84, paper, Color.white);
            Text("CountCaption", root, 144, 360, 188, 48, "구성 인원", 30, Ink, true);
            previous = Button("PreviousCount", root, 342, 354, 62, 62, "−", 34);
            previous.onClick.AddListener(() => changeCount(-1));
            countLabel = Text("PlayerCount", root, 414, 356, 126, 58, "", 40, Ink, true);
            countLabel.alignment = TextAlignmentOptions.Center;
            next = Button("NextCount", root, 554, 354, 62, 62, "+", 34);
            next.onClick.AddListener(() => changeCount(1));
            rangeLabel = Text("CountRange", root, 642, 365, 118, 40, "", 24, muted);
            modeLabel = Text("CurrentMode", root, 782, 365, 196, 40, "", 25, Ink);

            Wood("Actions", root, 116, 444, 884, 108);
            var recommendButton = Button("Recommend", root, 130, 458, 430, 76, "추천 구성 불러오기", 27);
            recommendButton.onClick.AddListener(() => recommend());
            Text("RandomCaption", root, 590, 472, 220, 48, "시작 시 랜덤", 26, Cream, true);
            var randomHit = Panel("Random", root, 824, 466, 152, 64, null, Color.clear);
            randomHit.GetComponent<Image>().raycastTarget = true;
            randomToggle = randomHit.gameObject.AddComponent<Toggle>();
            randomToggle.transition = Selectable.Transition.None;
            randomToggle.targetGraphic = randomHit.GetComponent<Image>();
            switchBack = Icon("Track", randomHit, 0, 0, 152, RoomHelmGraphic.Symbol.Pill, muted);
            Place(switchBack.rectTransform, 0, 0, 152, 64);
            switchKnob = Icon("Knob", randomHit, 7, 5, 54, RoomHelmGraphic.Symbol.Disc, Cream);
            switchLabel = Text("SwitchLabel", randomHit, 60, 8, 84, 48, "", 23, Cream, true);
            switchLabel.alignment = TextAlignmentOptions.Center;
            randomToggle.onValueChanged.AddListener(enabled => randomChanged(enabled));
            helper = Text("Helper", root, 128, 562, 860, 36, "", 23, muted);
            Text("RoleHeader", root, 144, 602, 360, 40, "직업", 25, Ink, true);
            Text("AmountHeader", root, 682, 602, 132, 40, "인원", 25, Ink, true);
            Text("CandidateHeader", root, 864, 602, 126, 40, "랜덤 후보", 24, Ink, true);
            Rule(root, 126, 644, 856);

            viewport = Panel("Viewport", root, 124, 650, 854, 830, null, Color.clear);
            viewport.GetComponent<Image>().raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Roles", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true; list.childForceExpandHeight = false;
            var size = content.gameObject.AddComponent<ContentSizeFitter>();
            size.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 48;
            var track = Panel("ScrollTrack", root, 980, 650, 26, 830, null, Color.clear);
            track.GetComponent<Image>().raycastTarget = true;
            Panel("Rail", track, 10, 0, 5, 830, null, new Color32(118, 85, 43, 70));
            var handle = Panel("Handle", track, 0, 0, 26, 160, null, Color.clear);
            Fill(handle);
            handle.GetComponent<Image>().raycastTarget = true;
            var handleArt = Box("Grip", handle, null, Gold);
            Fill(handleArt.rectTransform); handleArt.rectTransform.offsetMin = new Vector2(9, 0);
            handleArt.rectTransform.offsetMax = new Vector2(-9, 0);
            var bar = track.gameObject.AddComponent<Scrollbar>(); bar.handleRect = handle;
            bar.targetGraphic = handle.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            catalogLabel = Text("ScrollHint", root, 140, 1480, 828, 32, "", 20, muted);
            catalogLabel.alignment = TextAlignmentOptions.Right;

            Rule(root, 128, 1518, 858);
            summary = Text("FactionSummary", root, 144, 1534, 504, 44, "", 26, Ink, true);
            validity = Icon("Validity", root, 681, 1534, 40, RoomHelmGraphic.Symbol.Ready, Good);
            total = Text("Total", root, 728, 1532, 260, 48, "", 31, Good, true);
            notice = Text("Validation", root, 144, 1592, 836, 84, "", 24, muted);
            notice.textWrappingMode = TextWrappingModes.Normal;
            notice.enableAutoSizing = false;
            notice.alignment = TextAlignmentOptions.TopLeft;
            Button("Cancel", root, 116, 1722, 312, 112, "취소", 35).onClick.AddListener(() => cancel());
            apply = Button("Apply", root, 452, 1722, 548, 112, "구성 적용", 38, true);
            apply.onClick.AddListener(() => confirm());
            applyGroup = apply.gameObject.AddComponent<CanvasGroup>();
            BuildTooltip();
            Fit();
        }

        /// <summary>직업 설명 상자. 클릭을 가로채지 않도록 레이캐스트를 끄고, 다른 UI보다 위에 그린다.</summary>
        private void BuildTooltip()
        {
            tooltip = Wood("RoleTooltip", root, 0, 0, TooltipWidth, 160);
            var group = tooltip.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            float width = TooltipWidth - TooltipPad * 2;
            tooltipTitle = Text("Title", tooltip, TooltipPad, 16, width, 42, "", 29, Cream, true);
            tooltipBody = Text("Description", tooltip, TooltipPad, 64, width, 40, "", 25, Cream);
            tooltipBody.textWrappingMode = TextWrappingModes.Normal;
            tooltipBody.enableAutoSizing = false;
            tooltipBody.overflowMode = TextOverflowModes.Overflow;
            tooltipBody.alignment = TextAlignmentOptions.TopLeft;
            tooltipGoal = Text("Goal", tooltip, TooltipPad, 0, width, 34, "", 23, Gold, true);
            foreach (TMP_Text text in new[] { tooltipTitle, tooltipBody, tooltipGoal }) text.richText = false; // 이름은 서버가 준 문자열
            tooltip.gameObject.SetActive(false);
        }

        private void ShowTooltip(RoleRow row)
        {
            if (tooltip == null || row?.Role == null) return;
            tipRow = row;
            string faction = Faction(row.Role);
            tooltipTitle.text = (row.Role.name ?? row.Role.code) + "  ·  " + FactionLabel(faction);
            tooltipBody.text = RoleInfoText.Description(row.Role.code);
            tooltipGoal.text = "목표 · " + RoleInfoText.Goal(row.Role.code, faction);
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
            float width = TooltipWidth - TooltipPad * 2;
            float body = Mathf.Ceil(tooltipBody.GetPreferredValues(tooltipBody.text, width, 0).y);
            Place(tooltipBody.rectTransform, TooltipPad, 64, width, body);
            Place(tooltipGoal.rectTransform, TooltipPad, 64 + body + 10, width, 34);
            tooltip.sizeDelta = new Vector2(TooltipWidth, 64 + body + 10 + 34 + 20);
            PlaceTooltip();
        }

        /// <summary>row가 아직 설명을 띄운 줄일 때만 닫는다 (다른 줄로 옮겨 간 뒤 늦게 온 이탈은 무시). null이면 무조건 닫는다.</summary>
        private void HideTooltip(RoleRow row)
        {
            if (row != null && row != tipRow) return;
            tipRow = null;
            if (tooltip != null) tooltip.gameObject.SetActive(false);
        }

        /// <summary>
        /// 설명 상자를 올린 줄의 이름 쪽(초상화 오른쪽)에 맞춰 줄 바로 아래에 둔다. 목록 아래로 넘치면 줄 위에 둔다.
        /// 줄이 목록 밖으로 스크롤되어 보이지 않으면 닫는다.
        /// </summary>
        private void PlaceTooltip()
        {
            if (tipRow == null) return;
            if (tipRow.Rect == null || !tipRow.Rect.gameObject.activeInHierarchy)
            {
                HideTooltip(null);
                return;
            }
            float rowTop = TopOf(tipRow.Rect, out float rowBottom, out float rowLeft);
            float viewTop = TopOf(viewport, out float viewBottom, out _);
            if (rowBottom <= viewTop || rowTop >= viewBottom)
            {
                HideTooltip(null);
                return;
            }
            float height = tooltip.sizeDelta.y;
            float x = Mathf.Clamp(rowLeft + 100, 40, root.rect.width - 40 - TooltipWidth);
            float y = rowBottom + 6 + height <= viewBottom + 40 ? rowBottom + 6 : rowTop - 6 - height;
            Place(tooltip, x, y, TooltipWidth, height);
        }

        /// <summary>rect의 윗변 y를 root의 Place 좌표(왼쪽 위가 0, 아래로 +)로 돌려주고, 아랫변 y와 왼쪽 x도 함께 준다.</summary>
        private float TopOf(RectTransform rect, out float bottom, out float left)
        {
            rect.GetWorldCorners(corners);
            Vector3 lower = root.InverseTransformPoint(corners[0]); // 왼쪽 아래
            Vector3 upper = root.InverseTransformPoint(corners[1]); // 왼쪽 위
            UnityEngine.Rect area = root.rect;
            left = lower.x - area.xMin;
            bottom = area.yMax - lower.y;
            return area.yMax - upper.y;
        }

        private static string FactionLabel(string faction)
            => faction == Factions.Crew ? "선원 진영" : faction == Factions.Pirate ? "해적 진영" : "중립 진영";

        public void SetSaving(bool saving)
        {
            controls.interactable = !saving;
            if (saving)
            {
                notice.color = muted;
                notice.text = "직업 구성을 방에 저장하는 중...";
            }
        }

        public void ShowSaveError(string message)
        {
            notice.color = Bad;
            notice.text = message;
        }

        public void BindRoles(RoleSetupOptionsDto options, Action<string, int> quantityChanged, Action<string, bool> candidateChanged)
        {
            HideTooltip(null);
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            rows.Clear(); factionCounts.Clear();
            var seen = new HashSet<string>();
            var roles = (options.roles ?? new List<RoleDto>()).Where(r => r != null && !string.IsNullOrEmpty(r.code) && seen.Add(r.code)).ToList();
            neutralRandom = roles.Any(r => Faction(r) == Factions.Neutral && RoleSetupRules.IsRandomOption(options, r.code));
            foreach (string faction in new[] { Factions.Crew, Factions.Pirate, Factions.Neutral })
            {
                var group = roles.Where(r => Faction(r) == faction).ToList();
                if (group.Count == 0) continue;
                Header(faction);
                foreach (RoleDto role in group) AddRole(role, options, quantityChanged, candidateChanged);
            }
            catalogLabel.text = $"전체 {rows.Count}개 직업 · 목록을 내려 모든 진영을 확인하세요 · 직업 이름을 누르면 설명이 나와요";
        }

        private void Header(string faction)
        {
            Color tint = faction == Factions.Crew ? new Color32(129, 161, 134, 110)
                : faction == Factions.Pirate ? new Color32(176, 94, 79, 100) : new Color32(115, 143, 170, 105);
            var header = Box("Faction_" + faction, content, null, tint);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            string label = faction == Factions.Crew ? "선원 진영" : faction == Factions.Pirate ? "해적 진영" : "중립 진영";
            var symbol = faction == Factions.Crew ? RoomHelmGraphic.Symbol.Helm
                : faction == Factions.Pirate ? RoomHelmGraphic.Symbol.Crown : RoomHelmGraphic.Symbol.Waiting;
            Icon("Emblem", header.transform, 14, 11, 40, symbol, Ink);
            Text("Name", header.transform, 70, 9, 290, 46, label, 32, Ink, true);
            if (faction == Factions.Neutral && !neutralRandom) Text("ManualOnly", header.transform, 352, 15, 268, 36, "직접 설정 전용", 22, muted);
            var count = Text("Count", header.transform, 686, 11, 148, 44, "", 29, Ink, true);
            count.alignment = TextAlignmentOptions.Right;
            factionCounts[faction] = count;
        }

        private void AddRole(RoleDto role, RoleSetupOptionsDto options, Action<string, int> quantityChanged, Action<string, bool> candidateChanged)
        {
            var row = Box("Role_" + role.code, content, null, new Color(1, 1, 1, .08f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            var model = new RoleRow { Role = role, Rect = row.rectTransform, Whole = row.gameObject.AddComponent<CanvasGroup>() };
            var mask = Icon("PortraitMask", row.transform, 16, 11, 78, RoomHelmGraphic.Symbol.Disc, new Color32(54, 34, 20, 255));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            Sprite portraitArt = portraits?.Find(PortraitId(role.code));
            if (portraitArt != null)
            {
                var portrait = Box("Portrait", mask.transform, portraitArt, Color.white);
                Place(portrait.rectTransform, -13, -5, 104, 110); portrait.preserveAspect = true;
            }
            else Icon("UnknownPortrait", mask.transform, 15, 15, 48, RoomHelmGraphic.Symbol.Helm, Cream);
            Icon("Medallion", row.transform, 10, 5, 90, RoomHelmGraphic.Symbol.Medallion, Gold);
            model.Name = Text("Name", row.transform, 112, 18, 344, 48, role.name ?? role.code, 31, Ink, true);
            model.Name.richText = false;
            bool canChoose = RoleSetupRules.IsRandomOption(options, role.code);
            string hint = role.code == RoleCodes.CrewSailor ? "남는 자리에 자동 배정"
                : Faction(role) == Factions.Neutral && !canChoose ? "직접 설정 전용" : RoleSetupRules.IsBaseRole(role.code) ? "공격 가능한 기본 직업" : "";
            Text("Hint", row.transform, 114, 64, 338, 26, hint, 20, muted);
            // 초상화·이름 쪽(수량 버튼 왼쪽)을 누르면 직업 설명을 띄우고, 다시 누르거나 포인터가 벗어나면 닫는다.
            // 버튼을 누를 때는 뜨지 않는다. 초상화 그래픽도 클릭을 받으므로 그 위에 투명하게 덮는다. 드래그는 그대로 목록 스크롤로 간다.
            var info = Panel("InfoArea", row.transform, 0, 0, 456, 100, null, Color.clear);
            info.GetComponent<Image>().raycastTarget = true;
            var press = info.gameObject.AddComponent<PointerPressRelay>();
            press.Pressed = () => { if (tipRow == model) HideTooltip(model); else ShowTooltip(model); };
            press.Exited = () => HideTooltip(model);
            var amount = Rect("Amount", row.transform); Place(amount, 462, 0, 250, 100);
            model.Amount = amount.gameObject.AddComponent<CanvasGroup>();
            model.Minus = Button("Minus", amount, 0, 12, 72, 76, "−", 32);
            model.Plus = Button("Plus", amount, 172, 12, 72, 76, "+", 32);
            model.Minus.onClick.AddListener(() => quantityChanged(role.code, -1));
            model.Plus.onClick.AddListener(() => quantityChanged(role.code, 1));
            model.Count = Text("Count", amount, 77, 20, 90, 60, "", 32, Ink, true);
            model.Count.alignment = TextAlignmentOptions.Center;
            var candidate = Panel("CandidateArea", row.transform, 738, 10, 100, 80, null, Color.clear);
            model.CandidateArea = candidate.gameObject.AddComponent<CanvasGroup>();
            if (canChoose)
            {
                candidate.GetComponent<Image>().raycastTarget = true;
                var rim = Panel("Box", candidate, 20, 12, 56, 56, null, Gold);
                Panel("Inside", rim, 2, 2, 52, 52, null, new Color32(246, 231, 198, 255));
                var mark = Icon("Check", rim, 6, 6, 44, RoomHelmGraphic.Symbol.Ready, Good);
                model.Candidate = candidate.gameObject.AddComponent<Toggle>();
                model.Candidate.targetGraphic = candidate.GetComponent<Image>(); model.Candidate.graphic = mark;
                model.Candidate.onValueChanged.AddListener(value => candidateChanged(role.code, value));
            }
            else if (RoleSetupRules.IsBaseRole(role.code))
                Icon("AlwaysIncluded", candidate, 28, 20, 40, RoomHelmGraphic.Symbol.Lock, muted);
            else Text("Excluded", candidate, 16, 14, 66, 52, "—", 28, muted).alignment = TextAlignmentOptions.Center;
            Rule(row.transform, 8, 99, 838);
            rows.Add(model);
        }

        public void Render(RoleSetupDto setup, RoleSetupOptionsDto options, int players, int min, int max, string problem)
        {
            bool random = setup.mode == RoleSetupModes.Random;
            var composition = random ? RoleSetupRules.Recommended(options, players)
                : RoleSetupRules.FixedComposition(setup, options, players);
            countLabel.text = $"{players}명"; rangeLabel.text = $"{min}–{max}명";
            previous.interactable = players > min; next.interactable = players < max;
            bool edited = setup.mode == RoleSetupModes.Custom && RoleSetupRules.FindCustom(setup, players) != null;
            modeLabel.text = random ? "랜덤 배정 중" : edited ? "직접 설정 중" : "추천 구성";
            randomToggle.SetIsOnWithoutNotify(random);
            switchBack.color = random ? Good : new Color32(86, 72, 52, 255);
            Place(switchKnob.rectTransform, random ? 90 : 7, 5, 54, 54);
            Place(switchLabel.rectTransform, random ? 8 : 60, 8, 84, 48);
            switchLabel.text = random ? "켜짐" : "꺼짐";
            helper.text = random ? "인원별 진영 수를 유지하고 체크한 후보에서 무작위로 배정해요."
                : "다른 직업의 수를 조정하면 남는 선원 자리가 자동으로 바뀌어요.";
            foreach (RoleRow row in rows)
            {
                row.Count.text = random ? "—" : RoleSetupRules.Count(composition, row.Role.code).ToString();
                row.Minus.interactable = !random && RoleSetupRules.CanRemove(composition, row.Role.code);
                row.Plus.interactable = !random && RoleSetupRules.CanAdd(composition, row.Role.code);
                row.Amount.alpha = random || row.Role.code == RoleCodes.CrewSailor ? .48f : 1;
                row.Whole.alpha = random && row.Candidate == null && Faction(row.Role) == Factions.Neutral ? .55f : 1;
                row.CandidateArea.alpha = random ? 1 : .72f;
                if (row.Candidate != null) row.Candidate.SetIsOnWithoutNotify(RoleSetupRules.IsCandidate(setup, row.Role.code));
            }
            int pirates = random ? RoleSetupRules.PirateSlots(options, players) : RoleSetupRules.CountPirates(options, composition);
            int neutrals = random ? 0 : composition.Count(c => RoleSetupRules.FindRole(options, c)?.faction == Factions.Neutral);
            int crew = composition.Count - pirates - neutrals;
            // 랜덤에서 중립 후보를 체크했으면 중립은 선원 진영 자리에 섞여 나오므로 몇 명인지 게임 시작 때 정해진다
            bool neutralDrawn = random && rows.Any(r => r.Candidate != null && Faction(r.Role) == Factions.Neutral
                && RoleSetupRules.IsCandidate(setup, r.Role.code));
            foreach (var pair in factionCounts)
                pair.Value.text = pair.Key == Factions.Neutral && neutralDrawn ? "무작위"
                    : $"{(pair.Key == Factions.Crew ? crew : pair.Key == Factions.Pirate ? pirates : neutrals)}명";
            summary.text = neutralDrawn ? $"선원·중립 {crew}  ·  해적 {pirates}" : $"선원 {crew}  ·  해적 {pirates}  ·  중립 {neutrals}";
            total.text = $"총 {composition.Count} / {players}명";
            total.color = problem == null ? Good : Bad;
            validity.Shape = problem == null ? RoomHelmGraphic.Symbol.Ready : RoomHelmGraphic.Symbol.Waiting;
            validity.color = total.color;
            notice.color = problem == null ? muted : Bad;
            notice.text = problem ?? (random ? "직업별 수량은 게임 시작 시 결정됩니다.\n선원과 해적은 항상 후보이며, "
                    + (neutralRandom ? "체크한 중립은 선원 진영 자리에 섞여 나와요." : "중립은 직접 설정에서만 배정돼요.")
                : "선택한 인원수의 구성입니다. 다른 인원수도 위에서 편집할 수 있어요.\n선원과 해적은 항상 랜덤 후보에 포함돼요.");
            apply.interactable = problem == null;
            applyGroup.alpha = problem == null ? 1 : .45f;
        }

        public void ScrollToTop() { Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }

        private void LateUpdate()
        {
            Fit();
            PlaceTooltip(); // 목록이 스크롤되면 설명 상자도 줄을 따라간다
        }

        private void OnDisable() => HideTooltip(null);
        private void Fit()
        {
            if (root == null) return;
            UnityEngine.Rect area = Screen.safeArea;
            safeArea.anchorMin = new Vector2(area.x / Mathf.Max(1, Screen.width), area.y / Mathf.Max(1, Screen.height));
            safeArea.anchorMax = new Vector2(area.xMax / Mathf.Max(1, Screen.width), area.yMax / Mathf.Max(1, Screen.height));
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            root.localScale = Vector3.one * Mathf.Max(.01f, Mathf.Min(safeArea.rect.width / 1080f, safeArea.rect.height / 1920f));
        }

        private TMP_Text Text(string name, Transform parent, float x, float y, float w, float h, string value, float size, Color color, bool bold = false)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            Place(text.rectTransform, x, y, w, h); text.font = font; text.text = value; text.fontSize = size;
            text.color = color; text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true; text.fontSizeMin = size * .82f; text.fontSizeMax = size;
            text.overflowMode = TextOverflowModes.Ellipsis; text.alignment = TextAlignmentOptions.MidlineLeft;
            if (bold) text.fontStyle = FontStyles.Bold;
            return text;
        }

        private Button Button(string name, Transform parent, float x, float y, float w, float h, string label, float size, bool primary = false)
        {
            var image = Box(name, parent, primary ? null : buttonArt, primary ? new Color32(146, 38, 29, 255) : new Color32(185, 160, 122, 255));
            Place(image.rectTransform, x, y, w, h); image.raycastTarget = true;
            var edge = Box("BrassFrame", image.transform, frame, Color.white); Fill(edge.rectTransform);
            var button = image.gameObject.AddClickSoundButton(); button.targetGraphic = image;
            var colors = button.colors; colors.disabledColor = new Color(.48f, .48f, .48f, .62f); button.colors = colors;
            Text("Label", image.transform, 6, 4, w - 12, h - 8, label, size, Cream, true).alignment = TextAlignmentOptions.Center;
            return button;
        }

        private RectTransform Wood(string name, Transform parent, float x, float y, float w, float h)
        {
            var panel = Panel(name, parent, x, y, w, h, null, new Color32(38, 23, 14, 255));
            var wood = Rect("Walnut", panel).gameObject.AddComponent<RawImage>(); Fill(wood.rectTransform);
            wood.texture = cabin != null ? cabin.texture : null; wood.uvRect = new UnityEngine.Rect(.26f, .37f, .45f, .29f);
            wood.color = new Color(.48f, .48f, .48f, 1); wood.raycastTarget = false;
            var rim = Box("BrassFrame", panel, frame, Color.white); Fill(rim.rectTransform);
            return panel;
        }

        private static void Rule(Transform parent, float x, float y, float width)
            => Panel("Rule", parent, x, y, width, 1, null, new Color32(151, 111, 54, 130));
        private static Sprite Art(string name) => Resources.LoadAll<Sprite>("RoomLobby/" + name).FirstOrDefault();
        private static string Faction(RoleDto role) => role.faction == Factions.Crew || role.faction == Factions.Pirate ? role.faction : Factions.Neutral;
        private static string PortraitId(string code)
        {
            switch (code)
            {
                case "NEUTRAL_GHOST_CAPTAIN": return "CONCEPT_GHOST_CAPTAIN";
                case "NEUTRAL_SIREN": return "CONCEPT_SIREN";
                case "NEUTRAL_KRAKEN": return "PIRATE_KRAKEN";
                case "NEUTRAL_MERMAID": return "THIRD_MERMAID";
                default: return code;
            }
        }
    }
}
