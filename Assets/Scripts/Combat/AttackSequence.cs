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
        /// <summary>デフォルトのフォールバック閉鎖時刻です。Attack clip完了前に強制的にウィンドウを閉じます。</summary>
        public const float DefaultFallbackCloseNormalizedTime = 0.9f;

        private readonly AttackWindowTracker windowTracker;
        private readonly float fallbackCloseNormalizedTime;

        private int attackSequenceId;
        private AttackSequencePhase phase = AttackSequencePhase.NotStarted;
        private bool fallbackWindowCloseUsed;

        public AttackSequence(AttackWindowTracker windowTracker, float fallbackCloseNormalizedTime = DefaultFallbackCloseNormalizedTime)
        {
            this.windowTracker = windowTracker;
            this.fallbackCloseNormalizedTime = Mathf.Clamp01(fallbackCloseNormalizedTime);
        }

        /// <summary>この攻撃系列を一意に識別するIDです。開始されていない場合は0です。</summary>
        public int AttackSequenceId => attackSequenceId;

        /// <summary>現在のフェーズです。</summary>
        public AttackSequencePhase Phase => phase;

        /// <summary>攻撃有効ウィンドウが現在開いているかを返します。</summary>
        public bool IsWindowOpen => phase == AttackSequencePhase.WindowOpen;

        /// <summary>攻撃継続中（未終局）かを返します。</summary>
        public bool IsActive => phase == AttackSequencePhase.Active || phase == AttackSequencePhase.WindowOpen;

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
        public bool StartSequence(int sequenceId, out string diagnostic)
        {
            if (!DamageRequest.IsValidAttackSequenceId(sequenceId))
            {
                diagnostic = "攻撃系列IDが無効なため、攻撃系列を開始できません。";
                return false;
            }

            if (IsActive)
            {
                diagnostic = $"攻撃系列{attackSequenceId}が進行中のため、新しい攻撃系列を開始できません。";
                return false;
            }

            attackSequenceId = sequenceId;
            phase = AttackSequencePhase.Active;
            fallbackWindowCloseUsed = false;
            diagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// Animator eventから呼び出す攻撃有効ウィンドウの開始点です。
        /// 重複したeventや不正なタイミングでの呼び出しは無視します。
        /// </summary>
        public bool OnAttackWindowOpenEvent(out string diagnostic)
        {
            if (phase != AttackSequencePhase.Active)
            {
                diagnostic = $"フェーズ{phase}では攻撃有効ウィンドウを開けません。";
                return false;
            }

            if (windowTracker == null)
            {
                diagnostic = "AttackWindowTrackerが設定されていないため、攻撃有効ウィンドウを開けません。";
                return false;
            }

            if (!windowTracker.BeginWindow(attackSequenceId, out diagnostic))
            {
                return false;
            }

            phase = AttackSequencePhase.WindowOpen;
            WindowOpened?.Invoke(attackSequenceId);
            return true;
        }

        /// <summary>
        /// Animator eventから呼び出す攻撃有効ウィンドウの終了点です。
        /// 重複したeventや、ウィンドウが既に閉じている場合は無視します（冪等）。
        /// </summary>
        public bool OnAttackWindowCloseEvent()
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
            if (!IsWindowOpen)
            {
                return;
            }

            if (normalizedTime < fallbackCloseNormalizedTime)
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
        public bool Complete(out string diagnostic)
        {
            if (phase == AttackSequencePhase.Completed || phase == AttackSequencePhase.Cancelled)
            {
                diagnostic = string.Empty;
                return false;
            }

            if (IsWindowOpen)
            {
                CloseWindowInternal(isFallback: true);
            }

            if (phase != AttackSequencePhase.Active)
            {
                diagnostic = $"フェーズ{phase}では攻撃系列を完了できません。";
                return false;
            }

            phase = AttackSequencePhase.Completed;
            diagnostic = string.Empty;
            SequenceCompleted?.Invoke(attackSequenceId);
            return true;
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

        private bool CloseWindowInternal(bool isFallback)
        {
            if (!IsWindowOpen)
            {
                return false;
            }

            if (windowTracker == null)
            {
                phase = AttackSequencePhase.Active;
                return false;
            }

            bool closed = windowTracker.EndWindow(attackSequenceId);
            phase = AttackSequencePhase.Active;
            if (closed)
            {
                WindowClosed?.Invoke(attackSequenceId);
            }

            return closed;
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
