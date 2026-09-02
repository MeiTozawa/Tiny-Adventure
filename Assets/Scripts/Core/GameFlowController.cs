using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// プレイヤー戦闘が参照するゲーム進行状態の最小実体です。
    /// 完全な初期化と勝敗処理はGameFlowタスクで拡張し、状態の読み取り契約はここで固定します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowController : MonoBehaviour, IGameplayStateProvider
    {
        [SerializeField]
        private GameplayState initialState = GameplayState.Running;

        public GameplayState CurrentState { get; private set; } = GameplayState.Boot;

        public bool IsTerminal => CurrentState == GameplayState.Victory || CurrentState == GameplayState.Defeat;

        public event Action<GameplayState> StateChanged;

        private void Awake()
        {
            CurrentState = initialState;
        }

        /// <summary>
        /// フローの状態を書き換えます。プレイヤーや敵はこのメソッドを直接呼ばず、状態を読み取ります。
        /// </summary>
        public bool TrySetState(GameplayState nextState)
        {
            if (CurrentState == nextState)
            {
                return false;
            }

            CurrentState = nextState;
            StateChanged?.Invoke(nextState);
            return true;
        }
    }
}
