using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ユニットの陣営、安定した識別子、命中レイヤーを保持します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatantMarker : MonoBehaviour, ICombatant
    {
        public enum CombatantFaction
        {
            Player,
            Enemy
        }

        [Header("戦闘対象")]
        [SerializeField]
        private CombatantFaction faction = CombatantFaction.Player;

        [SerializeField]
        private string combatantId = "Knight";

        [SerializeField, Min(0)]
        private int hitLayer;

        [SerializeField]
        private HealthComponent healthComponent;

        [SerializeField]
        private HitFlashReceiver flashReceiver;

        [SerializeField]
        private PlayerAnimationDriver playerAnimationDriver;

        [SerializeField]
        private EnemyAnimationDriver enemyAnimationDriver;

        [SerializeField]
        private Animator targetAnimator;

        private IKnockbackReceiver knockbackReceiver;
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");

        public CombatantMarker Marker => this;
        public CombatantFaction Faction => faction;
        public string CombatantId => combatantId;
        public int HitLayer => hitLayer;
        public HealthComponent Health => healthComponent != null ? healthComponent : (healthComponent = GetComponent<HealthComponent>());
        public IKnockbackReceiver KnockbackReceiver => knockbackReceiver ??= (GetComponent<IKnockbackReceiver>() ?? GetComponentInParent<IKnockbackReceiver>() ?? GetComponentInChildren<IKnockbackReceiver>());
        public HitFlashReceiver FlashReceiver => flashReceiver != null ? flashReceiver : (flashReceiver = GetComponent<HitFlashReceiver>() ?? GetComponentInChildren<HitFlashReceiver>());

        private void Awake()
        {
            if (healthComponent == null)
            {
                healthComponent = GetComponent<HealthComponent>();
            }
            if (knockbackReceiver == null)
            {
                knockbackReceiver = GetComponent<IKnockbackReceiver>() ?? GetComponentInParent<IKnockbackReceiver>() ?? GetComponentInChildren<IKnockbackReceiver>();
            }
            if (flashReceiver == null)
            {
                flashReceiver = GetComponent<HitFlashReceiver>() ?? GetComponentInChildren<HitFlashReceiver>();
            }
            CacheAnimationDrivers();
        }

        private void CacheAnimationDrivers()
        {
            if (playerAnimationDriver == null) playerAnimationDriver = GetComponent<PlayerAnimationDriver>() ?? GetComponentInParent<PlayerAnimationDriver>() ?? GetComponentInChildren<PlayerAnimationDriver>();
            if (enemyAnimationDriver == null) enemyAnimationDriver = GetComponent<EnemyAnimationDriver>() ?? GetComponentInParent<EnemyAnimationDriver>() ?? GetComponentInChildren<EnemyAnimationDriver>();
            if (targetAnimator == null) targetAnimator = GetComponent<Animator>() ?? GetComponentInParent<Animator>() ?? GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// 被弾アニメーションを駆動します。PlayerAnimationDriver、EnemyAnimationDriver、Animator の順で実行します。
        /// </summary>
        public bool TriggerHitAnimation()
        {
            if (playerAnimationDriver == null && enemyAnimationDriver == null && targetAnimator == null)
            {
                CacheAnimationDrivers();
            }

            if (playerAnimationDriver != null)
            {
                playerAnimationDriver.TriggerHit();
                return true;
            }

            if (enemyAnimationDriver != null)
            {
                enemyAnimationDriver.TriggerHit();
                return true;
            }

            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetTrigger(HitTriggerParameter);
                return true;
            }

            return false;
        }

        /// <summary>
        /// テスト用フィードバック受信オブジェクト設定。
        /// </summary>
        public void SetFeedbackReceivers(
            IKnockbackReceiver knockback = null,
            HitFlashReceiver flash = null,
            PlayerAnimationDriver playerDriver = null,
            EnemyAnimationDriver enemyDriver = null,
            Animator animator = null)
        {
            if (knockback != null) knockbackReceiver = knockback;
            if (flash != null) flashReceiver = flash;
            if (playerDriver != null) playerAnimationDriver = playerDriver;
            if (enemyDriver != null) enemyAnimationDriver = enemyDriver;
            if (animator != null) targetAnimator = animator;
        }

        /// <summary>有効かつアクティブなゲームオブジェクトだけを戦闘候補にします。</summary>
        public bool IsAvailableForCombat => isActiveAndEnabled && gameObject.activeInHierarchy;

        /// <summary>登録前に必要な安定識別子の契約を満たすかを返します。</summary>
        public bool IsIdentityValid => !string.IsNullOrWhiteSpace(combatantId);

        /// <summary>登録前に識別子の契約を日本語診断で検証します。</summary>
        public bool TryValidateIdentity(out string diagnostic)
        {
            if (IsIdentityValid)
            {
                diagnostic = string.Empty;
                return true;
            }

            diagnostic = "戦闘対象IDが設定されていません。";
            return false;
        }

        private void Reset()
        {
            hitLayer = gameObject.layer;
        }

        /// <summary>識別情報を設定します。</summary>
        internal void SetIdentity(CombatantFaction newFaction, string newCombatantId)
        {
            faction = newFaction;
            combatantId = newCombatantId;
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(combatantId))
            {
                combatantId = "Knight";
            }

            hitLayer = Mathf.Max(0, hitLayer);
        }
    }
}
