using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視口武器の出刀プログラム運動学（Attack Kinetics）を計算する独立モジュールです。
    /// 単一責任：コンボ攻撃の所要時間、進捗、3段連撃（横薙ぎ、縦斬り、突刺）の補間軌跡およびヒットストップ微震の計算。
    /// </summary>
    [Serializable]
    public sealed class ViewmodelAttackKinetics
    {
        [Header("出刀アニメーション (Attack Kinetics)")]
        [Tooltip("基準攻撃所要時間（秒）です。")]
        [SerializeField, Min(0.05f)]
        private float baseAttackDuration = 0.50f;

        private bool isAttacking;
        private int currentComboIndex;
        private float attackTimer;
        private float attackDuration;
        private bool isHitStopPaused;
        private Vector3 hitStopJitterOffset;
        private float currentStrikeOpenProgress = DefaultStrikeOpenProgress;
        private float currentStrikeCloseProgress = DefaultStrikeCloseProgress;

        public const float DefaultStrikeOpenProgress = 0.25f;
        public const float DefaultStrikeCloseProgress = 0.38f;

        public float BaseAttackDuration => baseAttackDuration;
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
            float speedMultiplier = 1f,
            float strikeOpenProgress = DefaultStrikeOpenProgress,
            float strikeCloseProgress = DefaultStrikeCloseProgress)
        {
            currentComboIndex = Mathf.Clamp(comboIndex, 0, 2);
            float speed = Mathf.Max(0.1f, speedMultiplier);
            attackDuration = baseAttackDuration / speed;
            currentStrikeOpenProgress = Mathf.Clamp(strikeOpenProgress, 0.01f, 0.90f);
            currentStrikeCloseProgress = Mathf.Clamp(strikeCloseProgress, currentStrikeOpenProgress + 0.02f, 0.99f);
            attackTimer = 0f;
            isAttacking = true;
            isHitStopPaused = false;
            hitStopJitterOffset = Vector3.zero;
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
                hitStopJitterOffset = UnityEngine.Random.insideUnitSphere * 0.0025f;
            }

            progress = Mathf.Clamp01(attackTimer / attackDuration);
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
            switch (comboIndex)
            {
                case 0:
                    CalculateHorizontalSlash(progress, currentStrikeOpenProgress, currentStrikeCloseProgress, out offsetPos, out offsetRot);
                    break;
                case 1:
                    CalculateVerticalSlash(progress, currentStrikeOpenProgress, currentStrikeCloseProgress, out offsetPos, out offsetRot);
                    break;
                case 2:
                    CalculateThrust(progress, currentStrikeOpenProgress, currentStrikeCloseProgress, out offsetPos, out offsetRot);
                    break;
                default:
                    offsetPos = Vector3.zero;
                    offsetRot = Quaternion.identity;
                    break;
            }
        }

        private static void CalculateHorizontalSlash(float progress, float openProgress, float closeProgress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < openProgress)
            {
                float t = Mathf.SmoothStep(0f, 1f, progress / Mathf.Max(0.001f, openProgress));
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(0.08f, 0.04f, -0.06f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(20f, -10f, -15f), t);
            }
            else if (progress < closeProgress)
            {
                float strikeSpan = Mathf.Max(0.001f, closeProgress - openProgress);
                float strikeRatio = (progress - openProgress) / strikeSpan;
                // 爆発的な出刀加速（Ease-Out）：始動直後に最高初速で一閃し、軟弱な減速感を完全排除
                float t = 1f - (1f - strikeRatio) * (1f - strikeRatio);
                Vector3 windupPos = new Vector3(0.08f, 0.04f, -0.06f);
                Vector3 peakPos = new Vector3(-0.32f, -0.05f, 0.28f);
                offsetPos = Vector3.Lerp(windupPos, peakPos, t);
                Quaternion windupRot = Quaternion.Euler(20f, -10f, -15f);
                Quaternion peakRot = Quaternion.Euler(-60f, 15f, 45f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float recoverySpan = Mathf.Max(0.001f, 1f - closeProgress);
                float t = Mathf.SmoothStep(0f, 1f, (progress - closeProgress) / recoverySpan);
                Vector3 peakPos = new Vector3(-0.32f, -0.05f, 0.28f);
                offsetPos = Vector3.Lerp(peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(-60f, 15f, 45f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }

        private static void CalculateVerticalSlash(float progress, float openProgress, float closeProgress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < openProgress)
            {
                float t = Mathf.SmoothStep(0f, 1f, progress / Mathf.Max(0.001f, openProgress));
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(0.06f, 0.20f, -0.08f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-30f, -20f, -30f), t);
            }
            else if (progress < closeProgress)
            {
                float strikeSpan = Mathf.Max(0.001f, closeProgress - openProgress);
                float strikeRatio = (progress - openProgress) / strikeSpan;
                // 爆発的な出刀加速（Ease-Out）：始動直後に最高初速で一閃し、軟弱な減速感を完全排除
                float t = 1f - (1f - strikeRatio) * (1f - strikeRatio);
                Vector3 windupPos = new Vector3(0.06f, 0.20f, -0.08f);
                Vector3 peakPos = new Vector3(-0.05f, -0.22f, 0.32f);
                offsetPos = Vector3.Lerp(windupPos, peakPos, t);
                Quaternion windupRot = Quaternion.Euler(-30f, -20f, -30f);
                Quaternion peakRot = Quaternion.Euler(30f, 15f, 45f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float recoverySpan = Mathf.Max(0.001f, 1f - closeProgress);
                float t = Mathf.SmoothStep(0f, 1f, (progress - closeProgress) / recoverySpan);
                Vector3 peakPos = new Vector3(-0.05f, -0.22f, 0.32f);
                offsetPos = Vector3.Lerp(peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(30f, 15f, 45f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }

        private static void CalculateThrust(float progress, float openProgress, float closeProgress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < openProgress)
            {
                float t = Mathf.SmoothStep(0f, 1f, progress / Mathf.Max(0.001f, openProgress));
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(-0.06f, 0.04f, -0.18f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(10f, 5f, -10f), t);
            }
            else if (progress < closeProgress)
            {
                float strikeSpan = Mathf.Max(0.001f, closeProgress - openProgress);
                float strikeRatio = (progress - openProgress) / strikeSpan;
                // 爆発的な出刀加速（Ease-Out）：始動直後に最高初速で一闪し、軟弱な減速感を完全排除
                float t = 1f - (1f - strikeRatio) * (1f - strikeRatio);
                Vector3 windupPos = new Vector3(-0.06f, 0.04f, -0.18f);
                Vector3 peakPos = new Vector3(-0.08f, 0.02f, 0.70f);
                offsetPos = Vector3.Lerp(windupPos, peakPos, t);
                Quaternion windupRot = Quaternion.Euler(10f, 5f, -10f);
                Quaternion peakRot = Quaternion.Euler(0f, -5f, 5f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float recoverySpan = Mathf.Max(0.001f, 1f - closeProgress);
                float t = Mathf.SmoothStep(0f, 1f, (progress - closeProgress) / recoverySpan);
                Vector3 peakPos = new Vector3(-0.08f, 0.02f, 0.70f);
                offsetPos = Vector3.Lerp(peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(0f, -5f, 5f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }
    }
}
