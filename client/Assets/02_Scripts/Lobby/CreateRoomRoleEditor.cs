using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WhoisntCitizen.Game;

namespace WhoisntCitizen.Lobby
{
    /// <summary>로비와 룸씬에서 인원별 직업 구성을 편집한다. 저장 성공 시에만 원본에 반영한다.</summary>
    public sealed class CreateRoomRoleEditor : MonoBehaviour
    {
        private RoleSetupDto source, draft;
        private RoleSetupOptionsDto options;
        private RoleCompositionLayout view;
        private Action<string> showError;
        private Action onChanged;
        private Action<RoleSetupDto, Action<string>> save;
        private int revision;
        private bool saving;
        private int playerCount;
        private string fixedMode;
        private bool opened;

        public bool IsOpen => opened && gameObject.activeInHierarchy && view != null && view.gameObject.activeSelf;
        public bool IsSaving => saving;

        // save의 완료 콜백: null은 성공, 문자열은 화면에 표시할 오류.
        public void Initialize(Action<string> onError, Action changed, Action<RoleSetupDto, Action<string>> saveSetup = null)
        {
            showError = onError;
            onChanged = changed;
            save = saveSetup;
        }

        public void Open(RoleSetupDto roleSetup, RoleSetupOptionsDto roleOptions, int initialCount)
        {
            if (roleSetup == null || roleOptions == null) return;
            if (!EnsureView()) return;
            revision++;
            saving = false;
            view.SetSaving(false);
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
            revision++;
            saving = false;
            opened = false;
            source = null;
            draft = null;
            if (view != null) view.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            revision++;
            saving = false;
            opened = false;
            source = draft = null;
            if (view != null) view.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (view != null) Destroy(view.gameObject);
        }

        private void ChangeCount(int delta)
        {
            if (draft == null || saving) return;
            playerCount = Mathf.Clamp(playerCount + delta, Minimum, Maximum);
            Render();
        }

        private void Recommend()
        {
            if (draft == null || saving) return;
            RoleSetupRules.RemoveCustom(draft, playerCount);
            fixedMode = draft.customCompositions.Count > 0 ? RoleSetupModes.Custom : RoleSetupModes.Recommended;
            draft.mode = fixedMode;
            Render();
        }

        private void SetRandom(bool enabled)
        {
            if (draft == null || saving) return;
            if (enabled && draft.mode != RoleSetupModes.Random) fixedMode = RoleSetupModes.Normalize(draft.mode);
            draft.mode = enabled ? RoleSetupModes.Random : fixedMode;
            Render();
        }

        private void ChangeQuantity(string code, int delta)
        {
            if (draft == null || saving || draft.mode == RoleSetupModes.Random || RoleSetupRules.FindRole(options, code) == null) return;
            List<string> roles = RoleSetupRules.FixedComposition(draft, options, playerCount);
            if (delta > 0 ? !RoleSetupRules.CanAdd(roles, code) : !RoleSetupRules.CanRemove(roles, code)) return;
            roles = delta > 0 ? RoleSetupRules.Add(options, roles, code) : RoleSetupRules.Remove(options, roles, code);
            RoleSetupRules.SetCustom(draft, playerCount, roles);
            fixedMode = draft.mode = RoleSetupModes.Custom;
            Render();
        }

        private void ChangeCandidate(string code, bool included)
        {
            if (draft == null || saving || !RoleSetupRules.IsRandomOption(options, code)) return;
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
            if (draft == null || source == null || saving) return;
            string problem = Problem();
            if (problem != null)
            {
                Render();
                return;
            }
            RoleSetupDto committed = draft.Clone();
            if (save == null)
            {
                Commit(committed);
                return;
            }
            saving = true;
            view.SetSaving(true);
            int requestRevision = ++revision;
            save(committed.Clone(), error =>
            {
                if (this == null || !IsOpen || requestRevision != revision || !saving) return;
                saving = false;
                view.SetSaving(false);
                if (error != null)
                {
                    Render();
                    view.ShowSaveError(error);
                    return;
                }
                Commit(committed);
            });
        }

        private void Commit(RoleSetupDto committed)
        {
            source.mode = committed.mode;
            source.customCompositions = committed.customCompositions;
            source.randomCandidates = committed.randomCandidates;
            Close();
            onChanged?.Invoke();
        }
    }
}
