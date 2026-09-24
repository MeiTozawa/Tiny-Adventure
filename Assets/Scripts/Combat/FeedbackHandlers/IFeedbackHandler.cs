namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットフィードバックパイプラインの個別処理ハンドラー契約。
    /// 既存の ICombatFeedbackModule と完全な互換性を持ち、纯 C# クラスとして合成可能です。
    /// </summary>
    public interface IFeedbackHandler : ICombatFeedbackModule
    {
    }
}
