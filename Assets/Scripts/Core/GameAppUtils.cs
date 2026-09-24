using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// ゲームアプリケーション共通ユーティリティ。
    /// 冗長なアダプタークラス（ApplicationExitAdapters等）を廃止し、
    /// エディタとビルド実行環境の差異を吸収して安全に終了を実行します。
    /// </summary>
    public static class GameAppUtils
    {
        /// <summary>
        /// アプリケーション終了を要求します。
        /// エディタ再生中であれば再生を停止し、スタンドアロンビルドであれば Application.Quit() を呼び出します。
        /// </summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
