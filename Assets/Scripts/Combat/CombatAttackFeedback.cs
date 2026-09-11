using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 攻击动作表现控制器。
    /// 监听 PlayerCombatController 与 EnemyMeleeCombat 的攻击时间轴，
    /// 在挥刀时播放 SFX_Sword_Whoosh..mp3 并激活剑光 SwordTrailController。
    /// 明确支持空挥动作表现，绝不将攻击动作事件越权作为伤害或受击命中判定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAttackFeedback : MonoBehaviour
    {
        [Header("服务引用")]
        [SerializeField]
        private CombatAudioController audioController;

        [SerializeField]
        private SwordTrailController swordTrail;

        [Header("绑定的战斗控制器（选填，未填时自动查找）")]
        [SerializeField]
        private PlayerCombatController playerCombat;

        [SerializeField]
        private EnemyMeleeCombat enemyCombat;

        [SerializeField]
        private CombatantMarker ownerMarker;

        private bool isPlayerBound;
        private bool isEnemyBound;

        /// <summary>诊断通知。</summary>
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
        /// 绑定玩家攻击控制器。
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
        /// 绑定敌人近战攻击控制器。
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
        /// 测试用注入依赖。
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
        /// 攻击开始响应。
        /// 播放挥击音效并启动刀光。
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
        /// 攻击结束或取消响应。
        /// 关闭刀光。
        /// </summary>
        public void HandleAttackEnded(int sequenceId)
        {
            if (swordTrail != null)
            {
                swordTrail.EndTrail();
            }
        }

        /// <summary>
        /// 清理运行时表现状态。
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
                Debug.LogError($"[攻击表现诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[攻击表现诊断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
