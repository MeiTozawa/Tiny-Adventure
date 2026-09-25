using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾瞬態閃光（Hit Flash）を駆動する純 C# フィードバックハンドラー。
    /// 対象エンティティの IHitFlashReceiver を検索して TriggerFlash を呼び出します。
    /// </summary>
    public sealed class HitFlashFeedbackHandler : ICombatFeedbackModule
    {
        public void Play(CombatFeedbackRequest request)
        {
            var flashReceiver = request.Target.FlashReceiver;
            if (flashReceiver != null)
            {
                flashReceiver.TriggerFlash(request.HitType);
            }
        }

        public void ClearRuntimeState()
        {
        }
    }
}
