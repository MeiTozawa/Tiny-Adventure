namespace TinyAdventure
{
    /// <summary>
    /// Demo 全体の進行状態を表します。状態遷移の書き込みは GameFlowController に限定します。
    /// </summary>
    public enum GameplayState
    {
        Boot,
        Running,
        Victory,
        Defeat,
        Restarting
    }
}
