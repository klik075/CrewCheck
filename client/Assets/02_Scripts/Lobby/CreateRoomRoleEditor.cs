using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.Lobby
{
    /// <summary>인원별 직업 구성을 임시 편집하고 적용 시에만 방 만들기 설정에 반영한다.</summary>
    public sealed class CreateRoomRoleEditor : MonoBehaviour
    {
        private RoleSetupDto source, draft;
        private RoleSetupOptionsDto options;
        private RoleCompositionLayout view;
        private Action<string> showError;
        private Action onChanged;
        private int playerCount;
        private string fixedMode;
        private bool opened;

        public bool IsOpen => opened && gameObject.activeInHierarchy && view != null && view.gameObject.activeSelf;

        public void Initialize(Action<string> onError, Action changed)
        {
            showError = onError;
            onChanged = changed;
        }

        public void Open(RoleSetupDto roleSetup, RoleSetupOptionsDto roleOptions, int initialCount)
        {
            if (roleSetup == null || roleOptions == null) return;
            if (!EnsureView()) return;
            source = roleSetup;
            draft = roleSetup.Clone();
            options = roleOptions;
            fixedMode = draft.mode == RoleSetupModes.Random
                ? (draft.customCompositions.Count > 0 ? RoleSetupModes.Custom : RoleSetupModes.Recommended)
                : RoleSetupModes.Normalize(draft.mode);
            playerCount = Mathf.Clamp(initialCount, Minimum, Maximum);
            opened = true;
            gameObject.SetActive(true);
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            EventSystem.current?.SetSelectedGameObject(null);
            view.BindRoles(options, ChangeQuantity, ChangeCandidate);
            Render();
            view.ScrollToTop();
        }

        private int Minimum => Mathf.Clamp(options != null && options.minPlayers > 0 ? options.minPlayers : 4, 4, 12);
        private int Maximum => Mathf.Clamp(options != null && options.maxPlayers > 0 ? options.maxPlayers : 12, Minimum, 12);

        private bool EnsureView()
        {
            if (view != null) return true;
            Canvas canvas = GetComponentInParent<Canvas>(true)?.rootCanvas;
            if (canvas == null)
            {
                showError?.Invoke("직업 구성 화면을 열 수 없습니다.");
                return false;
            }
            // 기존 임시 UI를 숨기고, 방 만들기 팝업의 크기와 무관한 전체 화면 오버레이를 만든다.
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            var oldImage = GetComponent<Image>();
            if (oldImage != null) oldImage.enabled = false;
            var root = RoomLobbyLayout.Rect("RoleCompositionOverlay", canvas.transform);
            view = root.gameObject.AddComponent<RoleCompositionLayout>();
            view.Build(canvas, ChangeCount, Recommend, SetRandom, Close, Apply);
            root.gameObject.SetActive(false);
            return true;
        }

        /// <summary>X, 취소, ESC는 이번 편집을 버린다.</summary>
        public void Close()
        {
            opened = false;
            source = null;
            draft = null;
            if (view != null) view.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            opened = false;
            if (view != null) view.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (view != null) Destroy(view.gameObject);
        }

        private void ChangeCount(int delta)
        {
            if (draft == null) return;
            playerCount = Mathf.Clamp(playerCount + delta, Minimum, Maximum);
            Render();
        }

        private void Recommend()
        {
            if (draft == null) return;
            RoleSetupRules.RemoveCustom(draft, playerCount);
            fixedMode = draft.customCompositions.Count > 0 ? RoleSetupModes.Custom : RoleSetupModes.Recommended;
            draft.mode = fixedMode;
            Render();
        }

        private void SetRandom(bool enabled)
        {
            if (draft == null) return;
            if (enabled && draft.mode != RoleSetupModes.Random) fixedMode = RoleSetupModes.Normalize(draft.mode);
            draft.mode = enabled ? RoleSetupModes.Random : fixedMode;
            Render();
        }

        private void ChangeQuantity(string code, int delta)
        {
            if (draft == null || draft.mode == RoleSetupModes.Random || RoleSetupRules.FindRole(options, code) == null) return;
            List<string> roles = RoleSetupRules.FixedComposition(draft, options, playerCount);
            if (delta > 0 ? !RoleSetupRules.CanAdd(roles, code) : !RoleSetupRules.CanRemove(roles, code)) return;
            roles = delta > 0 ? RoleSetupRules.Add(options, roles, code) : RoleSetupRules.Remove(options, roles, code);
            RoleSetupRules.SetCustom(draft, playerCount, roles);
            fixedMode = draft.mode = RoleSetupModes.Custom;
            Render();
        }

        private void ChangeCandidate(string code, bool included)
        {
            if (draft == null || options.randomCandidates == null || !options.randomCandidates.Contains(code)
                || RoleSetupRules.FindRole(options, code)?.faction == Factions.Neutral) return;
            RoleSetupRules.SetCandidate(draft, options, code, included);
            Render();
        }

        private string Problem()
        {
            bool random = draft.mode == RoleSetupModes.Random;
            // 랜덤은 직업 수를 미리 정하지 않으므로 서버의 해당 인원 추천 진영 배분을 확인한다.
            var roles = random ? RoleSetupRules.Recommended(options, playerCount)
                : RoleSetupRules.FixedComposition(draft, options, playerCount);
            return RoleSetupRules.Problem(options, playerCount, roles)
                ?? (draft.mode == RoleSetupModes.Custom ? RoleSetupRules.FirstCustomProblem(draft, options) : null);
        }

        private void Render()
        {
            if (draft == null) return;
            view.Render(draft, options, playerCount, Minimum, Maximum, Problem());
        }

        private void Apply()
        {
            if (draft == null || source == null) return;
            string problem = Problem();
            if (problem != null)
            {
                Render();
                return;
            }
            RoleSetupDto committed = draft.Clone();
            source.mode = committed.mode;
            source.customCompositions = committed.customCompositions;
            source.randomCandidates = committed.randomCandidates;
            Close();
            onChanged?.Invoke();
        }
    }
}
