using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒット時の時間減速（Time Slow / ヒットストップ・肉斬り感）中央コントローラー。
    /// プレイヤーの攻撃が敵に命中した瞬間に、グローバルな Time.timeScale を一時的に低下させ、
    /// 非スケール時間の指定期間経過後に滑らかに 1.0f へ復帰させます。
    /// プレイヤー受撃時は減速を発動せず、同一攻撃系列で複数の敵が同時に被弾した場合は1回のみ減速を発動します。
    /// ゼロリーク原則（Zero Leak Guarantee）：OnDisable、OnDestroy、ClearRuntimeState で確実に Time.timeScale = 1.0f へ復元します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatTimeSlowController : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("命中時間減速スイッチ")]
        [Tooltip("打撃感向上のための命中時時間減速を有効にするかどうか。")]
        [SerializeField]
        private bool isEnabled = true;

        [Header("通常命中減速設定")]
        [Tooltip("通常命中時のグローバル時間スケール（例: 0.15 で通常速度の15%）。")]
        [Range(0.01f, 1f)]
        [SerializeField]
        private float normalTimeScale = 0.15f;

        [Tooltip("通常命中減速の継続時間（秒、非スケール実時間）。")]
        [Min(0.01f)]
        [SerializeField]
        private float normalDurationSeconds = 0.08f;

        [Header("致命・撃破命中減速設定")]
        [Tooltip("致命・撃破命中時のグローバル時間スケール（例: 0.05 で5%の映画的スローモーション）。")]
        [Range(0.01f, 1f)]
        [SerializeField]
        private float lethalTimeScale = 0.05f;

        [Tooltip("致命・撃破命中減速の継続時間（秒、非スケール実時間）。")]
        [Min(0.01f)]
        [SerializeField]
        private float lethalDurationSeconds = 0.20f;

        [Header("復帰補間")]
        [Tooltip("減速終了時の滑らかな復帰補間時間（秒、非スケール実時間）。")]
        [Min(0f)]
        [SerializeField]
        private float recoverySmoothSeconds = 0.03f;

        [Tooltip("連続多重ヒット時の最大許容累積減速時間上限（秒）。")]
        [Min(0.05f)]
        [SerializeField]
        private float maxDurationSeconds = 0.35f;

        private IUnscaledTimeSource timeSource;
        private bool isSlowActive;
        private double startedAtUnscaled;
        private double durationDeadlineUnscaled;
        private double recoveryDeadlineUnscaled;
        private float activeTargetTimeScale = 1f;
        private int lastHandledAttackSequenceId;

        /// <summary>現在時間減速が進行中であるかを示します。</summary>
        public bool IsSlowActive => isSlowActive;

        /// <summary>現在の Time.timeScale を取得します。</summary>
        public float CurrentTimeScale => Time.timeScale;

        /// <summary>通常命中時の時間スケール設定値です。</summary>
        public float NormalTimeScale => normalTimeScale;

        /// <summary>通常命中時の減速継続時間（秒）です。</summary>
        public float NormalDurationSeconds => normalDurationSeconds;

        /// <summary>致命命中時の時間スケール設定値です。</summary>
        public float LethalTimeScale => lethalTimeScale;

        /// <summary>致命命中時の減速継続時間（秒）です。</summary>
        public float LethalDurationSeconds => lethalDurationSeconds;

        /// <summary>直前に時間減速を処理した攻撃系列IDを取得します。</summary>
        public int LastHandledAttackSequenceId => lastHandledAttackSequenceId;

        private void Awake()
        {
            EnsureTimeSource();
        }

        private void Update()
        {
            Tick();
        }

        private void OnDisable()
        {
            ClearRuntimeState();
        }

        private void OnDestroy()
        {
            ClearRuntimeState();
        }

        /// <summary>
        /// Updateループまたはテスト駆動用の状態ステップ評価を行います。
        /// </summary>
        public void Tick()
        {
            if (!isSlowActive) return;

            EnsureTimeSource();
            double now = timeSource.Now;

            if (now < durationDeadlineUnscaled)
            {
                Time.timeScale = activeTargetTimeScale;
            }
            else if (now < recoveryDeadlineUnscaled && recoverySmoothSeconds > 0.0001f)
            {
                float t = (float)((now - durationDeadlineUnscaled) / recoverySmoothSeconds);
                Time.timeScale = Mathf.Lerp(activeTargetTimeScale, 1f, Mathf.Clamp01(t));
            }
            else
            {
                EndSlow();
            }
        }

        /// <summary>
        /// テスト用の時間源および設定値を注入・初期化します。
        /// </summary>
        public void ConfigureForTests(
            IUnscaledTimeSource customTimeSource,
            float customNormalTimeScale = 0.15f,
            float customNormalDuration = 0.08f,
            float customLethalTimeScale = 0.05f,
            float customLethalDuration = 0.20f,
            float customRecoverySmoothSeconds = 0.03f)
        {
            timeSource = customTimeSource;
            normalTimeScale = customNormalTimeScale;
            normalDurationSeconds = customNormalDuration;
            lethalTimeScale = customLethalTimeScale;
            lethalDurationSeconds = customLethalDuration;
            recoverySmoothSeconds = customRecoverySmoothSeconds;
            ClearRuntimeState();
        }

        /// <summary>
        /// ヒットフィードバック要求を処理し、プレイヤー攻撃時に時間減速を開始します。
        /// プレイヤー受撃時は減速を発動せず、複数の敵が同一攻撃で同時に被弾した場合は1回のみ発動します。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            if (!isEnabled) return;

            // 1. プレイヤー被弾時（プレイヤー自身が受撃対象、またはプレイヤー攻撃でない場合）は時間減速を一切発動しない
            if (!request.IsPlayerAttack || request.IsPlayerTarget || (request.Target != null && request.Target.Faction == CombatantMarker.CombatantFaction.Player))
            {
                return;
            }

            int sequenceId = request.Damage.AttackSequenceId;

            // 2. 単一攻撃系列（同一スイング）で複数の敵が同時に被弾した場合の重複排除：
            // 同一攻撃系列IDによる後続被弾では減速の再開始や持続時間の再延長を行わない
            if (sequenceId > 0 && sequenceId == lastHandledAttackSequenceId)
            {
                // ただし同一系列内で致命判定（敵撃破）が発生した場合は、減速強度のみ致死スケールへ強化（時間の延長はしない）
                if (request.HitType == CombatHitType.Lethal && isSlowActive)
                {
                    activeTargetTimeScale = Mathf.Min(activeTargetTimeScale, lethalTimeScale);
                    Time.timeScale = activeTargetTimeScale;
                }
                return;
            }

            // sequenceId が未指定（<= 0）かつ既に減速中である場合も重複延長を防止
            if (sequenceId <= 0 && isSlowActive)
            {
                if (request.HitType == CombatHitType.Lethal)
                {
                    activeTargetTimeScale = Mathf.Min(activeTargetTimeScale, lethalTimeScale);
                    Time.timeScale = activeTargetTimeScale;
                }
                return;
            }

            EnsureTimeSource();
            double now = timeSource.Now;

            float targetScale = request.HitType == CombatHitType.Lethal ? lethalTimeScale : normalTimeScale;
            float duration = request.HitType == CombatHitType.Lethal ? lethalDurationSeconds : normalDurationSeconds;

            if (sequenceId > 0)
            {
                lastHandledAttackSequenceId = sequenceId;
            }

            isSlowActive = true;
            startedAtUnscaled = now;
            durationDeadlineUnscaled = now + duration;
            recoveryDeadlineUnscaled = durationDeadlineUnscaled + recoverySmoothSeconds;
            activeTargetTimeScale = targetScale;
            Time.timeScale = activeTargetTimeScale;
        }

        /// <summary>
        /// 実行時減速状態を強制破棄し、Time.timeScale を確実に 1.0f に復元します。
        /// </summary>
        public void ClearRuntimeState()
        {
            EndSlow();
            lastHandledAttackSequenceId = 0;
        }

        private void EndSlow()
        {
            if (isSlowActive)
            {
                isSlowActive = false;
            }

            Time.timeScale = 1f;
            startedAtUnscaled = 0d;
            durationDeadlineUnscaled = 0d;
            recoveryDeadlineUnscaled = 0d;
            activeTargetTimeScale = 1f;
        }

        private void EnsureTimeSource()
        {
            if (timeSource == null)
            {
                timeSource = new RealtimeUnscaledTimeSource();
            }
        }

        private sealed class RealtimeUnscaledTimeSource : IUnscaledTimeSource
        {
            public double Now => Time.realtimeSinceStartupAsDouble;
        }
    }
}
