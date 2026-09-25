namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットストップ（Hit Stop）中央コントローラーのインターフェース。
    /// </summary>
    public interface IHitStopController : ICombatFeedbackModule
    {
        bool IsActive { get; }
        float RemainingUnscaledSeconds { get; }

        void RegisterParticipant(IHitStopParticipant participant);
        void UnregisterParticipant(IHitStopParticipant participant);
        void Tick();
    }
}
