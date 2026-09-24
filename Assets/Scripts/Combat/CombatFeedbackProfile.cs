using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// カメラインパルス振動設定。
    /// </summary>
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

    /// <summary>
    /// ヒットフィードバック個別パラメータ（通常・撃破設定）。
    /// </summary>
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

    /// <summary>
    /// ヒットストップ設定。
    /// </summary>
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

    /// <summary>
    /// カメラ演出設定（CinemachineインパルスおよびFOVパンチ）。
    /// </summary>
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

    /// <summary>
    /// 攻撃演出設定（空振り音およびトレイル）。
    /// </summary>
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

    /// <summary>
    /// 戦闘フィードバックプロファイル提供インターフェース。
    /// </summary>
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
        [SerializeField]
        private HitFeedbackVariant normalHit;

        [SerializeField]
        private HitFeedbackVariant lethalHit;

        [Header("キャラクター死亡SE（致命ヒットSEと独立）")]
        [Tooltip("敵死亡SE（SFX_Enemy_Die.mp3）")]
        [SerializeField]
        private AudioClip enemyDeathClip;

        [Tooltip("プレイヤー死亡SE（SFX_Player_Die.mp3）")]
        [SerializeField]
        private AudioClip playerDeathClip;

        [Header("被ダメージSE")]
        [Tooltip("プレイヤー被ダメージSE（SFX_Player_Hurt.mp3）")]
        [SerializeField]
        private AudioClip playerHurtClip;

        [Tooltip("敵被ダメージSE（SFX_Enemy_Hurt.mp3）")]
        [SerializeField]
        private AudioClip enemyHurtClip;

        [Header("剣撃SE")]
        [Tooltip("剣撃SE（SFX_Sword_Whoosh..mp3、実際のファイル名にドットが2つ含まれます）")]
        [SerializeField]
        private AudioClip swordWhooshClip;

        [Header("ヒットストップ・カメラ演出")]
        [SerializeField]
        private HitStopSettings hitStop;

        [SerializeField]
        private CameraFeedbackSettings cameraSettings;

        [Header("攻撃フィードバック")]
        [SerializeField]
        private AttackFeedbackSettings attack;

        [Header("終了ポリシー")]
        [Tooltip("終了状態での致命ヒットで完全なフィードバックを発生させるかどうか")]
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

        /// <summary>
        /// 設定パラメータを注入します。
        /// </summary>
        internal void SetConfig(
            HitFeedbackVariant normal,
            HitFeedbackVariant lethal,
            AudioClip enemyDeath = null,
            AudioClip playerDeath = null,
            AudioClip playerHurt = null,
            AudioClip enemyHurt = null,
            AudioClip whoosh = null)
        {
            normalHit = normal;
            lethalHit = lethal;
            enemyDeathClip = enemyDeath;
            playerDeathClip = playerDeath;
            playerHurtClip = playerHurt;
            enemyHurtClip = enemyHurt;
            swordWhooshClip = whoosh;
            ClampValues();
        }

        /// <summary>
        /// すべての設定パラメータを安全な値の範囲内に制限します。
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
        /// 設定の整合性を検証します。未設定アセットがある場合はエラーログを出力し、GameError.InvalidParameter を返します。
        /// </summary>
        public Result ValidateConfiguration()
        {
            bool hasError = false;

            if (normalHit.impactPrefab == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の normalHit.impactPrefab が未設定です。通常ヒットエフェクトが生成されません。Impact_Normal.prefab の割り当てを推奨します。", this);
                hasError = true;
            }

            if (lethalHit.impactPrefab == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の lethalHit.impactPrefab が未設定です。致命ヒットエフェクトが生成されません。Impact_Lethal.prefab の割り当てを推奨します。", this);
                hasError = true;
            }

            if (normalHit.hitClip == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の normalHit.hitClip が未設定です。SFX_Hit_Normal.mp3 の割り当てを推奨します。", this);
                hasError = true;
            }

            if (lethalHit.hitClip == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の lethalHit.hitClip が未設定です。SFX_Hit_Lethal.mp3 の割り当てを推奨します。", this);
                hasError = true;
            }

            if (enemyDeathClip == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の enemyDeathClip が未設定です。SFX_Enemy_Die.mp3 の割り当てを推奨します。", this);
                hasError = true;
            }

            if (playerDeathClip == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の playerDeathClip が未設定です。SFX_Player_Die.mp3 の割り当てを推奨します。", this);
                hasError = true;
            }

            if (swordWhooshClip == null)
            {
                Debug.LogError("[設定エラー] CombatFeedbackProfile の swordWhooshClip が未設定です。SFX_Sword_Whoosh..mp3 の割り当てを推奨します。", this);
                hasError = true;
            }

            return hasError ? GameError.InvalidParameter : Result.Ok();
        }

        /// <summary>
        /// 設定パラメータを注入します。
        /// </summary>
        internal void SetConfig(
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
