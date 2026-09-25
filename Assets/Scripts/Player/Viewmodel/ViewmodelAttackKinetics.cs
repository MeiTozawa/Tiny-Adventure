using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視口武器の出刀プログラム運動学（Attack Kinetics）を計算する純粋計算モジュールです。
    /// 単一責任：コンボ攻撃の所要時間、進捗、3段連撃（横薙ぎ、縦斬り、突刺）の補間軌跡およびヒットストップ微震の計算。
    /// 数値データおよび軌跡ポーズは ViewmodelAttackKineticsConfig (ScriptableObject) から供給されます。
    /// </summary>
    public sealed class ViewmodelAttackKinetics
    {
        private const float DefaultBaseAttackDuration = 0.50f;
        private const float DefaultHitStopJitterAmplitude = 0.015f;

        private static readonly AttackMotionPose DefaultHorizontalSlash = new(
            new Vector3(0.08f, 0.04f, -0.06f),
            new Vector3(20f, -10f, -15f),
            new Vector3(-0.32f, -0.05f, 0.28f),
            new Vector3(-60f, 15f, 45f));

        private static readonly AttackMotionPose DefaultVerticalSlash = new(
            new Vector3(0.06f, 0.20f, -0.08f),
            new Vector3(-30f, -20f, -30f),
            new Vector3(-0.05f, -0.22f, 0.32f),
            new Vector3(30f, 15f, 45f));

        private static readonly AttackMotionPose DefaultThrust = new(
            new Vector3(-0.06f, 0.04f, -0.18f),
            new Vector3(10f, 5f, -10f),
            new Vector3(-0.08f, 0.02f, 0.70f),
            new Vector3(0f, -5f, 5f));

        private ViewmodelAttackKineticsConfig config;

        private bool isAttacking;
        private int currentComboIndex;
        private float attackTimer;
        private float attackDuration;
        private bool isHitStopPaused;
        private Vector3 hitStopJitterOffset;
        private float currentStrikeOpenProgress;
        private float currentStrikeCloseProgress;

        public ViewmodelAttackKinetics(ViewmodelAttackKineticsConfig config = null)
        {
            this.config = config;
        }

        public void Configure(ViewmodelAttackKineticsConfig configAsset)
        {
            config = configAsset;
        }

        public ViewmodelAttackKineticsConfig Config
        {
            get => config;
            set => config = value;
        }

        public float BaseAttackDuration => config.BaseAttackDuration;
        public float HitStopJitterAmplitude => config.HitStopJitterAmplitude;

        public bool IsAttacking => isAttacking;
        public int CurrentComboIndex => currentComboIndex;
        public bool IsHitStopPaused => isHitStopPaused;
        public float AttackTimer => attackTimer;
        public float AttackDuration => attackDuration;
        public float AttackProgress => attackDuration > 0.0001f ? Mathf.Clamp01(attackTimer / attackDuration) : 0f;
        public float CurrentStrikeOpenProgress => currentStrikeOpenProgress;
        public float CurrentStrikeCloseProgress => currentStrikeCloseProgress;
        public bool IsInDamageWindow => isAttacking && AttackProgress >= currentStrikeOpenProgress && AttackProgress <= currentStrikeCloseProgress;

        /// <summary>
        /// 指定されたコンボ段数、速度倍率、および開閉進行度で出刀動作を開始します。
        /// </summary>
        public void TriggerAttack(
            int comboIndex,
            float speedMultiplier,
            float strikeOpenProgress,
            float strikeCloseProgress)
        {
            currentComboIndex = Mathf.Clamp(comboIndex, 0, 2);
            float speed = Mathf.Max(0.01f, speedMultiplier);
            float effectiveBaseDuration = BaseAttackDuration;
            attackDuration = effectiveBaseDuration / speed;
            currentStrikeOpenProgress = Mathf.Clamp01(strikeOpenProgress);
            currentStrikeCloseProgress = Mathf.Clamp01(strikeCloseProgress);
            attackTimer = 0f;
            isAttacking = true;
            isHitStopPaused = false;
            hitStopJitterOffset = Vector3.zero;
        }

        public void TriggerAttack(int comboIndex, float speedMultiplier)
        {
            TriggerAttack(comboIndex, speedMultiplier, 0f, 1f);
        }

        /// <summary>
        /// 出刀動作を中断し、待機姿勢へ即座にリセットします。
        /// </summary>
        public void CancelAttack()
        {
            isAttacking = false;
            attackTimer = 0f;
            isHitStopPaused = false;
            hitStopJitterOffset = Vector3.zero;
        }

        /// <summary>
        /// ヒットストップ開始時に出刀のタイマー進行を一時停止します。
        /// </summary>
        public void BeginHitStop()
        {
            isHitStopPaused = true;
        }

        /// <summary>
        /// ヒットストップ終了時に出刀の進行を再開します。
        /// </summary>
        public void EndHitStop()
        {
            isHitStopPaused = false;
            hitStopJitterOffset = Vector3.zero;
        }

        /// <summary>
        /// 出刀運動の進行とオフセットを計算・評価します。
        /// </summary>
        public void Evaluate(float safeDeltaTime, out Vector3 offsetPos, out Quaternion offsetRot, out float progress, out bool justCompleted)
        {
            offsetPos = Vector3.zero;
            offsetRot = Quaternion.identity;
            progress = 0f;
            justCompleted = false;

            if (!isAttacking)
            {
                return;
            }

            if (!isHitStopPaused)
            {
                attackTimer += safeDeltaTime;
            }
            else
            {
                // 刀肉停頓中の高周波微震（刃が骨や甲冑に噛み込む手応え・ブレードジッター）
                hitStopJitterOffset = UnityEngine.Random.insideUnitSphere * HitStopJitterAmplitude;
            }

            progress = attackDuration > 0.0001f ? Mathf.Clamp01(attackTimer / attackDuration) : 1f;
            CalculateAttackMotion(currentComboIndex, progress, out offsetPos, out offsetRot);

            if (isHitStopPaused)
            {
                offsetPos += hitStopJitterOffset;
            }

            if (progress >= 1f && !isHitStopPaused)
            {
                isAttacking = false;
                justCompleted = true;
            }
        }

        private void CalculateAttackMotion(int comboIndex, float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            AttackMotionPose pose = GetEffectivePose(comboIndex);
            if (!pose.IsValid)
            {
                offsetPos = Vector3.zero;
                offsetRot = Quaternion.identity;
                return;
            }

            CalculatePoseMotion(in pose, progress, currentStrikeOpenProgress, currentStrikeCloseProgress, out offsetPos, out offsetRot);
        }

        private AttackMotionPose GetEffectivePose(int comboIndex)
        {
            if (config != null)
            {
                AttackMotionPose configuredPose = config.GetPose(comboIndex);
                if (configuredPose.IsValid)
                {
                    return configuredPose;
                }
            }

            return comboIndex switch
            {
                0 => DefaultHorizontalSlash,
                1 => DefaultVerticalSlash,
                2 => DefaultThrust,
                _ => default
            };
        }

        private static void CalculatePoseMotion(
            in AttackMotionPose pose,
            float progress,
            float openProgress,
            float closeProgress,
            out Vector3 offsetPos,
            out Quaternion offsetRot)
        {
            if (progress < openProgress)
            {
                float t = Mathf.SmoothStep(0f, 1f, progress / Mathf.Max(0.001f, openProgress));
                offsetPos = Vector3.Lerp(Vector3.zero, pose.windupPos, t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(pose.windupRotEuler), t);
            }
            else if (progress < closeProgress)
            {
                float strikeSpan = Mathf.Max(0.001f, closeProgress - openProgress);
                float strikeRatio = (progress - openProgress) / strikeSpan;
                float t = 1f - (1f - strikeRatio) * (1f - strikeRatio);
                offsetPos = Vector3.Lerp(pose.windupPos, pose.peakPos, t);
                Quaternion windupRot = Quaternion.Euler(pose.windupRotEuler);
                Quaternion peakRot = Quaternion.Euler(pose.peakRotEuler);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float recoverySpan = Mathf.Max(0.001f, 1f - closeProgress);
                float t = Mathf.SmoothStep(0f, 1f, (progress - closeProgress) / recoverySpan);
                offsetPos = Vector3.Lerp(pose.peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(pose.peakRotEuler);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }
    }
}
