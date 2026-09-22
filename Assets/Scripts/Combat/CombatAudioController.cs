using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘オーディオコントローラー。
    /// 剣撃音、ヒット/被弾音、およびキャラクター死亡音を再生します。再生中音声を中断せず、
    /// 通常/致命ヒット音（SFX_Hit_Lethal.mp3）とキャラクター死亡音（SFX_Player_Die / SFX_Enemy_Die）を厳密に分離して扱います。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class CombatAudioController : MonoBehaviour, ICombatFeedbackModule
    {
        public const float MinimumWhooshInterval = 0.20f;

        [Header("設定・参照")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        [SerializeField]
        private AudioSource audioSource;

        private IAudioPlaybackAdapter playbackAdapter;
        private ICombatFeedbackProfileProvider profileProvider;
        private readonly Dictionary<CombatantMarker, double> lastWhooshTimes = new Dictionary<CombatantMarker, double>();
        private double lastGenericWhooshTime = -1d;

        /// <summary>オーディオ診断メッセージ通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;

        private void Awake()
        {
            EnsureAdapter();
        }

        private void OnEnable()
        {
            EnsureAdapter();
        }

        /// <summary>
        /// 依存関係を設定します。
        /// </summary>
        internal void SetDependencies(IAudioPlaybackAdapter adapter, ICombatFeedbackProfileProvider profile = null)
        {
            playbackAdapter = adapter;
            if (profile != null)
            {
                profileProvider = profile;
            }
        }

        /// <summary>
        /// ヒットフィードバック統合エントリ: ヒット音および被弾音を再生します。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("CombatFeedbackProfile が未設定のため、ヒットオーディオ再生をスキップしました。", false);
                return;
            }

            EnsureAdapter();

            HitFeedbackVariant variant = request.HitType == CombatHitType.Lethal
                ? profile.LethalHit
                : profile.NormalHit;

            float pitch = 1f;
            if (variant.pitchRange.x > 0f && variant.pitchRange.y >= variant.pitchRange.x)
            {
                pitch = UnityEngine.Random.Range(variant.pitchRange.x, variant.pitchRange.y);
            }

            // 1. 武器ヒットSEの再生（通常/致命）
            if (variant.hitClip != null)
            {
                playbackAdapter.PlayOneShot(variant.hitClip, request.HitPoint, variant.volume, pitch, true);
            }
            else
            {
                ReportDiagnostic($"「{request.HitType}」ヒットSE Clip が未設定です。", false);
            }

            // 2. キャラクター被弾SEの再生（プレイヤー被弾 / 敵被弾）
            AudioClip hurtClip = request.IsPlayerTarget ? profile.PlayerHurtClip : profile.EnemyHurtClip;
            if (hurtClip != null)
            {
                playbackAdapter.PlayOneShot(hurtClip, request.HitPoint, variant.volume, pitch, true);
            }
        }

        /// <summary>
        /// キャラクター死亡オーディオ再生エントリ。CombatDeathAudioRouter から独立してトリガーされます。
        /// 致命ヒットSE SFX_Hit_Lethal とキャラクター死亡SE SFX_Player_Die / SFX_Enemy_Die の責務は分離されています。
        /// </summary>
        public void PlayDeath(DeathAudioRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("CombatFeedbackProfile が未設定のため、死亡オーディオ再生をスキップしました。", false);
                return;
            }

            EnsureAdapter();

            AudioClip deathClip = request.IsPlayer ? profile.PlayerDeathClip : profile.EnemyDeathClip;
            if (deathClip != null)
            {
                playbackAdapter.PlayOneShot(deathClip, request.WorldPosition, 1f, 1f, true);
            }
            else
            {
                ReportDiagnostic($"キャラクター死亡SE Clip が未設定です（プレイヤー: {request.IsPlayer}）。", false);
            }
        }

        /// <summary>
        /// 剣撃音再生エントリ。空振り時にもトリガー可能です。
        /// </summary>
        public void PlayWhoosh(AttackFeedbackContext context)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("CombatFeedbackProfile が未設定のため、剣撃音再生をスキップしました。", false);
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (context.Attacker != null)
            {
                if (lastWhooshTimes.TryGetValue(context.Attacker, out double lastTime) && (now - lastTime) < MinimumWhooshInterval)
                {
                    return;
                }

                lastWhooshTimes[context.Attacker] = now;
            }
            else
            {
                if (lastGenericWhooshTime >= 0d && (now - lastGenericWhooshTime) < MinimumWhooshInterval)
                {
                    return;
                }

                lastGenericWhooshTime = now;
            }

            EnsureAdapter();

            AudioClip whooshClip = profile.SwordWhooshClip;
            if (whooshClip != null)
            {
                var attackSettings = profile.Attack;
                float pitch = 1f;
                if (attackSettings.whooshPitchRange.x > 0f && attackSettings.whooshPitchRange.y >= attackSettings.whooshPitchRange.x)
                {
                    pitch = UnityEngine.Random.Range(attackSettings.whooshPitchRange.x, attackSettings.whooshPitchRange.y);
                }

                playbackAdapter.PlayOneShot(whooshClip, context.Position, attackSettings.whooshVolume, pitch, false);
            }
            else
            {
                ReportDiagnostic("剣撃SE Clip（SFX_Sword_Whoosh..mp3）が未設定です。", false);
            }
        }

        /// <summary>
        /// ランタイム状態をクリアします。
        /// </summary>
        public void ClearRuntimeState()
        {
            lastWhooshTimes.Clear();
            lastGenericWhooshTime = -1d;
            LastDiagnostic = string.Empty;
        }

        private void EnsureAdapter()
        {
            if (playbackAdapter == null)
            {
                if (audioSource == null)
                {
                    audioSource = GetComponent<AudioSource>();
                    if (audioSource == null)
                    {
                        audioSource = gameObject.AddComponent<AudioSource>();
                    }
                }

                playbackAdapter = new UnityAudioPlaybackAdapter(audioSource);
            }
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[音声診断] {message}", this);
            }
            else
            {
                Debug.Log($"[音声診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }

        private sealed class UnityAudioPlaybackAdapter : IAudioPlaybackAdapter
        {
            private readonly AudioSource source;

            public UnityAudioPlaybackAdapter(AudioSource source)
            {
                this.source = source;
            }

            public void PlayOneShot(AudioClip clip, Vector3 worldPosition, float volume, float pitch, bool spatialized)
            {
                if (clip == null || source == null) return;

                source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
                source.spatialBlend = spatialized ? 1f : 0f;
                source.transform.position = worldPosition;
                source.PlayOneShot(clip, Mathf.Clamp01(volume));
            }
        }
    }
}
