using System;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>ゲームプレイ時間を外部の壁時計から分離する契約です。</summary>
    public interface IGameplayClock
    {
        double Now { get; }
        double FixedNow { get; }
    }

    /// <summary>
    /// 一時停止、再開、固定AI tickを提供するゲームプレイ時刻源です。
    /// 終局状態ではFlowの状態を監視してtickを停止します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayClock : MonoBehaviour, IGameplayClock, VContainer.Unity.ITickable, VContainer.Unity.IFixedTickable
    {
        private IGameplayStateProvider gameplayStateProvider;
        private double gameplayNow;
        private double fixedGameplayNow;
        private bool paused = true;

        public double Now => gameplayNow;
        public double FixedNow => fixedGameplayNow;
        public bool IsPaused => paused;
        public bool IsGameplayTickEnabled => !paused && (gameplayStateProvider == null || gameplayStateProvider.CurrentState == GameplayState.Running);
        public int FixedTickCount { get; private set; }

        /// <summary>
        /// 依存関係を明示的に注入・設定します。テストや手動接続で使用します。
        /// 循環依存を防止するため、DIによる GameFlowController の自動逆注入は行いません。
        /// </summary>
        public void Construct(IGameplayStateProvider stateProvider = null)
        {
            gameplayStateProvider = stateProvider;
        }

        private void Awake()
        {
            gameplayNow = 0d;
            fixedGameplayNow = 0d;
            FixedTickCount = 0;
        }

        public void Tick()
        {
            if (!IsGameplayTickEnabled)
            {
                return;
            }

            gameplayNow += Mathf.Max(0f, Time.deltaTime);
        }

        public void FixedTick()
        {
            if (!IsGameplayTickEnabled)
            {
                return;
            }

            fixedGameplayNow += Mathf.Max(0f, Time.fixedDeltaTime);
            FixedTickCount++;
        }

        /// <summary>GameFlowControllerを明示接続します。</summary>
        public void ConfigureStateProvider(IGameplayStateProvider stateProvider)
        {
            gameplayStateProvider = stateProvider;
        }

        /// <summary>ゲームプレイ時間とAI固定tickを停止します。</summary>
        public void PauseGameplay()
        {
            paused = true;
        }

        /// <summary>ゲームプレイ時間とAI固定tickをRunning中だけ再開します。</summary>
        public void ResumeGameplay()
        {
            paused = false;
        }

        /// <summary>テストまたは再開処理でゲームプレイ時間を初期値へ戻します。</summary>
        public void ResetGameplayTime()
        {
            gameplayNow = 0d;
            fixedGameplayNow = 0d;
            FixedTickCount = 0;
        }
    }
}
