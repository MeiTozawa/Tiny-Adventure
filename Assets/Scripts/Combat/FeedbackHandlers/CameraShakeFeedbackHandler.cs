using UnityEngine;
using Unity.Cinemachine;

namespace TinyAdventure
{
    /// <summary>
    /// Cinemachine Impulse によるカメラシェイクを生成する純 C# フィードバックハンドラー。
    /// </summary>
    public sealed class CameraShakeFeedbackHandler : ICombatFeedbackModule
    {
        private readonly CinemachineImpulseSource impulseSource;
        private readonly CombatFeedbackProfile profile;

        public CameraShakeFeedbackHandler(CinemachineImpulseSource impulseSource, CombatFeedbackProfile profile)
        {
            this.impulseSource = impulseSource;
            this.profile = profile;
        }

        public void Play(CombatFeedbackRequest request)
        {
            ImpulseFeedbackSettings settings = request.HitType == CombatHitType.Lethal
                ? profile.LethalHit.impulse
                : profile.NormalHit.impulse;

            Vector3 impulseVelocity = (request.Direction != Vector3.zero ? request.Direction : Vector3.down) * settings.amplitude;
            impulseSource.GenerateImpulseWithVelocity(impulseVelocity);
        }

        public void ClearRuntimeState()
        {
        }
    }
}
