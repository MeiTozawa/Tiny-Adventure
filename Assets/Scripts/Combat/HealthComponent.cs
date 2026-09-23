using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘対象の体力と死亡ライフサイクルを一元管理します。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(CombatantMarker))]
    public sealed class HealthComponent : MonoBehaviour, IHealthDeathSource
    {
        public const float DefaultMaximumHealth = 100f;

        [Header("ステータス設定")]
        [Tooltip("キャラクターの基礎ステータスアセットです。未設定時はデフォルト値（100）を使用します。")]
        [SerializeField]
        private CharacterStatsConfig statsConfig;

        private float maximumHealth = DefaultMaximumHealth;
        private CombatantMarker combatantMarker;
        private bool deathTransitionPublished;

        public CharacterStatsConfig StatsConfig
        {
            get => statsConfig;
            set => statsConfig = value;
        }

        /// <summary>体力のライフサイクル状態です。</summary>
        public HealthState State { get; private set; } = HealthState.Alive;

        /// <summary>設定された最大体力です。</summary>
        public float MaximumHealth => maximumHealth;

        /// <summary>最大体力の別名です。HUD と検証コードから同じ契約を参照できます。</summary>
        public float MaxHealth => MaximumHealth;

        /// <summary>現在の体力です。常に0以上、最大体力以下です。</summary>
        public float CurrentHealth { get; private set; }

        /// <summary>この体力が属する戦闘対象マーカーです。</summary>
        public CombatantMarker CombatantMarker => Marker;

        /// <summary>IHealthDeathSource 実装用のマーカープロパティです。</summary>
        public CombatantMarker Marker => combatantMarker;

        /// <summary>戦闘対象マーカーを設定します（テスト・DI用）。</summary>
        public void SetCombatantMarker(CombatantMarker marker) => combatantMarker = marker;

        public bool IsAlive => State == HealthState.Alive;
        public bool IsInDeathTransition => State == HealthState.DeathTransition;
        public bool IsRemoved => State == HealthState.Removed;

        /// <summary>体力が変化したときに現在値と最大値を通知します。</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>死亡遷移へ初めて入ったときに一度だけ通知します。</summary>
        public event Action Died;

        /// <summary>体力状態が変化したときに通知します。</summary>
        public event Action<HealthState> StateChanged;

        private void Awake()
        {
            combatantMarker = GetComponent<CombatantMarker>();
            combatantMarker?.SetDependencies(health: this);
            maximumHealth = statsConfig != null && statsConfig.MaximumHealth > 0f
                ? statsConfig.MaximumHealth
                : DefaultMaximumHealth;

            Assert.IsTrue(IsFinitePositive(maximumHealth), "HealthComponent: 最大体力は有限で0より大きい値である必要があります。");

            // シーン入場時の初期体力を確実に設定し、DamageService経由の最初の攻撃を受けられるようにします。
            CurrentHealth = maximumHealth;
        }

        private void OnValidate()
        {
            if (statsConfig != null && statsConfig.MaximumHealth > 0f)
            {
                maximumHealth = statsConfig.MaximumHealth;
            }
            else if (!IsFinitePositive(maximumHealth))
            {
                maximumHealth = DefaultMaximumHealth;
            }
        }

        /// <summary>
        /// 最大体力を設定します。不正な値の場合は既存の設定を変更せずエラーを返します。
        /// </summary>
        public Result Configure(float maxHealth)
        {
            if (!IsFinitePositive(maxHealth))
            {
                return GameError.InvalidParameter;
            }

            maximumHealth = maxHealth;
            CurrentHealth = ClampHealth(CurrentHealth);
            return Result.Ok();
        }

        /// <summary>
        /// Demo 入場時の体力と死亡状態を初期化します。
        /// </summary>
        public Result EnterDemo()
        {
            if (!IsFinitePositive(maximumHealth))
            {
                return GameError.InvalidState;
            }

            HealthState previousState = State;
            State = HealthState.Alive;
            deathTransitionPublished = false;
            CurrentHealth = ClampHealth(maximumHealth);

            if (previousState != State)
            {
                StateChanged?.Invoke(State);
            }

            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);
            return Result.Ok();
        }

        /// <summary>
        /// 正式な DamageRequest を検証して適用します。
        /// </summary>
        public Result Receive(DamageRequest request)
        {
            if (State != HealthState.Alive)
            {
                return GameError.TargetDead;
            }

            EnsureCombatantMarker();
            Assert.IsNotNull(combatantMarker, "HealthComponent: CombatantMarker参照がありません。");

            if (request.Target == null || request.Target != combatantMarker || !request.Target.IsIdentityValid)
            {
                return GameError.InvalidParameter;
            }

            if (request.Amount <= 0f || float.IsNaN(request.Amount) || float.IsInfinity(request.Amount))
            {
                return GameError.InvalidParameter;
            }

            if (request.Source == null || request.Source == combatantMarker || !request.Source.IsIdentityValid || !request.Source.IsAvailableForCombat)
            {
                return GameError.InvalidParameter;
            }

            if (!request.IsStructurallyValid)
            {
                return GameError.InvalidParameter;
            }

            float previousHealth = CurrentHealth;
            CurrentHealth = ClampHealth(previousHealth - request.Amount);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);

            if (CurrentHealth <= 0f)
            {
                CurrentHealth = 0f;
                EnterDeathTransition();
            }

            return Result.Ok();
        }

        /// <summary>
        /// 死亡アニメーション完了後に対象を Removed へ遷移させます。
        /// </summary>
        public Result CompleteDeath()
        {
            if (State != HealthState.DeathTransition)
            {
                return GameError.InvalidState;
            }

            CurrentHealth = 0f;
            State = HealthState.Removed;
            StateChanged?.Invoke(State);
            return Result.Ok();
        }

        private void EnterDeathTransition()
        {
            CurrentHealth = 0f;
            if (deathTransitionPublished)
            {
                return;
            }

            deathTransitionPublished = true;
            State = HealthState.DeathTransition;
            StateChanged?.Invoke(State);
            Died?.Invoke();
        }

        private float ClampHealth(float value)
        {
            if (!IsFinitePositive(maximumHealth))
            {
                return 0f;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, 0f, maximumHealth);
        }

        private void EnsureCombatantMarker()
        {
            _ = Marker;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }

    /// <summary>体力コンポーネントのライフサイクル状態です。</summary>
    public enum HealthState
    {
        Alive,
        DeathTransition,
        Removed
    }
}
