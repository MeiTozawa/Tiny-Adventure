using System;
using UnityEngine;

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
    public sealed class GameplayClock : MonoBehaviour, IGameplayClock
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

        /// <summary>ゲームプレイ固定tick時に通知します。購読側は割り当てを発生させない処理を行います。</summary>
        public event Action<double> FixedTick;

        private void Awake()
        {
            gameplayStateProvider = FindAnyObjectByType<GameFlowController>();
            gameplayNow = 0d;
            fixedGameplayNow = 0d;
            FixedTickCount = 0;
        }

        private void Update()
        {
            if (!IsGameplayTickEnabled)
            {
                return;
            }

            gameplayNow += Mathf.Max(0f, Time.deltaTime);
        }

private void FixedUpdate()
        {
            if (!IsGameplayTickEnabled)
            {
                return;
            }

            fixedGameplayNow += Mathf.Max(0f, Time.fixedDeltaTime);
            FixedTickCount++;
            try
            {
                FixedTick?.Invoke(fixedGameplayNow);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameplayClock診断] 固定AI tick中の例外を捕捉しました。例外種別: {exception.GetType().Name}。", this);
            }
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

        public static bool IsValidTimestamp(double timestamp)
        {
            return !double.IsNaN(timestamp) && !double.IsInfinity(timestamp) && timestamp >= 0d;
        }

        public static bool TryValidateTimestamp(double timestamp, out string diagnostic)
        {
            if (IsValidTimestamp(timestamp))
            {
                diagnostic = string.Empty;
                return true;
            }

            diagnostic = "ゲーム時刻は有限かつ0以上である必要があります。";
            return false;
        }
    }
}
