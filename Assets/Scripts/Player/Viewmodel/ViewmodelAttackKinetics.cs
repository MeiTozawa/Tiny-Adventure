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

        public float BaseAttackDuration => baseAttackDuration;
        public bool IsAttacking => isAttacking;
        public int CurrentComboIndex => currentComboIndex;
        public bool IsHitStopPaused => isHitStopPaused;
        public float AttackTimer => attackTimer;
        public float AttackDuration => attackDuration;

        /// <summary>
        /// 指定されたコンボ段数と速度倍率で出刀動作を開始します。
        /// </summary>
        public void TriggerAttack(int comboIndex, float speedMultiplier = 1f)
        {
            currentComboIndex = Mathf.Clamp(comboIndex, 0, 2);
            float speed = Mathf.Max(0.1f, speedMultiplier);
            attackDuration = baseAttackDuration / speed;
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

        private static void CalculateAttackMotion(int comboIndex, float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            switch (comboIndex)
            {
                case 0:
                    CalculateHorizontalSlash(progress, out offsetPos, out offsetRot);
                    break;
                case 1:
                    CalculateVerticalSlash(progress, out offsetPos, out offsetRot);
                    break;
                case 2:
                    CalculateThrust(progress, out offsetPos, out offsetRot);
                    break;
                default:
                    offsetPos = Vector3.zero;
                    offsetRot = Quaternion.identity;
                    break;
            }
        }

        private static void CalculateHorizontalSlash(float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < 0.15f)
            {
                float t = progress / 0.15f;
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(0.08f, 0.04f, -0.06f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(20f, -10f, -15f), t);
            }
            else if (progress < 0.45f)
            {
                float t = Mathf.SmoothStep(0f, 1f, (progress - 0.15f) / 0.30f);
                Vector3 windupPos = new Vector3(0.08f, 0.04f, -0.06f);
                Vector3 peakPos = new Vector3(-0.32f, -0.05f, 0.28f);
                offsetPos = Vector3.Lerp(windupPos, peakPos, t);
                Quaternion windupRot = Quaternion.Euler(20f, -10f, -15f);
                Quaternion peakRot = Quaternion.Euler(-60f, 15f, 45f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, (progress - 0.45f) / 0.55f);
                Vector3 peakPos = new Vector3(-0.32f, -0.05f, 0.28f);
                offsetPos = Vector3.Lerp(peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(-60f, 15f, 45f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }

        private static void CalculateVerticalSlash(float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < 0.15f)
            {
                float t = progress / 0.15f;
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(0.06f, 0.20f, -0.08f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-30f, -20f, -30f), t);
            }
            else if (progress < 0.45f)
            {
                float t = Mathf.SmoothStep(0f, 1f, (progress - 0.15f) / 0.30f);
                Vector3 windupPos = new Vector3(0.06f, 0.20f, -0.08f);
                Vector3 peakPos = new Vector3(-0.05f, -0.22f, 0.32f);
                offsetPos = Vector3.Lerp(windupPos, peakPos, t);
                Quaternion windupRot = Quaternion.Euler(-30f, -20f, -30f);
                Quaternion peakRot = Quaternion.Euler(30f, 15f, 45f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, (progress - 0.45f) / 0.55f);
                Vector3 peakPos = new Vector3(-0.05f, -0.22f, 0.32f);
                offsetPos = Vector3.Lerp(peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(30f, 15f, 45f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }

        private static void CalculateThrust(float progress, out Vector3 offsetPos, out Quaternion offsetRot)
        {
            if (progress < 0.15f)
            {
                float t = progress / 0.15f;
                offsetPos = Vector3.Lerp(Vector3.zero, new Vector3(-0.06f, 0.04f, -0.18f), t);
                offsetRot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(10f, 5f, -10f), t);
            }
            else if (progress < 0.45f)
            {
                float t = Mathf.SmoothStep(0f, 1f, (progress - 0.15f) / 0.30f);
                Vector3 windupPos = new Vector3(-0.06f, 0.04f, -0.18f);
                Vector3 peakPos = new Vector3(-0.08f, 0.02f, 0.70f);
                offsetPos = Vector3.Lerp(windupPos, peakPos, t);
                Quaternion windupRot = Quaternion.Euler(10f, 5f, -10f);
                Quaternion peakRot = Quaternion.Euler(0f, -5f, 5f);
                offsetRot = Quaternion.Slerp(windupRot, peakRot, t);
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, (progress - 0.45f) / 0.55f);
                Vector3 peakPos = new Vector3(-0.08f, 0.02f, 0.70f);
                offsetPos = Vector3.Lerp(peakPos, Vector3.zero, t);
                Quaternion peakRot = Quaternion.Euler(0f, -5f, 5f);
                offsetRot = Quaternion.Slerp(peakRot, Quaternion.identity, t);
            }
        }
    }
}
