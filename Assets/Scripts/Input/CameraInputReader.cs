using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 第三人称カメラが必要とする観察入力を提供します。
    /// </summary>
    public readonly struct CameraInputSnapshot
    {
        public CameraInputSnapshot(Vector2 look)
        {
            Look = look;
        }

        public Vector2 Look { get; }
    }

    /// <summary>
    /// InputReaderを共有し、カメラ用のLook入力を機器に依存しない形で公開します。
    /// </summary>
    public sealed class CameraInputReader : MonoBehaviour
    {
        [SerializeField]
        private InputReader inputReader;

        public bool IsReady => inputReader != null && inputReader.IsReady;
        public string LastDiagnostic => string.Empty;

        [Inject]
        public void Construct(InputReader input = null)
        {
            if (input != null) inputReader = input;
        }

        private void Awake()
        {
        }

        /// <summary>
        /// 現在フレームのカメラ観察入力を読み取ります。
        /// </summary>
        public CameraInputSnapshot ReadSnapshot()
        {
            if (inputReader == null || !inputReader.IsReady)
            {
                return default;
            }

            GameplayInputSnapshot gameplayInput = inputReader.ReadSnapshot();
            return new CameraInputSnapshot(gameplayInput.Look);
        }

        /// <summary>
        /// カメラの水平・垂直観察入力だけを取得します。
        /// </summary>
        public Vector2 ReadLook()
        {
            return ReadSnapshot().Look;
        }
    }
}
