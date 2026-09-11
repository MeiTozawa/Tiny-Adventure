using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗相机反馈控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatCameraFeedback : MonoBehaviour, ICombatFeedbackModule
    {
        public void Play(CombatFeedbackRequest request)
        {
        }

        public void ClearRuntimeState()
        {
        }
    }
}
