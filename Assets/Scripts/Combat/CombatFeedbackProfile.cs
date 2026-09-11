using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 冲量相机震动设置。
    /// </summary>
    [Serializable]
    public struct ImpulseFeedbackSettings
    {
        [Tooltip("冲量振幅强度")]
        [Min(0f)]
        public float amplitude;

        [Tooltip("冲量频率")]
        [Min(0f)]
        public float frequency;

        [Tooltip("震动持续时间（秒）")]
        [Min(0.01f)]
        public float durationSeconds;

        [Tooltip("监听衰减半径")]
        [Min(0.1f)]
        public float listenerRadius;

        public static ImpulseFeedbackSettings DefaultNormal => new ImpulseFeedbackSettings
        {
            amplitude = 0.5f,
            frequency = 1f,
            durationSeconds = 0.15f,
            listenerRadius = 50f
        };

        public static ImpulseFeedbackSettings DefaultLethal => new ImpulseFeedbackSettings
        {
            amplitude = 1.2f,
            frequency = 1.5f,
            durationSeconds = 0.3f,
            listenerRadius = 50f
        };

        public static ImpulseFeedbackSettings DefaultPlayerHurt => new ImpulseFeedbackSettings
        {
            amplitude = 1.0f,
            frequency = 1.2f,
            durationSeconds = 0.25f,
            listenerRadius = 50f
        };
    }

    /// <summary>
    /// 命中反馈变体参数（普通命中与致死命中分别配置）。
    /// </summary>
    [Serializable]
    public struct HitFeedbackVariant
    {
        [Tooltip("命中特效 Prefab（可使用 Placeholder，支持后续无代码替换）")]
        public GameObject impactPrefab;

        [Tooltip("命中音效 Clip")]
        public AudioClip hitClip;

        [Tooltip("特效自动销毁生命周期（秒）")]
        [Min(0.05f)]
        public float lifetimeSeconds;

        [Tooltip("生成位置相对于 HitPoint 的局部偏移")]
        public Vector3 positionOffset;

        [Tooltip("生成旋转相对于命中方向的局部欧拉角偏移")]
        public Vector3 rotationOffset;

        [Tooltip("生成局部缩放")]
        public Vector3 spawnScale;

        [Tooltip("音效音量")]
        [Range(0f, 1f)]
        public float volume;

        [Tooltip("音高随机范围 (min, max)")]
        public Vector2 pitchRange;

        [Tooltip("相机冲量震动参数")]
        public ImpulseFeedbackSettings impulse;

        public static HitFeedbackVariant DefaultNormal => new HitFeedbackVariant
        {
            lifetimeSeconds = 1.0f,
            spawnScale = Vector3.one,
            volume = 0.8f,
            pitchRange = new Vector2(0.95f, 1.05f),
            impulse = ImpulseFeedbackSettings.DefaultNormal
        };

        public static HitFeedbackVariant DefaultLethal => new HitFeedbackVariant
        {
            lifetimeSeconds = 1.5f,
            spawnScale = Vector3.one * 1.5f,
            volume = 1.0f,
            pitchRange = new Vector2(0.9f, 1.0f),
            impulse = ImpulseFeedbackSettings.DefaultLethal
        };
    }

    /// <summary>
    /// Hit Stop 顿挫参数配置。
    /// </summary>
    [Serializable]
    public struct HitStopSettings
    {
        [Tooltip("普通命中停顿时长（秒，默认0.04）")]
        [Min(0.01f)]
        public float normalSeconds;

        [Tooltip("致死命中停顿时长（秒，默认0.08）")]
        [Min(0.01f)]
        public float lethalSeconds;

        [Tooltip("最大停顿时长上限（秒，默认0.12）")]
        [Min(0.02f)]
        public float maximumSeconds;

        [Tooltip("恢复时的缓冲安全时间")]
        [Min(0f)]
        public float recoveryBufferSeconds;

        [Tooltip("是否允许终局致死一击触发 Hit Stop")]
        public bool allowTerminalHit;

        public static HitStopSettings Default => new HitStopSettings
        {
            normalSeconds = 0.04f,
            lethalSeconds = 0.08f,
            maximumSeconds = 0.12f,
            recoveryBufferSeconds = 0.01f,
            allowTerminalHit = true
        };
    }

    /// <summary>
    /// 相机镜头反馈配置（Cinemachine Impulse 与 FOV Punch）。
    /// </summary>
    [Serializable]
    public struct CameraFeedbackSettings
    {
        public ImpulseFeedbackSettings normalHitImpulse;
        public ImpulseFeedbackSettings lethalHitImpulse;
        public ImpulseFeedbackSettings playerHurtImpulse;

        [Tooltip("普通命中 FOV 偏移（度数，负值为拉近放大）")]
        public float normalHitFovOffset;

        [Tooltip("致死命中 FOV 偏移（度数）")]
        public float lethalHitFovOffset;

        [Tooltip("玩家受击 FOV 偏移（度数）")]
        public float playerHurtFovOffset;

        [Tooltip("FOV 冲击进入平滑时间（秒）")]
        [Min(0.001f)]
        public float enterSeconds;

        [Tooltip("FOV 冲击恢复原值时间（秒）")]
        [Min(0.001f)]
        public float recoverSeconds;

        public static CameraFeedbackSettings Default => new CameraFeedbackSettings
        {
            normalHitImpulse = ImpulseFeedbackSettings.DefaultNormal,
            lethalHitImpulse = ImpulseFeedbackSettings.DefaultLethal,
            playerHurtImpulse = ImpulseFeedbackSettings.DefaultPlayerHurt,
            normalHitFovOffset = -1.5f,
            lethalHitFovOffset = -3.0f,
            playerHurtFovOffset = 2.0f,
            enterSeconds = 0.03f,
            recoverSeconds = 0.15f
        };
    }

    /// <summary>
    /// 攻击动作反馈配置（挥刀音与刀光）。
    /// </summary>
    [Serializable]
    public struct AttackFeedbackSettings
    {
        [Tooltip("挥刀音播放延迟（秒）")]
        [Min(0f)]
        public float whooshDelaySeconds;

        [Tooltip("是否在攻击有效窗口开启时播放挥刀音")]
        public bool playWhooshAtWindowOpen;

        [Tooltip("是否启用武器刀光")]
        public bool enableSwordTrail;

        [Tooltip("刀光开启的动画归一化时间")]
        [Range(0f, 1f)]
        public float trailStartNormalizedTime;

        [Tooltip("刀光关闭的动画归一化时间")]
        [Range(0f, 1f)]
        public float trailEndNormalizedTime;

        public static AttackFeedbackSettings Default => new AttackFeedbackSettings
        {
            whooshDelaySeconds = 0f,
            playWhooshAtWindowOpen = true,
            enableSwordTrail = true,
            trailStartNormalizedTime = 0.1f,
            trailEndNormalizedTime = 0.6f
        };
    }

    /// <summary>
    /// 反馈配置提供者接口。
    /// </summary>
    public interface ICombatFeedbackProfileProvider
    {
        HitFeedbackVariant NormalHit { get; }
        HitFeedbackVariant LethalHit { get; }
        AudioClip EnemyDeathClip { get; }
        AudioClip PlayerDeathClip { get; }
        HitStopSettings HitStop { get; }
        CameraFeedbackSettings Camera { get; }
        AttackFeedbackSettings Attack { get; }
        bool AllowTerminalHitFeedback { get; }
    }

    /// <summary>
    /// 战斗打击感反馈配置资产（ScriptableObject）。
    /// </summary>
    [CreateAssetMenu(fileName = "CombatFeedbackProfile", menuName = "Tiny Adventure/Combat/Feedback Profile")]
    public sealed class CombatFeedbackProfile : ScriptableObject, ICombatFeedbackProfileProvider
    {
        [Header("命中表现配置")]
        [SerializeField]
        private HitFeedbackVariant normalHit = HitFeedbackVariant.DefaultNormal;

        [SerializeField]
        private HitFeedbackVariant lethalHit = HitFeedbackVariant.DefaultLethal;

        [Header("角色死亡音效（与致死命中音独立）")]
        [Tooltip("敌人死亡音效（SFX_Enemy_Die.mp3）")]
        [SerializeField]
        private AudioClip enemyDeathClip;

        [Tooltip("玩家死亡音效（SFX_Player_Die.mp3）")]
        [SerializeField]
        private AudioClip playerDeathClip;

        [Header("受击音效")]
        [Tooltip("玩家受击音效（SFX_Player_Hurt.mp3）")]
        [SerializeField]
        private AudioClip playerHurtClip;

        [Tooltip("敌人受击音效（SFX_Enemy_Hurt.mp3）")]
        [SerializeField]
        private AudioClip enemyHurtClip;

        [Header("挥刀音效")]
        [Tooltip("挥刀音效（SFX_Sword_Whoosh..mp3，注意实际文件名包含双句点）")]
        [SerializeField]
        private AudioClip swordWhooshClip;

        [Header("顿挫与镜头")]
        [SerializeField]
        private HitStopSettings hitStop = HitStopSettings.Default;

        [SerializeField]
        private CameraFeedbackSettings cameraSettings = CameraFeedbackSettings.Default;

        [Header("攻击动作反馈")]
        [SerializeField]
        private AttackFeedbackSettings attack = AttackFeedbackSettings.Default;

        [Header("终局策略")]
        [Tooltip("是否允许终局状态下的致死一击产生完整反馈")]
        [SerializeField]
        private bool allowTerminalHitFeedback = true;

        public HitFeedbackVariant NormalHit => normalHit;
        public HitFeedbackVariant LethalHit => lethalHit;
        public AudioClip EnemyDeathClip => enemyDeathClip;
        public AudioClip PlayerDeathClip => playerDeathClip;
        public AudioClip PlayerHurtClip => playerHurtClip;
        public AudioClip EnemyHurtClip => enemyHurtClip;
        public AudioClip SwordWhooshClip => swordWhooshClip;
        public HitStopSettings HitStop => hitStop;
        public CameraFeedbackSettings Camera => cameraSettings;
        public AttackFeedbackSettings Attack => attack;
        public bool AllowTerminalHitFeedback => allowTerminalHitFeedback;

        private void OnValidate()
        {
            ClampValues();
        }

        /// <summary>
        /// 限制所有配置参数在合法安全范围内。
        /// </summary>
        public void ClampValues()
        {
            ClampVariant(ref normalHit, true);
            ClampVariant(ref lethalHit, false);

            hitStop.maximumSeconds = Mathf.Clamp(hitStop.maximumSeconds, 0.02f, 0.5f);
            hitStop.normalSeconds = Mathf.Clamp(hitStop.normalSeconds, 0.01f, hitStop.maximumSeconds);
            hitStop.lethalSeconds = Mathf.Clamp(hitStop.lethalSeconds, 0.01f, hitStop.maximumSeconds);
            hitStop.recoveryBufferSeconds = Mathf.Max(0f, hitStop.recoveryBufferSeconds);

            cameraSettings.enterSeconds = Mathf.Max(0.001f, cameraSettings.enterSeconds);
            cameraSettings.recoverSeconds = Mathf.Max(0.001f, cameraSettings.recoverSeconds);

            attack.whooshDelaySeconds = Mathf.Max(0f, attack.whooshDelaySeconds);
            attack.trailStartNormalizedTime = Mathf.Clamp01(attack.trailStartNormalizedTime);
            attack.trailEndNormalizedTime = Mathf.Clamp(attack.trailEndNormalizedTime, attack.trailStartNormalizedTime, 1f);
        }

        private static void ClampVariant(ref HitFeedbackVariant variant, bool isNormal)
        {
            variant.lifetimeSeconds = Mathf.Max(0.05f, variant.lifetimeSeconds);
            if (variant.spawnScale.x <= 0f || variant.spawnScale.y <= 0f || variant.spawnScale.z <= 0f)
            {
                variant.spawnScale = isNormal ? Vector3.one : Vector3.one * 1.5f;
            }

            variant.volume = Mathf.Clamp01(variant.volume);
            if (variant.pitchRange.x < 0.1f) variant.pitchRange.x = 0.1f;
            if (variant.pitchRange.y < variant.pitchRange.x) variant.pitchRange.y = variant.pitchRange.x;
            if (variant.pitchRange.y > 3f) variant.pitchRange.y = 3f;

            variant.impulse.amplitude = Mathf.Max(0f, variant.impulse.amplitude);
            variant.impulse.frequency = Mathf.Max(0f, variant.impulse.frequency);
            variant.impulse.durationSeconds = Mathf.Max(0.01f, variant.impulse.durationSeconds);
            variant.impulse.listenerRadius = Mathf.Max(0.1f, variant.impulse.listenerRadius);
        }

        /// <summary>
        /// 校验配置完整性并输出中文诊断列表。
        /// </summary>
        public bool ValidateConfiguration(out List<string> diagnostics)
        {
            diagnostics = new List<string>();

            if (normalHit.impactPrefab == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 normalHit.impactPrefab 未设置，普通命中将无法生成特效。建议分配 Impact_Normal.prefab。");
            }

            if (lethalHit.impactPrefab == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 lethalHit.impactPrefab 未设置，致死命中将无法生成特效。建议分配 Impact_Lethal.prefab。");
            }

            if (normalHit.hitClip == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 normalHit.hitClip 未设置，建议分配 SFX_Hit_Normal.mp3。");
            }

            if (lethalHit.hitClip == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 lethalHit.hitClip 未设置，建议分配 SFX_Hit_Lethal.mp3。");
            }

            if (enemyDeathClip == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 enemyDeathClip 未设置，建议分配 SFX_Enemy_Die.mp3。");
            }

            if (playerDeathClip == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 playerDeathClip 未设置，建议分配 SFX_Player_Die.mp3。");
            }

            if (swordWhooshClip == null)
            {
                diagnostics.Add("[配置诊断] CombatFeedbackProfile 的 swordWhooshClip 未设置，建议分配 SFX_Sword_Whoosh..mp3。");
            }

            return diagnostics.Count == 0;
        }

        /// <summary>
        /// 测试注入配置入口。
        /// </summary>
        public void ConfigureForTests(
            HitFeedbackVariant newNormalHit,
            HitFeedbackVariant newLethalHit,
            AudioClip newEnemyDeathClip,
            AudioClip newPlayerDeathClip,
            HitStopSettings newHitStop,
            CameraFeedbackSettings newCamera,
            AttackFeedbackSettings newAttack,
            bool newAllowTerminal = true)
        {
            normalHit = newNormalHit;
            lethalHit = newLethalHit;
            enemyDeathClip = newEnemyDeathClip;
            playerDeathClip = newPlayerDeathClip;
            hitStop = newHitStop;
            cameraSettings = newCamera;
            attack = newAttack;
            allowTerminalHitFeedback = newAllowTerminal;
            ClampValues();
        }
    }
}
