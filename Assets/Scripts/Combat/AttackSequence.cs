using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 一回の攻撃の生存期間を管理します。攻撃系列ID、フェーズ、
    /// AttackWindowTrackerを介した攻撃有効ウィンドウの開閉を一つにまとめ、
    /// Animator eventまたはnormalized time監視のどちらからでも安全に終了できます。
    /// </summary>
    public sealed class AttackSequence
    {
        /// <summary>Animator eventがない場合に攻撃窓を開くnormalized timeです。</summary>
        public const float DefaultFallbackOpenNormalizedTime = 0.2f;

        /// <summary>デフォルトのフォールバック閉鎖時刻です。Attack clip完了前に強制的にウィンドウを閉じます。</summary>
        public const float DefaultFallbackCloseNormalizedTime = 0.9f;

        private readonly AttackWindowTracker windowTracker;
        private float fallbackOpenNormalizedTime;
        private float fallbackCloseNormalizedTime;

        private int attackSequenceId;
        private AttackSequencePhase phase = AttackSequencePhase.NotStarted;
        private bool windowHasOpened;
        private bool fallbackWindowOpenUsed;
        private bool fallbackWindowCloseUsed;

        public AttackSequence(AttackWindowTracker windowTracker, float fallbackCloseNormalizedTime = DefaultFallbackCloseNormalizedTime)
        {
            this.windowTracker = windowTracker;
            this.fallbackCloseNormalizedTime = Mathf.Clamp01(fallbackCloseNormalizedTime);
            this.fallbackOpenNormalizedTime = Mathf.Min(
                DefaultFallbackOpenNormalizedTime,
                Mathf.Max(0f, this.fallbackCloseNormalizedTime - 0.05f));
        }

        /// <summary>
        /// 現在の攻撃系列に適用する判定ウィンドウの開閉タイミングを動的に設定します。
        /// </summary>
        public void ConfigureTiming(float closeNormalizedTime, float openNormalizedTime = DefaultFallbackOpenNormalizedTime)
        {
            fallbackCloseNormalizedTime = Mathf.Clamp01(closeNormalizedTime);
            fallbackOpenNormalizedTime = Mathf.Min(openNormalizedTime, Mathf.Max(0f, fallbackCloseNormalizedTime - 0.05f));
        }

        /// <summary>この攻撃系列を一意に識別するIDです。開始されていない場合は0です。</summary>
        public int AttackSequenceId => attackSequenceId;

        /// <summary>現在のフェーズです。</summary>
        public AttackSequencePhase Phase => phase;

        /// <summary>攻撃有効ウィンドウが現在開いているかを返します。</summary>
        public bool IsWindowOpen => phase == AttackSequencePhase.WindowOpen;

        /// <summary>攻撃継続中（未終局）かを返します。</summary>
        public bool IsActive => phase == AttackSequencePhase.Active || phase == AttackSequencePhase.WindowOpen;

        /// <summary>Animator eventがない場合のフォールバック開放が使われたかを返します。</summary>
        public bool FallbackWindowOpenUsed => fallbackWindowOpenUsed;

        /// <summary>フォールバックによる強制閉鎖が使われたかを返します。診断・テスト用です。</summary>
        public bool FallbackWindowCloseUsed => fallbackWindowCloseUsed;

        /// <summary>攻撃有効ウィンドウが開いたときに発火します。</summary>
        public event Action<int> WindowOpened;

        /// <summary>攻撃有効ウィンドウが閉じたときに発火します。</summary>
        public event Action<int> WindowClosed;

        /// <summary>攻撃系列が正常に完了したときに発火します。</summary>
        public event Action<int> SequenceCompleted;

        /// <summary>攻撃系列が取消/強制終了したときに発火します。</summary>
        public event Action<int> SequenceCancelled;

        /// <summary>
        /// 新しい攻撃系列を開始します。NotStarted、Completed、Cancelledの各フェーズからだけ開始できます。
        /// </summary>
        public Result StartSequence(int sequenceId)
        {
            if (!DamageRequest.IsValidAttackSequenceId(sequenceId))
            {
                return GameError.InvalidParameter;
            }

            if (IsActive)
            {
                return GameError.ActionInProgress;
            }

            attackSequenceId = sequenceId;
            phase = AttackSequencePhase.Active;
            windowHasOpened = false;
            fallbackWindowOpenUsed = false;
            fallbackWindowCloseUsed = false;
            return Result.Ok();
        }

        /// <summary>
        /// Animator eventから呼び出す攻撃有効ウィンドウの開始点です。
        /// 重複したeventや不正なタイミングでの呼び出しは無視します。
        /// </summary>
        public Result OnAttackWindowOpenEvent()
        {
            if (phase != AttackSequencePhase.Active || windowHasOpened)
            {
                return GameError.InvalidState;
            }

            if (windowTracker == null)
            {
                return GameError.InvalidState;
            }

            Result beginResult = windowTracker.BeginWindow(attackSequenceId);
            if (beginResult.IsErr)
            {
                return beginResult;
            }

            windowHasOpened = true;
            phase = AttackSequencePhase.WindowOpen;
            WindowOpened?.Invoke(attackSequenceId);
            return Result.Ok();
        }

        /// <summary>
        /// Animator eventから呼び出す攻撃有効ウィンドウの終了点です。
        /// 重複したeventや、ウィンドウが既に閉じている場合は無視します（冪等）。
        /// </summary>
        public Result OnAttackWindowCloseEvent()
        {
            return CloseWindowInternal(isFallback: false);
        }

        /// <summary>
        /// Animator eventが欠落した場合の保険として、normalized timeを毎フレーム監視して
        /// 攻撃clip完了前に強制的にウィンドウを閉じます。Animator eventが正しく届いている限り
        /// このメソッドは何もしません。
        /// </summary>
        public void Tick(float normalizedTime)
        {
            if (phase == AttackSequencePhase.Active && !windowHasOpened && normalizedTime >= fallbackOpenNormalizedTime)
            {
                if (windowTracker != null && windowTracker.BeginWindow(attackSequenceId).IsOk)
                {
                    windowHasOpened = true;
                    fallbackWindowOpenUsed = true;
                    phase = AttackSequencePhase.WindowOpen;
                    Debug.LogWarning(
                        $"[攻撃診断] 攻撃系列{attackSequenceId}のAnimator開放eventが届かなかったため、" +
                        $"normalized time {normalizedTime:F2}で攻撃有効ウィンドウを自動開放しました。");
                    WindowOpened?.Invoke(attackSequenceId);
                }
            }

            if (phase != AttackSequencePhase.WindowOpen || normalizedTime < fallbackCloseNormalizedTime)
            {
                return;
            }

            fallbackWindowCloseUsed = true;
            Debug.LogWarning(
                $"[攻撃診断] 攻撃系列{attackSequenceId}のAnimator eventが届かなかったため、" +
                $"normalized time {normalizedTime:F2}で攻撃有効ウィンドウを強制的に閉じました。");
            CloseWindowInternal(isFallback: true);
        }

        /// <summary>
        /// 攻撃clipの再生完了時に呼び出し、攻撃系列を正常完了させます。
        /// ウィンドウが開いたままの場合は先に強制的に閉じます。
        /// </summary>
        public Result Complete()
        {
            if (phase == AttackSequencePhase.Completed || phase == AttackSequencePhase.Cancelled)
            {
                return GameError.StateAlreadyTerminal;
            }

            if (IsWindowOpen)
            {
                CloseWindowInternal(isFallback: true);
            }

            if (phase != AttackSequencePhase.Active)
            {
                return GameError.InvalidState;
            }

            phase = AttackSequencePhase.Completed;
            SequenceCompleted?.Invoke(attackSequenceId);
            return Result.Ok();
        }

        /// <summary>
        /// 攻撃を取消します。未開始または既に終局済みの場合は何もしません（冪等）。
        /// 開いているウィンドウは必ず閉じます。
        /// </summary>
        public void Cancel()
        {
            if (phase == AttackSequencePhase.NotStarted ||
                phase == AttackSequencePhase.Completed ||
                phase == AttackSequencePhase.Cancelled)
            {
                return;
            }

            if (IsWindowOpen)
            {
                CloseWindowInternal(isFallback: true);
            }

            phase = AttackSequencePhase.Cancelled;
            SequenceCancelled?.Invoke(attackSequenceId);
        }

        /// <summary>
        /// オブジェクトの無効化や終局門禁のために、フェーズを問わず必ず終了状態へ遷移させます。
        /// 既に終局済みの場合は何もしません（冪等）。
        /// </summary>
        public void ForceClose()
        {
            if (phase == AttackSequencePhase.Completed || phase == AttackSequencePhase.Cancelled)
            {
                return;
            }

            Cancel();
        }

        private Result CloseWindowInternal(bool isFallback)
        {
            if (!IsWindowOpen)
            {
                return GameError.AttackWindowClosed;
            }

            if (windowTracker == null)
            {
                phase = AttackSequencePhase.Active;
                return GameError.InvalidState;
            }

            Result endResult = windowTracker.EndWindow(attackSequenceId);
            phase = AttackSequencePhase.Active;
            if (endResult.IsOk)
            {
                WindowClosed?.Invoke(attackSequenceId);
                return Result.Ok();
            }

            return endResult;
        }
    }

    /// <summary>
    /// 一回の攻撃系列が遷移するフェーズです。
    /// </summary>
    public enum AttackSequencePhase
    {
        NotStarted,
        Active,
        WindowOpen,
        Completed,
        Cancelled
    }
}
