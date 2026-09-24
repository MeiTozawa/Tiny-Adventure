using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// コンボ攻撃における出刀軌跡ポーズ構造体です。
    /// </summary>
    [Serializable]
    public struct AttackMotionPose
    {
        public Vector3 windupPos;
        public Vector3 windupRotEuler;
        public Vector3 peakPos;
        public Vector3 peakRotEuler;

        public AttackMotionPose(Vector3 windupP, Vector3 windupR, Vector3 peakP, Vector3 peakR)
        {
            windupPos = windupP;
            windupRotEuler = windupR;
            peakPos = peakP;
            peakRotEuler = peakR;
        }

        public bool IsValid =>
            windupPos.sqrMagnitude > 0.000001f ||
            peakPos.sqrMagnitude > 0.000001f ||
            windupRotEuler.sqrMagnitude > 0.000001f ||
            peakRotEuler.sqrMagnitude > 0.000001f;
    }

    /// <summary>
    /// 第一人称視口武器の出刀プログラム運動学（Attack Kinetics）の数値設定を保持する ScriptableObject です。
    /// 基準出刀時間、ヒットストップ微震振幅、および3段連撃（横薙ぎ、縦斬り、突刺）の各軌跡ポーズをデータ駆動化します。
    /// </summary>
    [CreateAssetMenu(fileName = "NewViewmodelAttackKineticsConfig", menuName = "Tiny Adventure/Combat/Viewmodel Attack Kinetics Config")]
    public class ViewmodelAttackKineticsConfig : ScriptableObject
    {
        [Header("出刀アニメーション (Attack Kinetics)")]
        [Tooltip("基準攻撃所要時間（秒）です。")]
        [SerializeField, Min(0.01f)]
        private float baseAttackDuration = 0.50f;

        [Tooltip("ヒットストップ時の微震振幅（メートル）です。")]
        [SerializeField, Min(0f)]
        private float hitStopJitterAmplitude = 0.015f;

        [Header("コンボ軌跡ポーズ設定")]
        [SerializeField]
        private AttackMotionPose horizontalSlashPose = new(
            new Vector3(0.08f, 0.04f, -0.06f),
            new Vector3(20f, -10f, -15f),
            new Vector3(-0.32f, -0.05f, 0.28f),
            new Vector3(-60f, 15f, 45f));

        [SerializeField]
        private AttackMotionPose verticalSlashPose = new(
            new Vector3(0.06f, 0.20f, -0.08f),
            new Vector3(-30f, -20f, -30f),
            new Vector3(-0.05f, -0.22f, 0.32f),
            new Vector3(30f, 15f, 45f));

        [SerializeField]
        private AttackMotionPose thrustPose = new(
            new Vector3(-0.06f, 0.04f, -0.18f),
            new Vector3(10f, 5f, -10f),
            new Vector3(-0.08f, 0.02f, 0.70f),
            new Vector3(0f, -5f, 5f));

        public float BaseAttackDuration
        {
            get => baseAttackDuration;
            set => baseAttackDuration = Mathf.Max(0.01f, value);
        }

        public float HitStopJitterAmplitude
        {
            get => hitStopJitterAmplitude;
            set => hitStopJitterAmplitude = Mathf.Max(0f, value);
        }

        public AttackMotionPose HorizontalSlashPose
        {
            get => horizontalSlashPose;
            set => horizontalSlashPose = value;
        }

        public AttackMotionPose VerticalSlashPose
        {
            get => verticalSlashPose;
            set => verticalSlashPose = value;
        }

        public AttackMotionPose ThrustPose
        {
            get => thrustPose;
            set => thrustPose = value;
        }

        public AttackMotionPose GetPose(int comboIndex)
        {
            return comboIndex switch
            {
                0 => horizontalSlashPose,
                1 => verticalSlashPose,
                2 => thrustPose,
                _ => default
            };
        }
    }
}
