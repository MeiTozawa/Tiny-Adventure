using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗音频控制器。
    /// 负责播放攻击挥刀音、命中受击音及角色死亡音效，严格保证不打断既有声音，
    /// 并区分普通/致死命中音（SFX_Hit_Lethal.mp3）与角色死亡音效（SFX_Player_Die / SFX_Enemy_Die）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class CombatAudioController : MonoBehaviour, ICombatFeedbackModule
    {
        public const float MinimumWhooshInterval = 0.20f;

        [Header("配置与引用")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        [SerializeField]
        private AudioSource audioSource;

        private IAudioPlaybackAdapter playbackAdapter;
        private ICombatFeedbackProfileProvider profileProvider;
        private readonly Dictionary<CombatantMarker, double> lastWhooshTimes = new Dictionary<CombatantMarker, double>();
        private double lastGenericWhooshTime = -1d;

        /// <summary>音频诊断通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        /// <summary>
        /// 测试用配置注入。
        /// </summary>
        public void ConfigureForTests(IAudioPlaybackAdapter adapter, ICombatFeedbackProfileProvider profile = null)
        {
            playbackAdapter = adapter;
            if (profile != null)
            {
                profileProvider = profile;
            }
        }

        /// <summary>
        /// 统一命中反馈入口：播放命中与受击音效。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("未配置 CombatFeedbackProfile，跳过命中音频播放。", false);
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

            // 1. 播放武器命中音效（普通/致死）
            if (variant.hitClip != null)
            {
                playbackAdapter.PlayOneShot(variant.hitClip, request.HitPoint, variant.volume, pitch, true);
            }
            else
            {
                ReportDiagnostic($"缺少「{request.HitType}」命中音效 Clip。", false);
            }

            // 2. 播放角色受击音效（玩家受击 / 敌人受击）
            AudioClip hurtClip = request.IsPlayerTarget ? profile.PlayerHurtClip : profile.EnemyHurtClip;
            if (hurtClip != null)
            {
                playbackAdapter.PlayOneShot(hurtClip, request.HitPoint, variant.volume, pitch, true);
            }
        }

        /// <summary>
        /// 角色死亡音频播放入口，由 CombatDeathAudioRouter 独立触发。
        /// 致死命中音 SFX_Hit_Lethal 与角色死亡音 SFX_Player_Die / SFX_Enemy_Die 职责分离。
        /// </summary>
        public void PlayDeath(DeathAudioRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("未配置 CombatFeedbackProfile，跳过死亡音频播放。", false);
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
                ReportDiagnostic($"缺少角色死亡音效 Clip（是否玩家：{request.IsPlayer}）。", false);
            }
        }

        /// <summary>
        /// 攻击挥刀音播放入口，允许空挥触发。
        /// </summary>
        public void PlayWhoosh(AttackFeedbackContext context)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("未配置 CombatFeedbackProfile，跳过挥刀音频播放。", false);
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
                ReportDiagnostic("缺少挥刀音效 Clip（SFX_Sword_Whoosh..mp3）。", false);
            }
        }

        /// <summary>
        /// 清理运行时状态。
        /// </summary>
        public void ClearRuntimeState()
        {
            lastWhooshTimes.Clear();
            lastGenericWhooshTime = -1d;
            LastDiagnostic = string.Empty;
        }

        private void ResolveReferences()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (playbackAdapter == null)
            {
                playbackAdapter = new UnityAudioPlaybackAdapter(audioSource);
            }
        }

        private void EnsureAdapter()
        {
            if (playbackAdapter == null)
            {
                ResolveReferences();
            }
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[音频诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[音频诊断] {message}", this);
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
