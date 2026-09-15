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
        [Min(0.01f)]
        public float Damage;

        [Tooltip("この段の有効攻撃射程（メートル）です。")]
        [Min(0.01f)]
        public float Range;

        [Tooltip("この段の出刀時に前方へ踏み込む突進距離（メートル）です。")]
        [Range(0f, 5.0f)]
        public float LungeDistance;

        [Tooltip("この段の踏み込み突進の継続時間（秒）です。")]
        [Range(0.02f, 0.5f)]
        public float LungeDuration;

        [Tooltip("この段のアニメーション再生速度倍率です。")]
        [Range(0.5f, 3.0f)]
        public float SpeedMultiplier;

        [Tooltip("この段の攻撃有効ウィンドウを閉じる正規化時間です。")]
        [Range(0.1f, 0.99f)]
        public float WindowCloseNormalizedTime;

        [Tooltip("この段の攻撃完了とみなす正規化時間です。")]
        [Range(0.1f, 1.0f)]
        public float CompletionNormalizedTime;
    }

    /// <summary>
    /// 3段コンボなどの多段攻撃シーケンスにおける各段の数値設定とリセット猶予時間を保持する ScriptableObject です。
    /// 従来の AttackConfigSO を継承し、単一設定としても後方互換性を保ちます。
    /// </summary>
    [CreateAssetMenu(fileName = "NewComboAttackConfig", menuName = "Tiny Adventure/Combat/Combo Attack Config")]
    public class ComboAttackConfigSO : AttackConfigSO
    {
        public const float DefaultComboResetTimeout = 0.45f;
        public const float MinimumComboResetTimeout = 0.1f;
        public const float MaximumComboResetTimeout = 2.0f;

        [Header("コンボ設定")]
        [Tooltip("コンボ各段の設定配列です（0: 横薙ぎ, 1: 縦斬り, 2: 突進刺突）。")]
        [SerializeField]
        private AttackConfigStep[] comboSteps = new AttackConfigStep[]
        {
            new AttackConfigStep
            {
                Damage = 20f,
                Range = 2.2f,
                LungeDistance = 0.8f,
                LungeDuration = 0.12f,
                SpeedMultiplier = 1.7f,
                WindowCloseNormalizedTime = 0.50f,
                CompletionNormalizedTime = 0.65f
            },
            new AttackConfigStep
            {
                Damage = 25f,
                Range = 2.2f,
                LungeDistance = 1.2f,
                LungeDuration = 0.15f,
                SpeedMultiplier = 1.6f,
                WindowCloseNormalizedTime = 0.55f,
                CompletionNormalizedTime = 0.70f
            },
            new AttackConfigStep
            {
                Damage = 40f,
                Range = 2.8f,
                LungeDistance = 2.2f,
                LungeDuration = 0.20f,
                SpeedMultiplier = 1.5f,
                WindowCloseNormalizedTime = 0.60f,
                CompletionNormalizedTime = 0.75f
            }
        };

        [Tooltip("攻撃完了後、コンボ段数が初期化されるまでの無入力猶予時間（秒）です。")]
        [SerializeField, Range(MinimumComboResetTimeout, MaximumComboResetTimeout)]
        private float comboResetTimeout = DefaultComboResetTimeout;

        public int StepCount => comboSteps != null ? comboSteps.Length : 0;

        public float ComboResetTimeout
        {
            get => comboResetTimeout;
            set => comboResetTimeout = Mathf.Clamp(value, MinimumComboResetTimeout, MaximumComboResetTimeout);
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
                    LungeDistance = base.LungeDistance,
                    LungeDuration = base.LungeDuration,
                    SpeedMultiplier = base.AttackSpeedMultiplier,
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
        public override float LungeDistance => StepCount > 0 ? comboSteps[0].LungeDistance : base.LungeDistance;
        public override float LungeDuration => StepCount > 0 ? comboSteps[0].LungeDuration : base.LungeDuration;
        public override float AttackCompletionNormalizedTime => StepCount > 0 ? comboSteps[0].CompletionNormalizedTime : base.AttackCompletionNormalizedTime;
        public override float AttackWindowCloseNormalizedTime => StepCount > 0 ? comboSteps[0].WindowCloseNormalizedTime : base.AttackWindowCloseNormalizedTime;

        public void SetStepsForTests(AttackConfigStep[] steps)
        {
            comboSteps = steps;
        }

        private void OnValidate()
        {
            comboResetTimeout = Mathf.Clamp(comboResetTimeout, MinimumComboResetTimeout, MaximumComboResetTimeout);
            if (comboSteps != null)
            {
                for (int i = 0; i < comboSteps.Length; i++)
                {
                    comboSteps[i].Damage = Mathf.Max(0.01f, comboSteps[i].Damage);
                    comboSteps[i].Range = Mathf.Max(0.01f, comboSteps[i].Range);
                    comboSteps[i].LungeDistance = Mathf.Clamp(comboSteps[i].LungeDistance, 0f, 5.0f);
                    comboSteps[i].LungeDuration = Mathf.Clamp(comboSteps[i].LungeDuration, 0.02f, 0.5f);
                    comboSteps[i].SpeedMultiplier = Mathf.Clamp(comboSteps[i].SpeedMultiplier, 0.5f, 3.0f);
                    comboSteps[i].WindowCloseNormalizedTime = Mathf.Clamp(comboSteps[i].WindowCloseNormalizedTime, 0.1f, 0.99f);
                    comboSteps[i].CompletionNormalizedTime = Mathf.Clamp(comboSteps[i].CompletionNormalizedTime, 0.1f, 1.0f);
                }
            }
        }
    }
}
