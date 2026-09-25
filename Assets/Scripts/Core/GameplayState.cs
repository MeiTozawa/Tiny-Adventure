using System;

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

    /// <summary>
    /// ゲーム進行状態の読み取り契約。
    /// </summary>
    public interface IGameplayStateProvider
    {
        GameplayState CurrentState { get; }
        bool IsTerminal { get; }
        event Action<GameplayState> StateChanged;
    }
}
