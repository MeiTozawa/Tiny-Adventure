using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 攻撃演出コントローラー。
    /// PlayerCombatController および EnemyMeleeCombat の攻撃タイムラインを監視し、
    /// 剣撃時に SFX_Sword_Whoosh..mp3 を再生し、軌跡 SwordTrailController を有効化します。
    /// 空振りの演出にも対応し、ダメージや命中判定への越境は行いません。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAttackFeedback : MonoBehaviour
    {
        private CombatAudioController audioController;

        [SerializeField]
        private SwordTrailController swordTrail;

        private PlayerCombatController playerCombat;
        private EnemyMeleeCombat enemyCombat;
        private CombatantMarker ownerMarker;

        private bool isPlayerBound;
        private bool isEnemyBound;

        [Inject]
        public void Construct(CombatAudioController audio = null)
        {
            if (audio != null) audioController = audio;
        }

        private void Awake()
        {
            if (playerCombat == null) playerCombat = GetComponent<PlayerCombatController>();
            if (enemyCombat == null) enemyCombat = GetComponent<EnemyMeleeCombat>();
            if (ownerMarker == null) ownerMarker = GetComponent<CombatantMarker>();
        }

        private void OnEnable()
        {
            BindEvents();
        }

        private void OnDisable()
        {
            UnbindEvents();
            ClearRuntimeState();
        }

        /// <summary>
        /// プレイヤー攻撃コントローラーをバインドします。
        /// </summary>
        public void BindPlayer(PlayerCombatController controller)
        {
            UnbindPlayer();
            playerCombat = controller;
            if (playerCombat != null)
            {
                ownerMarker = playerCombat.CombatantMarker;
                BindPlayerEvents();
            }
        }

        /// <summary>
        /// 敵近接攻撃コントローラーをバインドします。
        /// </summary>
        public void BindEnemy(EnemyMeleeCombat controller)
        {
            UnbindEnemy();
            enemyCombat = controller;
            if (enemyCombat != null)
            {
                ownerMarker = enemyCombat.CombatantMarker;
                BindEnemyEvents();
            }
        }

        /// <summary>
        /// 依存関係を設定します。
        /// </summary>
        public void Construct(
            CombatAudioController audio,
            SwordTrailController trail,
            CombatantMarker marker = null)
        {
            audioController = audio;
            swordTrail = trail;
            ownerMarker = marker;
        }

        /// <summary>
        /// 攻撃開始ハンドラー。
        /// 剣撃音を再生し、軌跡を開始します。
        /// </summary>
        public void HandleAttackStarted(int sequenceId)
        {
            Vector3 pos = ownerMarker != null ? ownerMarker.transform.position : transform.position;
            var context = new AttackFeedbackContext(ownerMarker, sequenceId, pos);

            if (audioController != null)
            {
                audioController.PlayWhoosh(context);
            }

            if (swordTrail != null)
            {
                swordTrail.BeginTrail(context);
            }
        }

        /// <summary>
        /// 攻撃終了またはキャンセルハンドラー。
        /// 軌跡を終了します。
        /// </summary>
        public void HandleAttackEnded(int sequenceId)
        {
            if (swordTrail != null)
            {
                swordTrail.EndTrail();
            }
        }

        /// <summary>
        /// ランタイム演出状態をクリアします。
        /// </summary>
        public void ClearRuntimeState()
        {
            if (swordTrail != null)
            {
                swordTrail.ClearRuntimeState();
            }
        }


        private void BindEvents()
        {
            BindPlayerEvents();
            BindEnemyEvents();
        }

        private void UnbindEvents()
        {
            UnbindPlayer();
            UnbindEnemy();
        }

        private void BindPlayerEvents()
        {
            if (playerCombat != null && !isPlayerBound)
            {
                playerCombat.AttackSequenceStarted += HandleAttackStarted;
                playerCombat.AttackSequenceCompleted += HandleAttackEnded;
                playerCombat.AttackSequenceCancelled += HandleAttackEnded;
                isPlayerBound = true;
            }
        }

        private void UnbindPlayer()
        {
            if (playerCombat != null && isPlayerBound)
            {
                playerCombat.AttackSequenceStarted -= HandleAttackStarted;
                playerCombat.AttackSequenceCompleted -= HandleAttackEnded;
                playerCombat.AttackSequenceCancelled -= HandleAttackEnded;
                isPlayerBound = false;
            }
        }

        private void BindEnemyEvents()
        {
            if (enemyCombat != null && !isEnemyBound)
            {
                enemyCombat.AttackStarted += HandleAttackStarted;
                enemyCombat.AttackCompleted += HandleAttackEnded;
                enemyCombat.AttackCancelled += HandleAttackEnded;
                isEnemyBound = true;
            }
        }

        private void UnbindEnemy()
        {
            if (enemyCombat != null && isEnemyBound)
            {
                enemyCombat.AttackStarted -= HandleAttackStarted;
                enemyCombat.AttackCompleted -= HandleAttackEnded;
                enemyCombat.AttackCancelled -= HandleAttackEnded;
                isEnemyBound = false;
            }
        }


    }
}
