using System;

namespace TinyAdventure
{
    /// <summary>
    /// GameFlowの再開（Restart）および終了（Exit）入力判定を担う独立入力処理モジュールです。
    /// </summary>
    public sealed class GameFlowInputHandler
    {
        public enum FlowAction
        {
            None = 0,
            Restart = 1,
            Exit = 2
        }

        /// <summary>
        /// フレームごとの入力を読み取り、実行すべき GameFlow アクションを返します（ゼロGC割当）。
        /// </summary>
        public FlowAction EvaluateFrameInput(InputReader inputReader, bool isTerminal)
        {
            if (inputReader == null)
            {
                return FlowAction.None;
            }

            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            return EvaluateInput(snapshot, isTerminal);
        }

        /// <summary>
        /// 入力スナップショットを評価し、実行すべき GameFlow アクションを返します（ゼロGC割当）。
        /// </summary>
        public FlowAction EvaluateInput(GameplayInputSnapshot snapshot, bool isTerminal)
        {
            if (snapshot.RestartPressed && isTerminal)
            {
                return FlowAction.Restart;
            }

            if (snapshot.ExitPressed)
            {
                return FlowAction.Exit;
            }

            return FlowAction.None;
        }

        /// <summary>
        /// フレームごとの入力を読み取り、再開または終了の条件を満たせばコールバックを実行します。
        /// </summary>
        public void ProcessFrameInput(InputReader inputReader, bool isTerminal, Action onRestart, Action onExit)
        {
            if (inputReader == null)
            {
                return;
            }

            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            ProcessInput(snapshot, isTerminal, onRestart, onExit);
        }

        /// <summary>
        /// 入力スナップショットを評価し、再開または終了の条件を満たせばコールバックを実行します。
        /// </summary>
        public void ProcessInput(GameplayInputSnapshot snapshot, bool isTerminal, Action onRestart, Action onExit)
        {
            FlowAction action = EvaluateInput(snapshot, isTerminal);
            if (action == FlowAction.Restart)
            {
                onRestart?.Invoke();
            }
            else if (action == FlowAction.Exit)
            {
                onExit?.Invoke();
            }
        }
    }
}
