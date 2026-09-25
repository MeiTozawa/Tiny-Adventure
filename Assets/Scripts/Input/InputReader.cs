using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace TinyAdventure {
    /// <summary>
    /// ゲームプレイ入力を機器に依存しない形で表します。
    /// </summary>
    public readonly struct GameplayInputSnapshot {
        /// <param name="attackHeld">攻撃ボタンが押し続けられているか（IsPressed）。省略時はattackPressedと同値。</param>
        public GameplayInputSnapshot(Vector2 move, Vector2 look, bool attackPressed, bool restartPressed,
            bool exitPressed, bool attackHeld = false)
        {
            Move = move;
            Look = look;
            AttackPressed = attackPressed;
            AttackHeld = attackHeld;
            RestartPressed = restartPressed;
            ExitPressed = exitPressed;
        }

        public Vector2 Move { get; }
        public Vector2 Look { get; }

        /// <summary>攻撃ボタンが押下されたフレームだけtrueになります（WasPressedThisFrame）。</summary>
        public bool AttackPressed { get; }

        /// <summary>攻撃ボタンが押し続けられている間trueになります（IsPressed）。</summary>
        public bool AttackHeld { get; }

        public bool RestartPressed { get; }
        public bool ExitPressed { get; }
    }

    /// <summary>
    /// Gameplayアクションを有効化し、後続システムへ統一入力スナップショットを提供します。
    /// 入力アクションはcom.unity.inputsystemが生成した強型付きクラス（Assets/Scripts/Input/InputSystem.cs、
    /// 元データはAssets/InputSystem.inputactions）から取得します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputReader : MonoBehaviour, IDisposable {
        private global::InputSystem gameplayActions;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction attackAction;
        private InputAction restartAction;
        private InputAction exitAction;
        public bool IsGameplayMapEnabled => gameplayActions.Gameplay.enabled;
        public bool IsAttackActionEnabled => attackAction.enabled;
        public bool HasMouseAttackBinding { get; private set; }
        private bool isGameplayInputEnabled = true;
        public bool IsGameplayInputEnabled => isGameplayInputEnabled;

        public void SetGameplayInputEnabled(bool enabled)
        {
            isGameplayInputEnabled = enabled;
        }

        private void Awake()
        {
            SetupActions();
            PauseService.Instance.SetInputReader(this);
        }

        private void OnEnable()
        {
            if (gameplayActions == null)
            {
                SetupActions();
            }

            gameplayActions!.Gameplay.Enable();

            Assert.IsTrue(gameplayActions.Gameplay.enabled, "Gameplayアクションマップが有効になっていません。Play Modeの入力入口を確認してください。");
            Assert.IsTrue(attackAction.enabled, "Gameplay/Attackアクションが有効になっていません。Play Modeの入力入口を確認してください。");
        }

        private void OnDisable()
        {
            if (gameplayActions != null && gameplayActions.Gameplay.enabled)
            {
                gameplayActions.Gameplay.Disable();
            }
        }

        private void OnDestroy()
        {
            PauseService.Instance.ClearInputReader(this);
            Dispose();
        }

        /// <summary>
        /// 生成された入力アクションクラスが保持するリソースを解放します。
        /// </summary>
        public void Dispose()
        {
            if (gameplayActions == null) return;

            try
            {
                gameplayActions.Disable();
                gameplayActions.Gameplay.Disable();
                gameplayActions.UI.Disable();
            }
            catch
            {
                // Teardown時の例外を抑制
            }

            gameplayActions.Dispose();
            gameplayActions = null;
            HasMouseAttackBinding = false;
        }

        /// <summary>
        /// 現在フレームの入力を読み取ります。ボタンは押下されたフレームだけtrueになります。
        /// </summary>
        public GameplayInputSnapshot ReadSnapshot()
        {
            if (gameplayActions == null) return default;

            if (!isGameplayInputEnabled)
            {
                return new GameplayInputSnapshot(
                    Vector2.zero,
                    Vector2.zero,
                    attackPressed: false,
                    restartAction.WasPressedThisFrame(),
                    exitAction.WasPressedThisFrame(),
                    attackHeld: false);
            }

            return new GameplayInputSnapshot(
                moveAction.ReadValue<Vector2>(),
                lookAction.ReadValue<Vector2>(),
                attackAction.WasPressedThisFrame(),
                restartAction.WasPressedThisFrame(),
                exitAction.WasPressedThisFrame(),
                attackHeld: attackAction.IsPressed());
        }

        private void SetupActions()
        {
            gameplayActions = new global::InputSystem();

            InputSystem.GameplayActions gameplay = gameplayActions.Gameplay;
            InputActionMap gameplayMap = gameplay.Get();
            moveAction = gameplay.Move;
            lookAction = gameplay.Look;
            attackAction = gameplay.Attack;
            restartAction = gameplay.Restart;
            exitAction = gameplay.Exit;

            Assert.IsTrue(gameplayMap is { name: "Gameplay" },
                "Gameplayアクションマップが見つかりません。Assets/InputSystem.inputactionsのマップ名をGameplayにしてください。");
            Assert.IsNotNull(moveAction, "Gameplayアクション「Move」が見つかりません。移動入力を設定してください。");
            Assert.IsNotNull(lookAction, "Gameplayアクション「Look」が見つかりません。カメラ入力を設定してください。");
            Assert.IsNotNull(attackAction, "Gameplay/Attackアクションが見つかりません。攻撃入力を設定してください。");
            Assert.IsNotNull(restartAction, "Gameplayアクション「Restart」が見つかりません。再開入力を設定してください。");
            Assert.IsNotNull(exitAction, "Gameplayアクション「Exit」が見つかりません。終了入力を設定してください。");

            HasMouseAttackBinding = HasBinding(attackAction, "<Mouse>/leftButton");
            Assert.IsTrue(HasMouseAttackBinding, "Gameplay/Attackに<Mouse>/leftButtonバインドがありません。左クリック攻撃を設定してください。");
        }

        private static bool HasBinding(InputAction action, string expectedPath)
        {
            if (action == null || string.IsNullOrEmpty(expectedPath))
            {
                return false;
            }

            return action.bindings.Any(t => string.Equals(t.path, expectedPath, StringComparison.Ordinal));
        }
    }
}