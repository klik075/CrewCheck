using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WhoisntCitizen.Game
{
    /// <summary>게임 결과 패널. 승리 진영, 내 승패, 전원의 실제 직업. "대기실로" 버튼을 누르면 BackRequested.</summary>
    public sealed class GameResultPanel : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TextMeshProUGUI winnerText;
        [SerializeField] private TextMeshProUGUI outcomeText;
        [SerializeField] private TextMeshProUGUI playersText;
        [SerializeField] private Button backButton;

        private readonly Dictionary<string, Sprite> backgrounds = new Dictionary<string, Sprite>();
        private Image backgroundImage;
        private AspectRatioFitter backgroundFit;
        private bool presentationReady;

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
            }
        }

        private void OnDestroy()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(OnBackClicked);
            }
        }

        public void Show(GameResultDto result, MyRoleDto me)
        {
            if (result == null) return;
            PreparePresentation();
            ShowBackground(result, me != null ? me.faction : null);
            if (winnerText != null)
            {
                winnerText.text = !result.IsCancelled && result.winner == Factions.Neutral
                    ? "개인 승리" : ReportFormatter.ResultHeadline(result);
            }
            if (outcomeText != null)
            {
                outcomeText.text = GameScreenText.ResultOutcome(result, me != null ? me.faction : null);
            }
            if (playersText != null)
            {
                playersText.text = GameScreenText.NoRichText(string.Join("\n", ReportFormatter.ResultLines(result).ToArray()));
            }
            Root.SetActive(true);
            Root.transform.SetAsLastSibling();
        }

        /// <summary>Uses the local player's outcome, not simply the winning faction. Cancelled/unknown results have no win/loss art.</summary>
        public static string BackgroundName(GameResultDto result, string myFaction)
        {
            if (result == null || !result.ended || result.IsCancelled || !KnownFaction(result.winner) || !KnownFaction(myFaction))
                return null;
            if (result.winner != myFaction) return "Defeat";
            switch (result.winner)
            {
                case Factions.Crew: return "CrewVictory";
                case Factions.Pirate: return "PirateVictory";
                case Factions.Neutral: return "PersonalVictory";
                default: return null;
            }
        }

        private static bool KnownFaction(string faction)
        {
            return faction == Factions.Crew || faction == Factions.Pirate || faction == Factions.Neutral;
        }

        private void ShowBackground(GameResultDto result, string myFaction)
        {
            string name = BackgroundName(result, myFaction);
            Sprite sprite = null;
            if (name != null && !backgrounds.TryGetValue(name, out sprite))
            {
                sprite = Resources.Load<Sprite>("GameResult/" + name);
                if (sprite != null) backgrounds.Add(name, sprite);
            }
            backgroundImage.sprite = sprite;
            backgroundImage.gameObject.SetActive(sprite != null);
            if (sprite != null) backgroundFit.aspectRatio = sprite.rect.width / sprite.rect.height;
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
            }
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
            Root.SetActive(false);
        }

        private void OnBackClicked()
        {
            if (BackRequested != null)
            {
                BackRequested();
            }
        }
    }
}
