using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾アニメーションを駆動する純 C# フィードバックハンドラー。
    /// MonoBehaviour に依存せず、対象の IHitAnimationReceiver または Animator を直接トリガーします。
    /// </summary>
    public sealed class AnimationFeedbackHandler : ICombatFeedbackModule
    {
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");

        public void Play(CombatFeedbackRequest request)
        {
            if (request.HitType == CombatHitType.Lethal) return;

            var target = request.Target;
            if (target == null) return;

            var animReceiver = target.AnimationReceiver;
            if (animReceiver != null)
            {
                animReceiver.TriggerHit();
            }
            else if (target.TargetAnimator != null)
            {
                target.TargetAnimator.SetTrigger(HitTriggerParameter);
            }
        }

        public void ClearRuntimeState()
        {
        }
    }
}
