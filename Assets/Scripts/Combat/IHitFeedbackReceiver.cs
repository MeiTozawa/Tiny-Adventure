namespace TinyAdventure
{
    /// <summary>
    /// ダメージ発生時に受撃演出・フィードバックを直接受け取る契約です。
    /// </summary>
    public interface IHitFeedbackReceiver
    {
        void OnHitFeedbackRequested(CombatantMarker target, DamageRequest damage);
    }
}
