using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗打击音频控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAudioController : MonoBehaviour, ICombatFeedbackModule
    {
        public void Play(CombatFeedbackRequest request)
        {
        }

        public void ClearRuntimeState()
        {
        }
    }
}
