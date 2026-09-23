using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ユニットの陣営、安定した識別子、命中レイヤーを保持します。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
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
        public HealthComponent Health => healthComponent;
        public IKnockbackReceiver KnockbackReceiver => knockbackReceiver;
        public HitFlashReceiver FlashReceiver => flashReceiver;

        public void SetDependencies(
            HealthComponent health = null,
            IKnockbackReceiver knockback = null,
            HitFlashReceiver flash = null,
            PlayerAnimationDriver playerAnim = null,
            EnemyAnimationDriver enemyAnim = null,
            Animator anim = null)
        {
            if (health != null) healthComponent = health;
            if (knockback != null) knockbackReceiver = knockback;
            if (flash != null) flashReceiver = flash;
            if (playerAnim != null) playerAnimationDriver = playerAnim;
            if (enemyAnim != null) enemyAnimationDriver = enemyAnim;
            if (anim != null) targetAnimator = anim;
        }

        private void Awake()
        {
            healthComponent = GetComponent<HealthComponent>();
            knockbackReceiver = GetComponent<IKnockbackReceiver>();
            flashReceiver = GetComponent<HitFlashReceiver>();
            playerAnimationDriver = GetComponentInChildren<PlayerAnimationDriver>();
            enemyAnimationDriver = GetComponentInChildren<EnemyAnimationDriver>();
            targetAnimator = GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// 被弾アニメーションを駆動します。PlayerAnimationDriver、EnemyAnimationDriver、Animator の順で実行します。
        /// </summary>
        public Result TriggerHitAnimation()
        {
            if (playerAnimationDriver != null)
            {
                playerAnimationDriver.TriggerHit();
                return Result.Ok();
            }

            if (enemyAnimationDriver != null)
            {
                enemyAnimationDriver.TriggerHit();
                return Result.Ok();
            }

            if (targetAnimator != null && targetAnimator.runtimeAnimatorController != null)
            {
                targetAnimator.SetTrigger(HitTriggerParameter);
                return Result.Ok();
            }

            return GameError.InvalidState;
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

        /// <summary>登録前に識別子の契約を検証します。</summary>
        public Result ValidateIdentity()
        {
            return IsIdentityValid ? Result.Ok() : GameError.InvalidParameter;
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
