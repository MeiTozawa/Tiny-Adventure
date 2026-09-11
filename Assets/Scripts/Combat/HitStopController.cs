using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗打击顿挫（Hit Stop）控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopController : MonoBehaviour, ICombatFeedbackModule
    {
        public void Play(CombatFeedbackRequest request)
        {
        }

        public void ClearRuntimeState()
        {
        }
    }
}
