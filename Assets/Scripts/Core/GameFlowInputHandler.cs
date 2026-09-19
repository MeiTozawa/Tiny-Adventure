using System;

namespace TinyAdventure
{
    /// <summary>
    /// GameFlowの再開（Restart）および終了（Exit）入力判定を担う独立入力処理モジュールです。
    /// 単一責任：GameplayInputSnapshotから再開・終了のトリガー条件を判定し、コールバックを実行します。
    /// </summary>
    public sealed class GameFlowInputHandler
    {
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
            if (snapshot.RestartPressed && isTerminal)
            {
                onRestart?.Invoke();
            }

            if (snapshot.ExitPressed)
            {
                onExit?.Invoke();
            }
        }
    }
}
