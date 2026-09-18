using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘被弾アニメーションフィードバックモジュール。
    /// 被弾時に対象の Animator を駆動して被弾アニメーションを再生します。通常ヒットは TriggerHit を発行し、
    /// 致命ヒットは死亡ライフサイクルへ委任するため、被弾アニメーションの再生や重複死亡アニメーションのトリガーは行いません。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAnimationFeedback : MonoBehaviour, ICombatAnimationFeedback, ICombatFeedbackModule
    {
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");

        /// <summary>アニメーションフィードバック診断メッセージ通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;

        /// <summary>
        /// フィードバックモジュールの統合実行エントリ。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            if (request.HitType == CombatHitType.Lethal)
            {
                PlayLethalHit(request);
            }
            else
            {
                PlayNormalHit(request);
            }
        }

        /// <summary>
        /// 通常被弾アニメーションを再生します。
        /// PlayerAnimationDriver および EnemyAnimationDriver を優先して検索し、未設定の場合は Animator の HitTrigger へフォールバックします。
        /// </summary>
        public void PlayNormalHit(CombatFeedbackRequest request)
        {
            var target = request.Target;
            if (target == null)
            {
                ReportDiagnostic("被弾対象が null のため、通常被弾アニメーションをスキップしました。", false);
                return;
            }

            var playerDriver = target.GetComponent<PlayerAnimationDriver>() ?? target.GetComponentInParent<PlayerAnimationDriver>() ?? target.GetComponentInChildren<PlayerAnimationDriver>();
            if (playerDriver != null)
            {
                playerDriver.TriggerHit();
                return;
            }

            var enemyDriver = target.GetComponent<EnemyAnimationDriver>() ?? target.GetComponentInParent<EnemyAnimationDriver>() ?? target.GetComponentInChildren<EnemyAnimationDriver>();
            if (enemyDriver != null)
            {
                enemyDriver.TriggerHit();
                return;
            }

            var animator = target.GetComponent<Animator>() ?? target.GetComponentInParent<Animator>() ?? target.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetTrigger(HitTriggerParameter);
                return;
            }

            ReportDiagnostic($"対象「{target.CombatantId}」に PlayerAnimationDriver、EnemyAnimationDriver、または有効な Animator が見つからないため、被弾アニメーションをスキップしました。", false);
        }

        /// <summary>
        /// 致命被弾に対応します。
        /// 致命ヒット時は TriggerHit を発行せず（死亡遷移の中断を防止）、TriggerDeath の能動的な発行も行いません（Health/Lifecycle に委任）。
        /// </summary>
        public void PlayLethalHit(CombatFeedbackRequest request)
        {
            // 致命ヒット時は TriggerHit を呼ばず、TriggerDeath も越境して呼ばない
            LastDiagnostic = string.Empty;
        }

        /// <summary>
        /// ランタイム状態をクリアします。
        /// </summary>
        public void ClearRuntimeState()
        {
            LastDiagnostic = string.Empty;
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[アニメーションフィードバック診断] {message}", this);
            }
            else
            {
                Debug.Log($"[アニメーションフィードバック診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
