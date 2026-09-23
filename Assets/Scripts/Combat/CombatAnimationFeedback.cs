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

        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");

        /// <summary>
        /// 通常被弾アニメーションを再生します。
        /// PlayerAnimationDriver および EnemyAnimationDriver（IHitAnimationReceiver）を優先して駆動し、未設定の場合は Animator の HitTrigger へフォールバックします。
        /// </summary>
        public void PlayNormalHit(CombatFeedbackRequest request)
        {
            var target = request.Target;
            if (target == null)
            {
                return;
            }

            var animReceiver = target.GetComponentInChildren<IHitAnimationReceiver>();
            if (animReceiver != null)
            {
                animReceiver.TriggerHit();
            }
            else
            {
                target.GetComponentInChildren<Animator>()?.SetTrigger(HitTriggerParameter);
            }
        }

        /// <summary>
        /// 致命被弾に対応します。
        /// 致命ヒット時は TriggerHit を発行せず（死亡遷移の中断を防止）、TriggerDeath の能動的な発行も行いません（Health/Lifecycle に委任）。
        /// </summary>
        public void PlayLethalHit(CombatFeedbackRequest request)
        {
        }

        /// <summary>
        /// ランタイム状態をクリアします。
        /// </summary>
        public void ClearRuntimeState()
        {
        }
    }
}
