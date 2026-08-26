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
    /// </summary>
    public sealed class InputReader : MonoBehaviour
    {
        private const string DefaultActionMapName = "Gameplay";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string AttackActionName = "Attack";
        private const string RestartActionName = "Restart";
        private const string ExitActionName = "Exit";

        [SerializeField]
        private InputActionAsset inputActions;

        [SerializeField]
        private string actionMapName = DefaultActionMapName;

        private InputActionMap gameplayMap;
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
        public InputActionAsset InputActions => inputActions;
        public string ActionMapName => actionMapName;

        private void OnEnable()
        {
            TryInitialize();
        }

        private void OnDisable()
        {
            IsReady = false;
            if (ownsMapEnable && gameplayMap != null)
            {
                gameplayMap.Disable();
                ownsMapEnable = false;
            }
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
        /// 読み取り前にアクション構成を検証し、必要なアクションマップを有効化します。
        /// </summary>
        public bool TryInitialize()
        {
            if (IsReady)
            {
                return true;
            }

            if (inputActions == null)
            {
                return ReportFailure("入力アセットが設定されていません。InputSystem.inputactionsを割り当ててください。");
            }

            string requestedMapName = string.IsNullOrWhiteSpace(actionMapName)
                ? DefaultActionMapName
                : actionMapName;
            gameplayMap = inputActions.FindActionMap(requestedMapName, false);
            if (gameplayMap == null)
            {
                return ReportFailure($"入力アクションマップ「{requestedMapName}」が見つかりません。Gameplayマップを確認してください。");
            }

            moveAction = gameplayMap.FindAction(MoveActionName, false);
            lookAction = gameplayMap.FindAction(LookActionName, false);
            attackAction = gameplayMap.FindAction(AttackActionName, false);
            restartAction = gameplayMap.FindAction(RestartActionName, false);
            exitAction = gameplayMap.FindAction(ExitActionName, false);

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

            if (!gameplayMap.enabled)
            {
                gameplayMap.Enable();
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
