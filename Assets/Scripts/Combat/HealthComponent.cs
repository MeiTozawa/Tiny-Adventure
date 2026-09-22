using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘対象の体力と死亡ライフサイクルを一元管理します。
    /// </summary>
    [DisallowMultipleComponent]
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
        public CombatantMarker Marker => combatantMarker != null ? combatantMarker : (combatantMarker = GetComponent<CombatantMarker>());

        public bool IsAlive => State == HealthState.Alive;
        public bool IsInDeathTransition => State == HealthState.DeathTransition;
        public bool IsRemoved => State == HealthState.Removed;

        /// <summary>体力が変化したときに現在値と最大値を通知します。</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>死亡遷移へ初めて入ったときに一度だけ通知します。</summary>
        public event Action Died;

        /// <summary>体力状態が変化したときに通知します。</summary>
        public event Action<HealthState> StateChanged;

        /// <summary>設定または受傷を拒否した理由を日本語で通知します。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;

        private void Awake()
        {
            combatantMarker = GetComponent<CombatantMarker>();
            maximumHealth = statsConfig != null && statsConfig.MaximumHealth > 0f
                ? statsConfig.MaximumHealth
                : DefaultMaximumHealth;

            if (!IsFinitePositive(maximumHealth))
            {
                ReportDiagnostic("HealthComponentの最大体力は有限で0より大きい値である必要があります。", true);
                return;
            }

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
        /// 最大体力を設定します。不正な値の場合は既存の設定を変更しません。
        /// </summary>
        public bool Configure(float maxHealth)
        {
            return Configure(maxHealth, out _);
        }

        /// <summary>
        /// 最大体力を設定し、失敗理由を日本語で返します。
        /// </summary>
        public bool Configure(float maxHealth, out string diagnostic)
        {
            if (!IsFinitePositive(maxHealth))
            {
                diagnostic = "HealthComponentの最大体力は有限で0より大きい値である必要があります。";
                ReportDiagnostic(diagnostic, true);
                return false;
            }

            maximumHealth = maxHealth;
            CurrentHealth = ClampHealth(CurrentHealth);
            diagnostic = string.Empty;
            LastDiagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// Demo 入場時の体力と死亡状態を初期化します。
        /// </summary>
        public bool EnterDemo()
        {
            return EnterDemo(out _);
        }

        /// <summary>
        /// Demo 入場時の体力と死亡状態を初期化し、失敗理由を返します。
        /// </summary>
        public bool EnterDemo(out string diagnostic)
        {
            if (!IsFinitePositive(maximumHealth))
            {
                diagnostic = "HealthComponentの最大体力が不正なため、Demoに入場できません。";
                ReportDiagnostic(diagnostic, true);
                return false;
            }

            HealthState previousState = State;
            State = HealthState.Alive;
            deathTransitionPublished = false;
            CurrentHealth = ClampHealth(maximumHealth);
            LastDiagnostic = string.Empty;

            if (previousState != State)
            {
                StateChanged?.Invoke(State);
            }

            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);
            diagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// 正式な DamageRequest をこの対象へ適用します。
        /// </summary>
        public bool Receive(DamageRequest request)
        {
            return Receive(request, out _);
        }

        /// <summary>
        /// 正式な DamageRequest を検証して適用します。
        /// </summary>
        public bool Receive(DamageRequest request, out string diagnostic)
        {
            if (State != HealthState.Alive)
            {
                diagnostic = $"戦闘対象「{GetCombatantName()}」は死亡遷移中または除去済みのため、追加ダメージを無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            EnsureCombatantMarker();
            if (combatantMarker == null)
            {
                diagnostic = "HealthComponentにCombatantMarkerがないため、ダメージを適用できません。";
                ReportDiagnostic(diagnostic, true);
                return false;
            }

            if (request.Target == null || request.Target != combatantMarker || !request.Target.IsIdentityValid)
            {
                diagnostic = $"HealthComponentの対象と一致しないダメージ対象を無視しました。対象「{GetCombatantName()}」。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (!DamageRequest.IsFinitePositiveAmount(request.Amount))
            {
                diagnostic = $"戦闘対象「{GetCombatantName()}」への無効なダメージ量を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (request.Source == null || request.Source == combatantMarker || !request.Source.IsIdentityValid || !request.Source.IsAvailableForCombat)
            {
                diagnostic = $"戦闘対象「{GetCombatantName()}」への無効なダメージ発生元を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            if (!request.IsStructurallyValid)
            {
                diagnostic = $"戦闘対象「{GetCombatantName()}」への不正なダメージ要求を無視しました。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            float previousHealth = CurrentHealth;
            CurrentHealth = ClampHealth(previousHealth - request.Amount);
            HealthChanged?.Invoke(CurrentHealth, MaximumHealth);

            if (CurrentHealth <= 0f)
            {
                CurrentHealth = 0f;
                EnterDeathTransition();
            }

            diagnostic = string.Empty;
            LastDiagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// 死亡アニメーション完了後に対象を Removed へ遷移させます。
        /// </summary>
        public bool CompleteDeath()
        {
            return CompleteDeath(out _);
        }

        /// <summary>
        /// 死亡アニメーション完了後に対象を Removed へ遷移させます。
        /// </summary>
        public bool CompleteDeath(out string diagnostic)
        {
            if (State != HealthState.DeathTransition)
            {
                diagnostic = State == HealthState.Removed
                    ? $"戦闘対象「{GetCombatantName()}」は既にRemoved状態です。"
                    : $"戦闘対象「{GetCombatantName()}」は死亡遷移中ではありません。";
                ReportDiagnostic(diagnostic, false);
                return false;
            }

            CurrentHealth = 0f;
            State = HealthState.Removed;
            StateChanged?.Invoke(State);
            diagnostic = string.Empty;
            LastDiagnostic = string.Empty;
            return true;
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
            if (combatantMarker == null)
            {
                combatantMarker = GetComponent<CombatantMarker>();
            }
        }

        private string GetCombatantName()
        {
            return combatantMarker != null && !string.IsNullOrWhiteSpace(combatantMarker.CombatantId)
                ? combatantMarker.CombatantId
                : gameObject.name;
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[体力診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
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
