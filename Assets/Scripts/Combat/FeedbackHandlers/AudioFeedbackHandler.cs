using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘オーディオ（ヒット音、被弾音、撃破音、剣撃風切り音）を再生する純 C# フィードバックハンドラー。
    /// MonoBehaviour に依存せず、AudioSource と CombatFeedbackProfile を用いてオーディオをディスパッチします。
    /// </summary>
    public sealed class AudioFeedbackHandler : ICombatFeedbackModule
    {
        private readonly AudioSource audioSource;
        private readonly CombatFeedbackProfile profile;

        public AudioFeedbackHandler(AudioSource audioSource, CombatFeedbackProfile profile)
        {
            this.audioSource = audioSource;
            this.profile = profile;
        }

        public void Play(CombatFeedbackRequest request)
        {
            if (audioSource == null || profile == null) return;

            HitFeedbackVariant variant = request.HitType == CombatHitType.Lethal
                ? profile.LethalHit
                : profile.NormalHit;

            float pitch = 1f;
            if (variant.pitchRange.x > 0f && variant.pitchRange.y >= variant.pitchRange.x)
            {
                pitch = Random.Range(variant.pitchRange.x, variant.pitchRange.y);
            }

            // 1. 武器ヒットSE（通常 / 致命）
            if (variant.hitClip != null)
            {
                audioSource.pitch = pitch;
                audioSource.PlayOneShot(variant.hitClip, Mathf.Clamp01(variant.volume));
            }

            // 2. キャラクター被弾SE
            AudioClip hurtClip = request.IsPlayerTarget ? profile.PlayerHurtClip : profile.EnemyHurtClip;
            if (hurtClip != null)
            {
                audioSource.pitch = pitch;
                audioSource.PlayOneShot(hurtClip, Mathf.Clamp01(variant.volume));
            }

            // 3. 致命ヒット時の死亡SE
            if (request.HitType == CombatHitType.Lethal)
            {
                AudioClip deathClip = request.IsPlayerTarget ? profile.PlayerDeathClip : profile.EnemyDeathClip;
                if (deathClip != null)
                {
                    audioSource.pitch = 1f;
                    audioSource.PlayOneShot(deathClip, 1f);
                }
            }
        }

        /// <summary>
        /// 剣撃風切り音（Whoosh）を再生します。
        /// </summary>
        public void PlayWhoosh()
        {
            if (audioSource == null || profile == null) return;

            if (profile.SwordWhooshClip != null)
            {
                audioSource.pitch = Random.Range(0.95f, 1.05f);
                audioSource.PlayOneShot(profile.SwordWhooshClip, 0.8f);
            }
        }

        public void ClearRuntimeState()
        {
        }
    }
}
