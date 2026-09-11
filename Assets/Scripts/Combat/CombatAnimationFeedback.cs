using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗受击动画反馈模块。
    /// 负责在受击时驱动目标 Animator 触发受击动画。普通命中触发 TriggerHit，
    /// 致死命中交由角色死亡生命周期处理，不触发受击动画与重复死亡动画。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAnimationFeedback : MonoBehaviour, ICombatAnimationFeedback, ICombatFeedbackModule
    {
        private static readonly int HitTriggerParameter = Animator.StringToHash("HitTrigger");

        /// <summary>动画反馈诊断通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;

        /// <summary>
        /// 统一反馈模块执行入口。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            if (request.HitType == CombatHitType.Lethal)
            {
                PlayLethalHit(request);
            }
            else
            {
                PlayNormalHit(request);
            }
        }

        /// <summary>
        /// 播放普通受击动画。
        /// 优先查找 PlayerAnimationDriver 与 EnemyAnimationDriver，其次回退到直接设置 Animator HitTrigger。
        /// </summary>
        public void PlayNormalHit(CombatFeedbackRequest request)
        {
            var target = request.Target;
            if (target == null)
            {
                ReportDiagnostic("受击目标为 null，跳过普通受击动画。", false);
                return;
            }

            var playerDriver = target.GetComponent<PlayerAnimationDriver>() ?? target.GetComponentInParent<PlayerAnimationDriver>() ?? target.GetComponentInChildren<PlayerAnimationDriver>();
            if (playerDriver != null)
            {
                playerDriver.TriggerHit();
                return;
            }

            var enemyDriver = target.GetComponent<EnemyAnimationDriver>() ?? target.GetComponentInParent<EnemyAnimationDriver>() ?? target.GetComponentInChildren<EnemyAnimationDriver>();
            if (enemyDriver != null)
            {
                enemyDriver.TriggerHit();
                return;
            }

            var animator = target.GetComponent<Animator>() ?? target.GetComponentInParent<Animator>() ?? target.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetTrigger(HitTriggerParameter);
                return;
            }

            ReportDiagnostic($"目标「{target.CombatantId}」未找到 PlayerAnimationDriver、EnemyAnimationDriver 或有效 Animator，跳过受击动画。", false);
        }

        /// <summary>
        /// 响应致死受击。
        /// 依据打击感规范，致死命中不触发 TriggerHit（避免打断死亡过渡），亦不主动触发 TriggerDeath（由现有 Health/Lifecycle 负责）。
        /// </summary>
        public void PlayLethalHit(CombatFeedbackRequest request)
        {
            // 致死命中不调用普通 TriggerHit，也不越权触发 TriggerDeath
            LastDiagnostic = string.Empty;
        }

        /// <summary>
        /// 清理运行时状态。
        /// </summary>
        public void ClearRuntimeState()
        {
            LastDiagnostic = string.Empty;
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[动画反馈诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[动画反馈诊断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
