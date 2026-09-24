using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 単一コンボ段における攻撃数値設定（ダメージ・射程・踏み込み・速度倍率・判定タイミング）を表すデータ構造です。
    /// </summary>
    [Serializable]
    public struct AttackConfigStep
    {
        [Tooltip("この段の攻撃ダメージです。")]
        [Min(0f)]
        public float Damage;

        [Tooltip("この段の有効攻撃射程（メートル）です。")]
        [Min(0f)]
        public float Range;

        [Tooltip("この段のアニメーション再生速度倍率です。")]
        [Min(0.01f)]
        public float SpeedMultiplier;

        [Tooltip("この段の攻撃有効ウィンドウを開く正規化時間（前摇終了・出刀判定開始点）です。")]
        [Range(0f, 1f)]
        public float WindowOpenNormalizedTime;

        [Tooltip("この段の攻撃有効ウィンドウを閉じる正規化時間です。")]
        [Range(0f, 1f)]
        public float WindowCloseNormalizedTime;

        [Tooltip("この段の攻撃完了とみなす正規化時間です。")]
        [Range(0f, 1f)]
        public float CompletionNormalizedTime;
    }

    /// <summary>
    /// 3段コンボなどの多段攻撃シーケンスにおける各段の数値設定とリセット猶予時間を保持する ScriptableObject です。
    /// 従来の AttackConfig を継承し、単一設定としても後方互換性を保ちます。
    /// </summary>
    [CreateAssetMenu(fileName = "NewComboAttackConfig", menuName = "Tiny Adventure/Combat/Combo Attack Config")]
    public class ComboAttackConfig : AttackConfig
    {
        [Header("コンボ設定")]
        [Tooltip("コンボ各段の設定配列です。")]
        [SerializeField]
        private AttackConfigStep[] comboSteps;

        [Tooltip("攻撃完了後、コンボ段数が初期化されるまでの無入力猶予時間（秒）です。")]
        [SerializeField, Min(0f)]
        private float comboResetTimeout;

        public int StepCount => comboSteps != null ? comboSteps.Length : 0;

        public float ComboResetTimeout
        {
            get => comboResetTimeout;
            set => comboResetTimeout = Mathf.Max(0f, value);
        }

        /// <summary>
        /// 指定インデックスのコンボ段設定を取得します。範囲外の場合は直近の段に安全にクランプされます。
        /// </summary>
        public AttackConfigStep GetStep(int index)
        {
            if (comboSteps == null || comboSteps.Length == 0)
            {
                return new AttackConfigStep
                {
                    Damage = base.AttackDamage,
                    Range = base.AttackRange,
                    SpeedMultiplier = base.AttackSpeedMultiplier,
                    WindowOpenNormalizedTime = base.AttackWindowOpenNormalizedTime,
                    WindowCloseNormalizedTime = base.AttackWindowCloseNormalizedTime,
                    CompletionNormalizedTime = base.AttackCompletionNormalizedTime
                };
            }

            int clampedIndex = Mathf.Clamp(index, 0, comboSteps.Length - 1);
            return comboSteps[clampedIndex];
        }

        public override float AttackDamage => StepCount > 0 ? comboSteps[0].Damage : base.AttackDamage;
        public override float AttackRange => StepCount > 0 ? comboSteps[0].Range : base.AttackRange;
        public override float AttackSpeedMultiplier => StepCount > 0 ? comboSteps[0].SpeedMultiplier : base.AttackSpeedMultiplier;
        public override float AttackWindowOpenNormalizedTime => StepCount > 0 ? comboSteps[0].WindowOpenNormalizedTime : base.AttackWindowOpenNormalizedTime;
        public override float AttackWindowCloseNormalizedTime => StepCount > 0 ? comboSteps[0].WindowCloseNormalizedTime : base.AttackWindowCloseNormalizedTime;
        public override float AttackCompletionNormalizedTime => StepCount > 0 ? comboSteps[0].CompletionNormalizedTime : base.AttackCompletionNormalizedTime;

        internal void SetSteps(AttackConfigStep[] steps)
        {
            comboSteps = steps;
        }

        private void OnValidate()
        {
            comboResetTimeout = Mathf.Max(0f, comboResetTimeout);
            if (comboSteps != null)
            {
                for (int i = 0; i < comboSteps.Length; i++)
                {
                    comboSteps[i].Damage = Mathf.Max(0f, comboSteps[i].Damage);
                    comboSteps[i].Range = Mathf.Max(0f, comboSteps[i].Range);
                    comboSteps[i].SpeedMultiplier = Mathf.Max(0.01f, comboSteps[i].SpeedMultiplier);
                    comboSteps[i].WindowOpenNormalizedTime = Mathf.Clamp01(comboSteps[i].WindowOpenNormalizedTime);
                    comboSteps[i].WindowCloseNormalizedTime = Mathf.Clamp01(comboSteps[i].WindowCloseNormalizedTime);
                    comboSteps[i].CompletionNormalizedTime = Mathf.Clamp01(comboSteps[i].CompletionNormalizedTime);
                }
            }
        }
    }
}
