using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 単一の攻撃動作に関する数値設定（ダメージ、射程、冷却、判定時間、速度感・突進）を保持する ScriptableObject です。
    /// 武器や攻撃種別ごとにアセットとして保存し、再利用・データ駆動化を可能にします。
    /// </summary>
    [CreateAssetMenu(fileName = "NewAttackConfig", menuName = "Tiny Adventure/Combat/Attack Config")]
    public class AttackConfigSO : ScriptableObject
    {
        public const float MinimumAttackRange = 0.01f;
        public const float MinimumDamage = 0.01f;
        public const float MinimumCooldown = 0f;
        public const float DefaultCompletionNormalizedTime = 0.70f;
        public const float DefaultWindowCloseNormalizedTime = 0.55f;

        public const float MinimumAttackSpeedMultiplier = 0.5f;
        public const float MaximumAttackSpeedMultiplier = 3.0f;
        public const float DefaultAttackSpeedMultiplier = 1.6f;

        public const float MinimumLungeDistance = 0f;
        public const float MaximumLungeDistance = 5.0f;
        public const float DefaultLungeDistance = 1.2f;

        public const float MinimumLungeDuration = 0.02f;
        public const float MaximumLungeDuration = 0.5f;
        public const float DefaultLungeDuration = 0.15f;

        [Header("ダメージと射程")]
        [SerializeField, Min(MinimumDamage)]
        private float attackDamage = 25f;

        [SerializeField, Min(MinimumAttackRange)]
        private float attackRange = 2.2f;

        [Header("タイミングと冷却")]
        [SerializeField, Min(MinimumCooldown)]
        private float attackCooldown = 1.25f;

        [Tooltip("攻撃有効ウィンドウを閉じるアニメーション正規化時間です。")]
        [SerializeField, Range(0.1f, 0.99f)]
        private float attackWindowCloseNormalizedTime = DefaultWindowCloseNormalizedTime;

        [Tooltip("攻撃動作完了とみなすアニメーション正規化時間です。")]
        [SerializeField, Range(0.1f, 1f)]
        private float attackCompletionNormalizedTime = DefaultCompletionNormalizedTime;

        [Header("速度感と踏み込み（Kinetics）")]
        [Tooltip("攻撃アニメーションの再生速度倍率です（1.5〜1.8推奨）。")]
        [SerializeField, Range(MinimumAttackSpeedMultiplier, MaximumAttackSpeedMultiplier)]
        private float attackSpeedMultiplier = DefaultAttackSpeedMultiplier;

        [Tooltip("出刀時に前方へ踏み込む突進距離（メートル）です。")]
        [SerializeField, Range(MinimumLungeDistance, MaximumLungeDistance)]
        private float lungeDistance = DefaultLungeDistance;

        [Tooltip("踏み込み突進の継続時間（秒）です。出刀に合わせて短時間で収束します。")]
        [SerializeField, Range(MinimumLungeDuration, MaximumLungeDuration)]
        private float lungeDuration = DefaultLungeDuration;

        public virtual float AttackDamage
        {
            get => attackDamage;
            set => attackDamage = Mathf.Max(MinimumDamage, value);
        }

        public virtual float AttackRange
        {
            get => attackRange;
            set => attackRange = Mathf.Max(MinimumAttackRange, value);
        }

        public virtual float AttackCooldown
        {
            get => attackCooldown;
            set => attackCooldown = Mathf.Max(MinimumCooldown, value);
        }

        public virtual float AttackWindowCloseNormalizedTime
        {
            get => attackWindowCloseNormalizedTime;
            set => attackWindowCloseNormalizedTime = Mathf.Clamp(value, 0.1f, 0.99f);
        }

        public virtual float AttackCompletionNormalizedTime
        {
            get => attackCompletionNormalizedTime;
            set => attackCompletionNormalizedTime = Mathf.Clamp(value, 0.1f, 1f);
        }

        public virtual float AttackSpeedMultiplier
        {
            get => attackSpeedMultiplier;
            set => attackSpeedMultiplier = Mathf.Clamp(value, MinimumAttackSpeedMultiplier, MaximumAttackSpeedMultiplier);
        }

        public virtual float LungeDistance
        {
            get => lungeDistance;
            set => lungeDistance = Mathf.Clamp(value, MinimumLungeDistance, MaximumLungeDistance);
        }

        public virtual float LungeDuration
        {
            get => lungeDuration;
            set => lungeDuration = Mathf.Clamp(value, MinimumLungeDuration, MaximumLungeDuration);
        }

        private void OnValidate()
        {
            attackDamage = Mathf.Max(MinimumDamage, attackDamage);
            attackRange = Mathf.Max(MinimumAttackRange, attackRange);
            attackCooldown = Mathf.Max(MinimumCooldown, attackCooldown);
            attackWindowCloseNormalizedTime = Mathf.Clamp(attackWindowCloseNormalizedTime, 0.1f, 0.99f);
            attackCompletionNormalizedTime = Mathf.Clamp(attackCompletionNormalizedTime, 0.1f, 1f);
            attackSpeedMultiplier = Mathf.Clamp(attackSpeedMultiplier, MinimumAttackSpeedMultiplier, MaximumAttackSpeedMultiplier);
            lungeDistance = Mathf.Clamp(lungeDistance, MinimumLungeDistance, MaximumLungeDistance);
            lungeDuration = Mathf.Clamp(lungeDuration, MinimumLungeDuration, MaximumLungeDuration);
        }
    }
}
