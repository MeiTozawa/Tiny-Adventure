namespace TinyAdventure
{
    /// <summary>
    /// ヒットストップ（局所的時間停止）を制御するフィードバックハンドラー。
    /// HitStopController に委譲し、Pipeline 経由で実行します。
    /// </summary>
    public sealed class HitStopFeedbackHandler : ICombatFeedbackModule
    {
        private readonly HitStopController hitStopController;

        public HitStopFeedbackHandler(HitStopController hitStopController)
        {
            this.hitStopController = hitStopController;
        }

        public void Play(CombatFeedbackRequest request)
        {
            hitStopController?.Play(request);
        }

        public void ClearRuntimeState()
        {
            hitStopController?.ClearRuntimeState();
        }
    }
}
