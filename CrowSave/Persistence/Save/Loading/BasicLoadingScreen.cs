using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CrowSave.Persistence.Save.Loading
{
    /// Minimal pluggable loading screen (TMP + optional fade via CanvasGroup).
    public sealed class BasicLoadingScreen : MonoBehaviour, ILoadingScreen
    {
        [Header("Text (optional)")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text stepsText;

        [Header("Progress Bar (optional)")]
        [Tooltip("Image must be Type=Filled, Method=Horizontal, Origin=Left.")]
        [SerializeField] private Image barFill;

        [Tooltip("Optional root GameObject for the progress bar so we can hide/show it easily.")]
        [SerializeField] private GameObject barRoot;

        [Header("Fade (optional)")]
        [Tooltip("CanvasGroup used to fade this screen (often placed on a full-screen panel root).")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Min(0f)] [SerializeField] private float fadeInDuration = 0.15f;
        [Min(0f)] [SerializeField] private float fadeOutDuration = 0.15f;

        [Tooltip("If true, blocks raycasts while visible.")]
        [SerializeField] private bool blockRaycastsWhenVisible = true;

        [Header("Feel / Timing")]
        [Tooltip("Guarantee the loading UI stays visible for at least this long (unscaled seconds).")]
        [Min(0f)] [SerializeField] private float minVisibleSeconds = 0.35f;

        [Tooltip("If true, progress will never reach 100% while 'working'. It only finishes when Hide() is called.")]
        [SerializeField] private bool holdAtNinetyEightUntilHide = true;

        [Tooltip("Working progress cap (only used if holdAtNinetyEightUntilHide is enabled).")]
        [Range(0.8f, 0.999f)] [SerializeField] private float workingProgressCap = 0.98f;

        [Tooltip("Smooth time used by SmoothDamp for the displayed progress.")]
        [Min(0.001f)] [SerializeField] private float progressSmoothTime = 0.12f;

        [Tooltip("Max speed used by SmoothDamp (units per second).")]
        [Min(0.1f)] [SerializeField] private float progressMaxSpeed = 3.0f;

        [Tooltip("When Hide() is called, we finish filling the bar to 100% over this duration (unscaled seconds).")]
        [Min(0f)] [SerializeField] private float completeFillSeconds = 0.18f;

        [Header("Indeterminate animation (optional)")]
        [SerializeField] private bool animateIndeterminateDots = true;
        [Min(0.05f)] [SerializeField] private float dotsTickSeconds = 0.20f;
        [SerializeField] private int dotsMax = 3;

        private LoadingViewMode _mode = LoadingViewMode.ProgressBar;

        private Coroutine _fadeRoutine;
        private Coroutine _hideRoutine;
        private Coroutine _dotsRoutine;

        private float _currentAlpha = 1f;

        // Progress presentation
        private float _targetProgress01;
        private float _shownProgress01;
        private float _progressVel;

        // Visibility state
        private float _shownAtUnscaled;
        private bool _hideRequested;

        // Status base text (for indeterminate dots)
        private string _baseStatus = "";

        public void Show(LoadingContext ctx, LoadingViewMode viewMode)
        {
            _mode = viewMode;

            // Cancel any pending hide/fade/dots
            if (_hideRoutine != null) { StopCoroutine(_hideRoutine); _hideRoutine = null; }
            if (_dotsRoutine != null) { StopCoroutine(_dotsRoutine); _dotsRoutine = null; }
            _hideRequested = false;

            gameObject.SetActive(true);

            _shownAtUnscaled = Time.unscaledTime;

            // Steps visibility
            if (stepsText != null)
                stepsText.gameObject.SetActive(viewMode == LoadingViewMode.Steps || viewMode == LoadingViewMode.BarAndSteps);

            // Default title
            _baseStatus = BuildDefaultTitle(ctx);
            if (statusText != null)
            {
                statusText.gameObject.SetActive(true);
                statusText.text = _baseStatus;
            }

            if (stepsText != null)
                stepsText.text = "";

            // Bar visibility
            bool wantsBar = (viewMode == LoadingViewMode.ProgressBar || viewMode == LoadingViewMode.BarAndSteps);
            if (barRoot != null) barRoot.SetActive(wantsBar);
            else if (barFill != null) barFill.gameObject.SetActive(wantsBar);

            // Reset progress presentation
            _targetProgress01 = 0f;
            _shownProgress01 = 0f;
            _progressVel = 0f;
            SetProgressInternal(0f);

            // Fade in
            if (fadeGroup != null)
            {
                SetFadeInteractivity(true);
                _currentAlpha = 0f;
                ApplyFadeAlpha(_currentAlpha);
                StartFadeTo(1f, fadeInDuration);
            }

            // Indeterminate dots animation
            if (_mode == LoadingViewMode.Indeterminate && animateIndeterminateDots && statusText != null)
            {
                _dotsRoutine = StartCoroutine(IndeterminateDotsRoutine());
            }
        }

        public void Hide()
        {
            if (!gameObject.activeInHierarchy) return;
            if (_hideRequested) return;
            _hideRequested = true;
            if (_dotsRoutine != null) { StopCoroutine(_dotsRoutine); _dotsRoutine = null; }
            if (statusText != null) statusText.text = _baseStatus;
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            _hideRoutine = StartCoroutine(HideRoutine());
        }

        public void SetStatus(string text)
        {
            _baseStatus = text ?? "";
            if (statusText == null) return;

            // If indeterminate dots are running, they will re-apply periodically.
            // Otherwise update immediately.
            if (_dotsRoutine == null)
                statusText.text = _baseStatus;
        }

        public void SetProgress(float value01)
        {
            // Indeterminate or steps-only: ignore numeric progress
            if (_mode == LoadingViewMode.Indeterminate || _mode == LoadingViewMode.Steps)
                return;

            float clamped = Mathf.Clamp01(value01);

            // Presentation trick: don't show "100%" while still working.
            if (holdAtNinetyEightUntilHide && !_hideRequested)
                clamped = Mathf.Min(clamped, workingProgressCap);

            _targetProgress01 = clamped;
        }

        public void SetSteps(string[] steps, int activeIndex)
        {
            if (stepsText == null) return;
            if (steps == null || steps.Length == 0) { stepsText.text = ""; return; }

            var s = "";
            for (int i = 0; i < steps.Length; i++)
            {
                s += (i == activeIndex ? "> " : "  ");
                s += steps[i] ?? "";
                if (i < steps.Length - 1) s += "\n";
            }
            stepsText.text = s;
        }

        private void Update()
        {
            // Smoothly present progress each frame
            if (barFill == null) return;

            // If no bar wanted, ignore
            bool wantsBar = _mode == LoadingViewMode.ProgressBar || _mode == LoadingViewMode.BarAndSteps;
            if (!wantsBar) return;

            _shownProgress01 = Mathf.SmoothDamp(
                _shownProgress01,
                _targetProgress01,
                ref _progressVel,
                progressSmoothTime,
                progressMaxSpeed,
                Time.unscaledDeltaTime
            );

            SetProgressInternal(_shownProgress01);
        }

        // ---------- Hide sequencing ----------

        private IEnumerator HideRoutine()
        {
            // Ensure at least one rendered frame after Show() even if Hide() is called immediately.
            yield return null;

            // Respect minimum visible time
            float elapsed = Time.unscaledTime - _shownAtUnscaled;
            float remaining = Mathf.Max(0f, minVisibleSeconds - elapsed);
            if (remaining > 0f)
            {
                float t = 0f;
                while (t < remaining)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // If we have a bar, finish to 100% over completeFillSeconds
            bool wantsBar = (_mode == LoadingViewMode.ProgressBar || _mode == LoadingViewMode.BarAndSteps);
            if (wantsBar && barFill != null)
            {
                float start = _shownProgress01;
                float dur = Mathf.Max(0.0001f, completeFillSeconds);

                _targetProgress01 = 1f;

                float t = 0f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    _shownProgress01 = Mathf.Lerp(start, 1f, k);
                    SetProgressInternal(_shownProgress01);
                    yield return null;
                }

                _shownProgress01 = 1f;
                SetProgressInternal(1f);
            }

            // Fade out then disable
            if (fadeGroup != null)
            {
                StartFadeTo(0f, fadeOutDuration);

                float dur = Mathf.Max(0.0001f, fadeOutDuration);
                float t = 0f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                SetFadeInteractivity(false);
            }

            gameObject.SetActive(false);
            _hideRoutine = null;
        }

        // ---------- Indeterminate dots ----------

        private IEnumerator IndeterminateDotsRoutine()
        {
            int dots = 0;

            while (true)
            {
                if (statusText != null)
                {
                    string suffix = new string('.', dots);
                    statusText.text = _baseStatus + suffix;
                }

                dots = (dots + 1) % (dotsMax + 1);

                float t = 0f;
                while (t < dotsTickSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        // ---------- Progress ----------

        private void SetProgressInternal(float value01)
        {
            if (barFill == null) return;
            barFill.fillAmount = Mathf.Clamp01(value01);
        }

        // ---------- Fade ----------

        private void StartFadeTo(float targetAlpha, float duration)
        {
            if (fadeGroup == null) return;

            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            _fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, duration));
        }

        private IEnumerator FadeRoutine(float targetAlpha, float duration)
        {
            if (fadeGroup == null) yield break;

            float startAlpha = _currentAlpha;
            float dur = Mathf.Max(0f, duration);

            if (dur <= 0f)
            {
                _currentAlpha = targetAlpha;
                ApplyFadeAlpha(_currentAlpha);
                yield break;
            }

            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                _currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, k);
                ApplyFadeAlpha(_currentAlpha);
                yield return null;
            }

            _currentAlpha = targetAlpha;
            ApplyFadeAlpha(_currentAlpha);
        }

        private void ApplyFadeAlpha(float a)
        {
            if (fadeGroup == null) return;
            fadeGroup.alpha = Mathf.Clamp01(a);
        }

        private void SetFadeInteractivity(bool visible)
        {
            if (fadeGroup == null) return;

            if (blockRaycastsWhenVisible)
            {
                fadeGroup.blocksRaycasts = visible;
                fadeGroup.interactable = visible;
            }
            else
            {
                fadeGroup.blocksRaycasts = false;
                fadeGroup.interactable = false;
            }
        }

        // ---------- Titles ----------

        private static string BuildDefaultTitle(LoadingContext ctx)
        {
            switch (ctx.Operation)
            {
                case LoadingOperation.Save: return ctx.Slot >= 0 ? $"Saving (slot {ctx.Slot})" : "Saving";
                case LoadingOperation.Load: return ctx.Slot >= 0 ? $"Loading (slot {ctx.Slot})" : "Loading";
                case LoadingOperation.Transition: return string.IsNullOrEmpty(ctx.ToScene) ? "Loading..." : $"Loading {ctx.ToScene}";
                case LoadingOperation.Checkpoint: return "Checkpoint";
                default: return "Loading";
            }
        }
    }
}
