using System;
using UnityEngine;

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

        private bool diagnosticReported;

        public event Action<string> DiagnosticReported;

        public bool IsReady => inputReader != null && inputReader.IsReady;
        public string LastDiagnostic { get; private set; }

        private void Awake()
        {
            if (inputReader == null)
            {
                inputReader = GetComponent<InputReader>();
            }

            if (inputReader == null)
            {
                inputReader = FindAnyObjectByType<InputReader>();
            }

            if (inputReader == null)
            {
                ReportFailure("カメラ入力リーダーにInputReaderが設定されていません。");
            }
        }

        private void OnEnable()
        {
            if (inputReader != null)
            {
                inputReader.DiagnosticReported += HandleInputDiagnostic;
            }
        }

        private void OnDisable()
        {
            if (inputReader != null)
            {
                inputReader.DiagnosticReported -= HandleInputDiagnostic;
            }
        }

        /// <summary>
        /// 現在フレームのカメラ観察入力を読み取ります。
        /// </summary>
        public CameraInputSnapshot ReadSnapshot()
        {
            if (inputReader == null)
            {
                ReportFailure("カメラ入力リーダーにInputReaderが設定されていません。");
                return default;
            }

            GameplayInputSnapshot gameplayInput = inputReader.ReadSnapshot();
            if (!inputReader.IsReady)
            {
                return default;
            }

            return new CameraInputSnapshot(gameplayInput.Look);
        }

        /// <summary>
        /// カメラの水平・垂直観察入力だけを取得します。
        /// </summary>
        public Vector2 ReadLook()
        {
            return ReadSnapshot().Look;
        }

        private void HandleInputDiagnostic(string message)
        {
            ReportFailure(message);
        }

        private void ReportFailure(string message)
        {
            LastDiagnostic = message;
            if (diagnosticReported)
            {
                return;
            }

            diagnosticReported = true;
            Debug.LogError($"[カメラ入力診断] {message}", this);
            DiagnosticReported?.Invoke(message);
        }
    }
}
