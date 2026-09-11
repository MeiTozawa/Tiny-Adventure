using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 剑刃刀光轨迹控制器。
    /// 包装 TrailRenderer，在攻击挥击期间启用，攻击结束或取消时立即关闭。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwordTrailController : MonoBehaviour
    {
        [SerializeField]
        private TrailRenderer trailRenderer;

        public bool IsEmitting => trailRenderer != null && trailRenderer.emitting;

        private void Awake()
        {
            ResolveReferences();
        }

        public void BeginTrail(AttackFeedbackContext context)
        {
            ResolveReferences();
            if (trailRenderer != null)
            {
                trailRenderer.emitting = true;
            }
        }

        public void EndTrail()
        {
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
            }
        }

        public void ClearRuntimeState()
        {
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
                trailRenderer.Clear();
            }
        }

        private void ResolveReferences()
        {
            if (trailRenderer == null)
            {
                trailRenderer = GetComponentInChildren<TrailRenderer>(true);
            }
        }
    }
}
