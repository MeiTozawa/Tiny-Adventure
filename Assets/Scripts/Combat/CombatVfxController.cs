using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗打击 VFX 特效控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVfxController : MonoBehaviour, ICombatFeedbackModule
    {
        public void Play(CombatFeedbackRequest request)
        {
        }

        public void ClearRuntimeState()
        {
        }
    }
}
