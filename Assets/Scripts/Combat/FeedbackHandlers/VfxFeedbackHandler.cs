using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 被弾ヒット VFX（火花・衝撃波）を生成する純 C# フィードバックハンドラー。
    /// </summary>
    public sealed class VfxFeedbackHandler : ICombatFeedbackModule
    {
        private readonly CombatFeedbackProfile profile;

        public VfxFeedbackHandler(CombatFeedbackProfile profile)
        {
            this.profile = profile;
        }

        public void Play(CombatFeedbackRequest request)
        {
            if (profile == null) return;

            HitFeedbackVariant variant = request.HitType == CombatHitType.Lethal
                ? profile.LethalHit
                : profile.NormalHit;

            GameObject prefab = variant.impactPrefab;
            if (prefab == null) return;

            Vector3 position = request.HitPoint;
            Quaternion rotation = request.Direction != Vector3.zero
                ? Quaternion.LookRotation(request.Direction)
                : Quaternion.identity;

            GameObject instance = Object.Instantiate(prefab, position, rotation);
            if (instance != null)
            {
                instance.transform.localScale = variant.spawnScale;
                Object.Destroy(instance, Mathf.Max(0.1f, variant.lifetimeSeconds));
            }
        }

        public void ClearRuntimeState()
        {
        }
    }
}
