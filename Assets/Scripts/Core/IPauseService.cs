using System;

namespace TinyAdventure
{
    /// <summary>
    /// ポーズを要求した発生源（優先度・原因識別用）。
    /// </summary>
    public enum PauseSource
    {
        Menu,
        Victory,
        Defeat,
        Cutscene
    }

    /// <summary>
    /// マルチソース対応のゲーム一時停止（ポーズ）管理サービス。
    /// 複数のポーズ要求（メニュー、勝利、敗北等）をカウント管理し、
    /// カーソルロック、ゲームプレイ入力の競合を防止します。
    /// </summary>
    public interface IPauseService
    {
        /// <summary>現在一時停止中かどうか。</summary>
        bool IsPaused { get; }

        /// <summary>ポーズ状態変化イベント（true: ポーズ開始, false: ポーズ解除）。</summary>
        event Action<bool> PauseStateChanged;

        /// <summary>指定された発生源からポーズを要求し、解放用トークンを返します。</summary>
        IDisposable RequestPause(PauseSource source);

        /// <summary>指定された発生源のポーズ要求を明示的に解除します。</summary>
        void ReleasePause(PauseSource source);

        /// <summary>指定された発生源が現在ポーズを要求中かどうかを返します。</summary>
        bool HasPauseSource(PauseSource source);

        /// <summary>すべてのポーズ要求を一括クリアし、通常速度（1f）とゲームプレイ入力へ復帰させます。</summary>
        void ClearAllPauses();
    }
}
