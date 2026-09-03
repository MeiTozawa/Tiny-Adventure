using System;
using System.Collections.Generic;
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
        public bool IsGameplayMapEnabled => gameplayActions != null && gameplayActions.Gameplay.enabled;
        public bool IsAttackActionEnabled => attackAction != null && attackAction.enabled;
        public bool HasMouseAttackBinding { get; private set; }
        public string LastDiagnostic { get; private set; }

        private void OnEnable()
        {
            TryInitialize();
        }

        private void OnDisable()
        {
            IsReady = false;
            HasMouseAttackBinding = false;
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
            HasMouseAttackBinding = false;
        }

        /// <summary>
        /// Gameplay入力の必須アクションと、実行時の有効状態を検査します。
        /// この検査は既存の入力アセットを修正せず、失敗した項目をすべて日本語で返します。
        /// </summary>
        public bool ValidateRequiredActions(out IReadOnlyList<string> diagnostics)
        {
            var results = new System.Collections.Generic.List<string>();

            if (!isActiveAndEnabled)
            {
                results.Add("InputReaderが有効なシーンオブジェクトにありません。");
            }

            if (!TryInitialize())
            {
                if (!string.IsNullOrEmpty(LastDiagnostic))
                {
                    results.Add(LastDiagnostic);
                }
                else
                {
                    results.Add("Gameplay入力の初期化に失敗しました。Assets/InputSystem.inputactionsを確認してください。");
                }
            }
            else
            {
                if (!IsGameplayMapEnabled)
                {
                    results.Add("Gameplayアクションマップが有効になっていません。Play Modeの入力入口を確認してください。");
                }

                if (!IsAttackActionEnabled)
                {
                    results.Add("Gameplay/Attackアクションが有効になっていません。Play Modeの入力入口を確認してください。");
                }
            }

            diagnostics = results;
            foreach (string message in results)
            {
                ReportFailure(message);
            }

            return results.Count == 0;
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
            InputActionMap gameplayMap = gameplay.Get();
            moveAction = gameplay.Move;
            lookAction = gameplay.Look;
            attackAction = gameplay.Attack;
            restartAction = gameplay.Restart;
            exitAction = gameplay.Exit;

            if (gameplayMap == null || gameplayMap.name != "Gameplay")
            {
                return ReportFailure("Gameplayアクションマップが見つかりません。Assets/InputSystem.inputactionsのマップ名をGameplayにしてください。");
            }

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
                return ReportFailure("Gameplay/Attackアクションが見つかりません。攻撃入力を設定してください。");
            }

            if (restartAction == null)
            {
                return ReportFailure("Gameplayアクション「Restart」が見つかりません。再開入力を設定してください。");
            }

            if (exitAction == null)
            {
                return ReportFailure("Gameplayアクション「Exit」が見つかりません。終了入力を設定してください。");
            }

            HasMouseAttackBinding = HasBinding(attackAction, "<Mouse>/leftButton");
            if (!HasMouseAttackBinding)
            {
                return ReportFailure("Gameplay/Attackに<Mouse>/leftButtonバインドがありません。左クリック攻撃を設定してください。");
            }

            if (!gameplayMap.enabled)
            {
                gameplayMap.Enable();
                ownsMapEnable = true;
            }

            if (!gameplayMap.enabled)
            {
                return ReportFailure("Gameplayアクションマップが有効になっていません。Play Modeの入力入口を確認してください。");
            }

            if (!attackAction.enabled)
            {
                return ReportFailure("Gameplay/Attackアクションが有効になっていません。Play Modeの入力入口を確認してください。");
            }

            LastDiagnostic = string.Empty;
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

        private static bool HasBinding(InputAction action, string expectedPath)
        {
            if (action == null || string.IsNullOrEmpty(expectedPath))
            {
                return false;
            }

            for (int index = 0; index < action.bindings.Count; index++)
            {
                if (string.Equals(action.bindings[index].path, expectedPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
