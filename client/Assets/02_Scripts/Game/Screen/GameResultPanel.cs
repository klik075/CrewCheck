using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WhoisntCitizen.GameUI;

namespace WhoisntCitizen.Game
{
    /// <summary>
    /// 게임 결과 패널. 승리 진영, 내 승패, 전원의 실제 직업. "대기실로" 버튼을 누르면 BackRequested.
    /// 자동 복귀가 켜져 있으면 등장 연출이 끝난 뒤 autoReturnSeconds초가 지나면 BackRequested를 낸다.
    /// </summary>
    public sealed class GameResultPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TextMeshProUGUI winnerText;
        [SerializeField] private TextMeshProUGUI outcomeText;
        [SerializeField] private TextMeshProUGUI playersText;
        [SerializeField] private Button backButton;
        [Tooltip("결과 등장 연출이 끝난 뒤 자동으로 대기실로 돌아가기까지의 시간(초). 0 이하면 자동 복귀하지 않는다.")]
        private float autoReturnSeconds = 20f;

        private readonly Dictionary<string, Sprite> backgrounds = new Dictionary<string, Sprite>();
        private Image backgroundImage;
        private AspectRatioFitter backgroundFit;
        private bool presentationReady;
        private GameResultSequence sequence;
        private GameResultCardView cardView;
        private bool returnAfterIntro;
        private bool returnRequested;

        public Func<long, ChibiCharacterSkin> SkinOf { get; set; }
        public bool IsIntroPlaying => sequence != null && sequence.IsPlaying;

        // 자동 복귀 카운트다운
        private bool counting;
        private float remaining;
        private int shownSeconds = -1;
        private TextMeshProUGUI backLabel;
        private string backLabelText;

        /// <summary>"대기실로" 버튼</summary>
        public event Action BackRequested;

        /// <summary>켜고 끌 오브젝트. 비어 있으면 자기 자신 (꺼진 채로 시작해 Awake 전에 불려도 안전하게).</summary>
        private GameObject Root
        {
            get { return root != null ? root : gameObject; }
        }

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackClicked);
                backLabel = backButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (backLabel != null) backLabelText = backLabel.text;
            }
        }

        private void Update()
        {
            if (!counting) return;
            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                OnBackClicked(); // 카운트다운도 여기서 멈춘다
                return;
            }
            RefreshCountdownLabel();
        }

        private void OnDestroy()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(OnBackClicked);
            }
        }

        /// <param name="autoReturn">false면 자동 복귀하지 않는다. (가짜 서버 게임: 자동으로 새 판이 이어지지 않게)</param>
        public void Show(GameResultDto result, MyRoleDto me, bool autoReturn = true)
        {
            if (result == null) return;
            StopCountdown();
            returnRequested = false;
            returnAfterIntro = autoReturn && autoReturnSeconds > 0f;
            PreparePresentation();
            ShowBackground(result, me);
            if (winnerText != null)
            {
                winnerText.text = ReportFormatter.ResultHeadline(result); // 예: "크라켄 승리", "세이렌 팀 승리"
            }
            if (outcomeText != null)
            {
                outcomeText.text = GameScreenText.ResultOutcome(result, me);
            }
            if (playersText != null)
            {
                playersText.text = GameScreenText.NoRichText(string.Join("\n", ReportFormatter.ResultLines(result).ToArray()));
            }
            Root.SetActive(true);
            Root.transform.SetAsLastSibling();
            cardView?.Show(result, me, SkinOf, backgroundImage.sprite != null);
            sequence.Play(result, me, SkinOf, backgroundImage.sprite != null, OnIntroFinished);
        }

        private void OnIntroFinished()
        {
            if (returnAfterIntro && Root.activeInHierarchy && !returnRequested) StartCountdown();
        }

        private void OnDisable()
        {
            StopCountdown();
            if (sequence != null) sequence.Stop();
        }

        private void StartCountdown()
        {
            counting = true;
            remaining = autoReturnSeconds;
            shownSeconds = -1;
            RefreshCountdownLabel();
        }

        private void StopCountdown()
        {
            counting = false;
            shownSeconds = -1;
            if (backLabel != null && backLabelText != null) backLabel.text = backLabelText;
        }

        /// <summary>버튼 라벨 아래 줄에 남은 초를 작게 붙인다. 예) "대기실로" + "(7초 뒤 자동 복귀)". 초가 바뀔 때만 갱신.</summary>
        private void RefreshCountdownLabel()
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, remaining));
            if (seconds == shownSeconds || backLabel == null) return;
            shownSeconds = seconds;
            backLabel.richText = true;
            backLabel.text = backLabelText + "\n<size=70%>(" + seconds + "초 뒤 자동 복귀)</size>"; // 버튼 이름보다 작게
        }

        /// <summary>
        /// Uses the local player's outcome (the server's winnerIds), not the faction: a seduced sailor wins with the siren
        /// team, and the neutral roles win alone. Siren, kraken, ghost captain and mermaid wins use the personal art.
        /// Cancelled/unknown results have no win/loss art.
        /// </summary>
        public static string BackgroundName(GameResultDto result, MyRoleDto me)
        {
            if (result == null || me == null || !result.ended || result.IsCancelled || !KnownWinner(result.winner))
                return null;
            if (!GameStateQueries.IsWinner(result, me.playerId, me.faction)) return "Defeat";
            switch (result.winner)
            {
                case Winners.Crew: return "CrewVictory";
                case Winners.Pirate: return "PirateVictory";
                default: return "PersonalVictory";
            }
        }

        private static bool KnownWinner(string winner)
        {
            return winner == Winners.Crew || winner == Winners.Pirate || Winners.IsPersonal(winner);
        }

        private void ShowBackground(GameResultDto result, MyRoleDto me)
        {
            string name = BackgroundName(result, me);
            Sprite sprite = null;
            if (name == "PersonalVictory") sprite = PersonalArt(result.winner);
            if (sprite == null && name != null && !backgrounds.TryGetValue(name, out sprite))
            {
                sprite = Resources.Load<Sprite>("GameResult/" + name);
                if (sprite != null) backgrounds.Add(name, sprite);
            }
            backgroundImage.sprite = sprite;
            backgroundImage.gameObject.SetActive(sprite != null);
            if (sprite != null) backgroundFit.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        /// <summary>
        /// The neutral winner's own picture (시안 H): GameResult/KrakenVictory, SirenVictory, GhostCaptainVictory or
        /// MermaidVictory when that art exists, else the role's ability cut-in art (NightScene/AbilityCutIns), else null
        /// (the shared PersonalVictory picture).
        /// </summary>
        private Sprite PersonalArt(string winner)
        {
            string art, cutIn;
            switch (winner)
            {
                case Winners.Kraken: art = "KrakenVictory"; cutIn = "PIRATE_KRAKEN"; break;
                case Winners.Siren: art = "SirenVictory"; cutIn = "CONCEPT_SIREN"; break;
                case Winners.GhostCaptain: art = "GhostCaptainVictory"; cutIn = "CONCEPT_GHOST_CAPTAIN"; break;
                case Winners.Mermaid: art = "MermaidVictory"; cutIn = "THIRD_MERMAID"; break;
                default: return null;
            }
            Sprite sprite;
            if (backgrounds.TryGetValue(art, out sprite)) return sprite;
            sprite = Resources.Load<Sprite>("GameResult/" + art);
            if (sprite == null)
            {
                AbilityCutInCatalog cutIns = Resources.Load<AbilityCutInCatalog>("NightScene/AbilityCutIns");
                if (cutIns != null) cutIns.TryFind(cutIn, out sprite, out _);
            }
            if (sprite != null) backgrounds.Add(art, sprite);
            return sprite;
        }

        private void PreparePresentation()
        {
            if (presentationReady) return;
            presentationReady = true;
            var art = new GameObject("ResultBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AspectRatioFitter));
            art.layer = Root.layer;
            art.transform.SetParent(Root.transform, false);
            art.transform.SetAsFirstSibling();
            backgroundImage = art.GetComponent<Image>();
            backgroundImage.raycastTarget = false;
            backgroundFit = art.GetComponent<AspectRatioFitter>();
            backgroundFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            // Keep the upper scenery visible; the translucent lower card still fits a full 12-player roster.
            var card = Root.transform.Find("Card") as RectTransform;
            if (card != null)
            {
                card.anchorMin = new Vector2(0.07f, 0.04f);
                card.anchorMax = new Vector2(0.93f, 0.60f);
                card.offsetMin = card.offsetMax = Vector2.zero;
                Image surface = card.GetComponent<Image>();
                if (surface != null) surface.color = new Color(0.025f, 0.055f, 0.08f, 0.86f);
            }
            if (winnerText != null)
            {
                PlaceAtTop(winnerText.rectTransform, 30f, 80f);
                winnerText.enableAutoSizing = true;
                winnerText.fontSizeMin = 32f;
                winnerText.fontSizeMax = 54f;
                winnerText.alignment = TextAlignmentOptions.Center;
            }
            if (outcomeText != null)
            {
                PlaceAtTop(outcomeText.rectTransform, 112f, 64f);
                outcomeText.enableAutoSizing = true;
                outcomeText.fontSizeMin = 24f;
                outcomeText.fontSizeMax = 40f;
                outcomeText.alignment = TextAlignmentOptions.Center;
            }
            if (playersText != null)
            {
                RectTransform rect = playersText.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(48f, 150f);
                rect.offsetMax = new Vector2(-48f, -206f);
                playersText.alignment = TextAlignmentOptions.TopLeft;
                playersText.enableAutoSizing = true;
                playersText.fontSizeMin = 20f;
                playersText.fontSizeMax = 34f;
            }
            if (backButton != null)
            {
                var rect = (RectTransform)backButton.transform;
                rect.anchorMin = new Vector2(0.24f, 0f);
                rect.anchorMax = new Vector2(0.76f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 30f);
                rect.sizeDelta = new Vector2(0f, 94f);
                TextMeshProUGUI label = backButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 20f;
                    label.fontSizeMax = 36f;
                }
            }
            sequence = Root.GetComponent<GameResultSequence>();
            if (sequence == null) sequence = Root.AddComponent<GameResultSequence>();
            if (card != null) cardView = new GameResultCardView(card, winnerText, outcomeText, playersText, backButton);
            sequence.Initialize(winnerText, outcomeText, playersText, backButton, cardView);
        }

        private static void PlaceAtTop(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-64f, height);
        }

        public void Hide()
        {
            StopCountdown();
            if (sequence != null) sequence.Stop();
            Root.SetActive(false);
        }

        private void OnBackClicked()
        {
            if (IsIntroPlaying) return; // 등장 연출은 끝까지 보여 준다 (누르면 넘기지 않는다)
            if (returnRequested) return;
            returnRequested = true;
            StopCountdown(); // 버튼과 타이머가 두 번 보내지 않게
            if (BackRequested != null)
            {
                BackRequested();
            }
        }
    }
}
