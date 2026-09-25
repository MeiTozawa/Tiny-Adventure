using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称武器表现层（Viewmodel）抽象接口。
    /// 允许战斗与控制逻辑驱动视口武器表现，而无需强依赖具体表现层实现。
    /// </summary>
    public interface IPlayerViewmodel
    {
        bool IsAttacking { get; }
        bool IsActiveAndEnabled { get; }
        Result<float> GetAttackNormalizedTime();
        void TriggerAttack(int comboIndex, float speedMultiplier, float strikeOpen, float strikeClose);
        void TriggerAttack(int comboIndex, float speedMultiplier);
        void CancelAttack();
        void SetMovementState(bool moving, float speedFactor = 1f);
        void ApplyLookInput(Vector2 lookDelta);
        void TriggerImpactJolt(Vector3 localDirection, float intensity = 1f);
    }
}
