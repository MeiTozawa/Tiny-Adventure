using System;
using UnityEngine;

namespace TinyAdventure
{
    [Serializable]
    public struct ImpulseFeedbackSettings
    {
        [Tooltip("インパルスの振幅強度")]
        [Min(0f)]
        public float amplitude;

        [Tooltip("インパルスの周波数")]
        [Min(0f)]
        public float frequency;

        [Tooltip("振動持続時間（秒）")]
        [Min(0.01f)]
        public float durationSeconds;

        [Tooltip("リスナー減衰半径")]
        [Min(0.1f)]
        public float listenerRadius;

        public static ImpulseFeedbackSettings DefaultNormal => new()
        {
            amplitude = 0.5f,
            frequency = 1f,
            durationSeconds = 0.15f,
            listenerRadius = 50f
        };

        public static ImpulseFeedbackSettings DefaultLethal => new()
        {
            amplitude = 1.2f,
            frequency = 1.5f,
            durationSeconds = 0.3f,
            listenerRadius = 50f
        };

        public static ImpulseFeedbackSettings DefaultPlayerHurt => new()
        {
            amplitude = 0.35f,
            frequency = 1.2f,
            durationSeconds = 0.15f,
            listenerRadius = 50f
        };
    }

    [Serializable]
    public struct HitFeedbackVariant
    {
        [Tooltip("ヒットエフェクトPrefab")]
        public GameObject impactPrefab;

        [Tooltip("ヒット音Clip")]
        public AudioClip hitClip;

        [Tooltip("エフェクト自動破棄秒数")]
        [Min(0.05f)]
        public float lifetimeSeconds;

        [Tooltip("生成位置オフセット")]
        public Vector3 positionOffset;

        [Tooltip("生成回転オフセット")]
        public Vector3 rotationOffset;

        [Tooltip("生成スケール")]
        public Vector3 spawnScale;

        [Tooltip("音量")]
        [Range(0f, 1f)]
        public float volume;

        [Tooltip("ピッチ範囲 (min, max)")]
        public Vector2 pitchRange;

        [Tooltip("カメラインパルス設定")]
        public ImpulseFeedbackSettings impulse;

        public static HitFeedbackVariant DefaultNormal => new()
        {
            lifetimeSeconds = 1.0f,
            spawnScale = Vector3.one,
            volume = 0.8f,
            pitchRange = new Vector2(0.95f, 1.05f),
            impulse = ImpulseFeedbackSettings.DefaultNormal
        };

        public static HitFeedbackVariant DefaultLethal => new()
        {
            lifetimeSeconds = 1.5f,
            spawnScale = Vector3.one * 1.5f,
            volume = 1.0f,
            pitchRange = new Vector2(0.9f, 1.0f),
            impulse = ImpulseFeedbackSettings.DefaultLethal
        };
    }

    [Serializable]
    public struct HitStopSettings
    {
        [Tooltip("通常ヒット停止時間（秒）")]
        [Min(0.01f)]
        public float normalSeconds;

        [Tooltip("撃破ヒット停止時間（秒）")]
        [Min(0.01f)]
        public float lethalSeconds;

        [Tooltip("最大停止時間上限（秒）")]
        [Min(0.02f)]
        public float maximumSeconds;

        [Tooltip("復帰バッファ時間（秒）")]
        [Min(0f)]
        public float recoveryBufferSeconds;

        [Tooltip("終局撃破ヒットでHitStopを発動するか")]
        public bool allowTerminalHit;

        public static HitStopSettings Default => new()
        {
            normalSeconds = 0.04f,
            lethalSeconds = 0.08f,
            maximumSeconds = 0.12f,
            recoveryBufferSeconds = 0.01f,
            allowTerminalHit = true
        };
    }

    [Serializable]
    public struct CameraFeedbackSettings
    {
        public ImpulseFeedbackSettings normalHitImpulse;
        public ImpulseFeedbackSettings lethalHitImpulse;
        public ImpulseFeedbackSettings playerHurtImpulse;

        [Tooltip("通常ヒットFOVオフセット（度数、負値でズームイン）")]
        public float normalHitFovOffset;

        [Tooltip("撃破ヒットFOVオフセット（度数）")]
        public float lethalHitFovOffset;

        [Tooltip("プレイヤー被弾FOVオフセット（度数）")]
        public float playerHurtFovOffset;

        [Tooltip("FOV衝撃遷移時間（秒）")]
        [Min(0.001f)]
        public float enterSeconds;

        [Tooltip("FOV衝撃復帰時間（秒）")]
        [Min(0.001f)]
        public float recoverSeconds;

        public static CameraFeedbackSettings Default => new()
        {
            normalHitImpulse = ImpulseFeedbackSettings.DefaultNormal,
            lethalHitImpulse = ImpulseFeedbackSettings.DefaultLethal,
            playerHurtImpulse = ImpulseFeedbackSettings.DefaultPlayerHurt,
            normalHitFovOffset = -1.5f,
            lethalHitFovOffset = -3.0f,
            playerHurtFovOffset = 0.6f,
            enterSeconds = 0.03f,
            recoverSeconds = 0.15f
        };
    }

    [Serializable]
    public struct AttackFeedbackSettings
    {
        [Tooltip("空振り音再生遅延（秒）")]
        [Min(0f)]
        public float whooshDelaySeconds;

        [Tooltip("攻撃有効ウィンドウ開始時に空振り音を再生するか")]
        public bool playWhooshAtWindowOpen;

        [Tooltip("武器トレイルを有効にするか")]
        public bool enableSwordTrail;

        [Tooltip("トレイル開始の正規化時間")]
        [Range(0f, 1f)]
        public float trailStartNormalizedTime;

        [Tooltip("トレイル終了の正規化時間")]
        [Range(0f, 1f)]
        public float trailEndNormalizedTime;

        [Tooltip("空振り音量")]
        [Range(0f, 1f)]
        public float whooshVolume;

        [Tooltip("空振り音ピッチ範囲")]
        public Vector2 whooshPitchRange;

        public static AttackFeedbackSettings Default => new()
        {
            whooshDelaySeconds = 0f,
            playWhooshAtWindowOpen = true,
            enableSwordTrail = true,
            trailStartNormalizedTime = 0.1f,
            trailEndNormalizedTime = 0.6f,
            whooshVolume = 0.8f,
            whooshPitchRange = new Vector2(0.95f, 1.05f)
        };
    }

    public interface ICombatFeedbackProfileProvider
    {
        HitFeedbackVariant NormalHit { get; }
        HitFeedbackVariant LethalHit { get; }
        AudioClip EnemyDeathClip { get; }
        AudioClip PlayerDeathClip { get; }
        AudioClip PlayerHurtClip { get; }
        AudioClip EnemyHurtClip { get; }
        AudioClip SwordWhooshClip { get; }
        HitStopSettings HitStop { get; }
        CameraFeedbackSettings Camera { get; }
        AttackFeedbackSettings Attack { get; }
        bool AllowTerminalHitFeedback { get; }
    }

    /// <summary>
    /// 戦闘打撃感フィードバック設定アセット。
    /// </summary>
    [CreateAssetMenu(fileName = "CombatFeedbackProfile", menuName = "Tiny Adventure/Combat/Feedback Profile")]
    public sealed class CombatFeedbackProfile : ScriptableObject, ICombatFeedbackProfileProvider
    {
        [Header("ヒット演出設定")]
        [SerializeField] private HitFeedbackVariant normalHit;
        [SerializeField] private HitFeedbackVariant lethalHit;

        [Header("キャラクター死亡SE")]
        [Tooltip("敵死亡SE（SFX_Enemy_Die.mp3）")]
        [SerializeField] private AudioClip enemyDeathClip;

        [Tooltip("プレイヤー死亡SE（SFX_Player_Die.mp3）")]
        [SerializeField] private AudioClip playerDeathClip;

        [Header("被ダメージSE")]
        [Tooltip("プレイヤー被ダメージSE（SFX_Player_Hurt.mp3）")]
        [SerializeField] private AudioClip playerHurtClip;

        [Tooltip("敵被ダメージSE（SFX_Enemy_Hurt.mp3）")]
        [SerializeField] private AudioClip enemyHurtClip;

        [Header("剣撃SE")]
        [Tooltip("剣撃SE")]
        [SerializeField] private AudioClip swordWhooshClip;

        [Header("ヒットストップ・カメラ演出")]
        [SerializeField] private HitStopSettings hitStop;
        [SerializeField] private CameraFeedbackSettings cameraSettings;

        [Header("攻撃フィードバック")]
        [SerializeField] private AttackFeedbackSettings attack;

        [Header("終了ポリシー")]
        [Tooltip("終了状態での致命ヒットで完全なフィードバックを発生させるかどうか")]
        [SerializeField] private bool allowTerminalHitFeedback = true;

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

        public void ClampValues()
        {
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
    }
}
