using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// プラットフォーム終了要求のインターフェースです。
    /// </summary>
    public interface IApplicationExit
    {
        void RequestExit();
    }

    /// <summary>
    /// Editor専用の終了要求記録アダプターです。Unity Editorプロセスは終了させません。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EditorApplicationExit : MonoBehaviour, IApplicationExit
    {
        public bool WasExitRequested { get; private set; }
        public int RequestCount { get; private set; }

        public void RequestExit()
        {
            WasExitRequested = true;
            RequestCount++;
            Debug.Log("[終了診断] Editorで終了要求を記録しました。Unity Editorは終了しません。", this);
        }

        public void ClearRequest()
        {
            WasExitRequested = false;
            RequestCount = 0;
        }
    }

    /// <summary>
    /// 実行環境用のプラットフォーム終了アダプターです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeApplicationExit : MonoBehaviour, IApplicationExit
    {
        public bool WasExitRequested { get; private set; }

        public void RequestExit()
        {
            WasExitRequested = true;
            Application.Quit();
        }
    }
}
