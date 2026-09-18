using System;
using UnityEngine;

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
        [Header("サービス参照")]
        [SerializeField]
        private CombatAudioController audioController;

        [SerializeField]
        private SwordTrailController swordTrail;

        [Header("バインド対象戦闘コントローラー（任意、未設定時は自動検索）")]
        [SerializeField]
        private PlayerCombatController playerCombat;

        [SerializeField]
        private EnemyMeleeCombat enemyCombat;

        [SerializeField]
        private CombatantMarker ownerMarker;

        private bool isPlayerBound;
        private bool isEnemyBound;

        /// <summary>診断メッセージ通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
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
                ownerMarker = enemyCombat.GetComponent<CombatantMarker>();
                BindEnemyEvents();
            }
        }

        /// <summary>
        /// テスト用の差し替え依存関係を設定します。
        /// </summary>
        public void ConfigureForTests(
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
            LastDiagnostic = string.Empty;
        }

        private void ResolveReferences()
        {
            if (audioController == null)
            {
                audioController = GetComponentInChildren<CombatAudioController>(true) ?? FindAnyObjectByType<CombatAudioController>();
            }

            if (swordTrail == null)
            {
                swordTrail = GetComponentInChildren<SwordTrailController>(true);
            }

            if (playerCombat == null)
            {
                playerCombat = GetComponent<PlayerCombatController>() ?? GetComponentInParent<PlayerCombatController>();
            }

            if (enemyCombat == null)
            {
                enemyCombat = GetComponent<EnemyMeleeCombat>() ?? GetComponentInParent<EnemyMeleeCombat>();
            }

            if (ownerMarker == null)
            {
                ownerMarker = GetComponent<CombatantMarker>() ?? GetComponentInParent<CombatantMarker>();
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

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[攻撃演出診断] {message}", this);
            }
            else
            {
                Debug.Log($"[攻撃演出診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
