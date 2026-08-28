using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TinyAdventure
{
    /// <summary>
    /// ゲームプレイ入力を機器に依存しない形で表します。
    /// </summary>
    public readonly struct GameplayInputSnapshot
    {
        public GameplayInputSnapshot(Vector2 move, Vector2 look, bool attackPressed, bool restartPressed, bool exitPressed)
        {
            Move = move;
            Look = look;
            AttackPressed = attackPressed;
            RestartPressed = restartPressed;
            ExitPressed = exitPressed;
        }

        public Vector2 Move { get; }
        public Vector2 Look { get; }
        public bool AttackPressed { get; }
        public bool RestartPressed { get; }
        public bool ExitPressed { get; }
    }

    /// <summary>
    /// Gameplayアクションを有効化し、後続システムへ統一入力スナップショットを提供します。
    /// 入力アクションはcom.unity.inputsystemが生成した強型付きクラス（Assets/Scripts/Input/InputSystem.cs、
    /// 元データはAssets/InputSystem.inputactions）から取得します。
    /// </summary>
    public sealed class InputReader : MonoBehaviour, IDisposable
    {
        private global::InputSystem gameplayActions;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction attackAction;
        private InputAction restartAction;
        private InputAction exitAction;
        private bool diagnosticReported;
        private bool ownsMapEnable;

        public event Action<string> DiagnosticReported;

        public bool IsReady { get; private set; }
        public string LastDiagnostic { get; private set; }

        private void OnEnable()
        {
            TryInitialize();
        }

        private void OnDisable()
        {
            IsReady = false;
            if (ownsMapEnable && gameplayActions != null)
            {
                gameplayActions.Gameplay.Disable();
                ownsMapEnable = false;
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }

        /// <summary>
        /// 生成された入力アクションクラスが保持するリソースを解放します。
        /// </summary>
        public void Dispose()
        {
            if (gameplayActions == null)
            {
                return;
            }

            if (ownsMapEnable)
            {
                gameplayActions.Gameplay.Disable();
                ownsMapEnable = false;
            }

            gameplayActions.Dispose();
            gameplayActions = null;
            IsReady = false;
        }

        /// <summary>
        /// 現在フレームの入力を読み取ります。ボタンは押下されたフレームだけtrueになります。
        /// </summary>
        public GameplayInputSnapshot ReadSnapshot()
        {
            if (!TryInitialize())
            {
                return default;
            }

            return new GameplayInputSnapshot(
                moveAction.ReadValue<Vector2>(),
                lookAction.ReadValue<Vector2>(),
                attackAction.WasPressedThisFrame(),
                restartAction.WasPressedThisFrame(),
                exitAction.WasPressedThisFrame());
        }

        /// <summary>
        /// 読み取り前に生成済みアクションクラスを初期化し、必要なアクションマップを有効化します。
        /// </summary>
        public bool TryInitialize()
        {
            if (IsReady)
            {
                return true;
            }

            try
            {
                gameplayActions ??= new global::InputSystem();
            }
            catch (Exception exception)
            {
                return ReportFailure($"生成された入力アクションクラスの初期化に失敗しました。Assets/InputSystem.inputactionsを確認してください。詳細: {exception.Message}");
            }

            InputSystem.GameplayActions gameplay = gameplayActions.Gameplay;
            moveAction = gameplay.Move;
            lookAction = gameplay.Look;
            attackAction = gameplay.Attack;
            restartAction = gameplay.Restart;
            exitAction = gameplay.Exit;

            if (moveAction == null)
            {
                return ReportFailure("Gameplayアクション「Move」が見つかりません。移動入力を設定してください。");
            }

            if (lookAction == null)
            {
                return ReportFailure("Gameplayアクション「Look」が見つかりません。カメラ入力を設定してください。");
            }

            if (attackAction == null)
            {
                return ReportFailure("Gameplayアクション「Attack」が見つかりません。攻撃入力を設定してください。");
            }

            if (restartAction == null)
            {
                return ReportFailure("Gameplayアクション「Restart」が見つかりません。再開入力を設定してください。");
            }

            if (exitAction == null)
            {
                return ReportFailure("Gameplayアクション「Exit」が見つかりません。終了入力を設定してください。");
            }

            if (!gameplay.Get().enabled)
            {
                gameplay.Enable();
                ownsMapEnable = true;
            }

            IsReady = true;
            return true;
        }

        private bool ReportFailure(string message)
        {
            IsReady = false;
            LastDiagnostic = message;
            if (!diagnosticReported)
            {
                diagnosticReported = true;
                Debug.LogError($"[入力診断] {message}", this);
                DiagnosticReported?.Invoke(message);
            }

            return false;
        }
    }
}
