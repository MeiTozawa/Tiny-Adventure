using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// マルチソース対応のゲーム一時停止（ポーズ）管理サービスの実装。
    /// 複数の発生源（メニュー、リザルト画面等）からのポーズ要求を参照カウント管理し、
    /// カーソルロック状態、およびゲームプレイ入力を一元制御します。
    /// （状態ベースで非侵襲的に一時停止を管理します）
    /// </summary>
    public sealed class PauseService : IPauseService, IDisposable
    {
        private static PauseService instance;

        private readonly HashSet<PauseSource> activeSources = new();
        private InputReader inputReader;

        /// <summary>グローバル単一インスタンス（DI環境外でのテストおよびフォールバック用）。</summary>
        public static PauseService Instance => instance ??= new PauseService();

        /// <summary>現在一時停止中かどうか。</summary>
        public bool IsPaused => activeSources.Count > 0;

        /// <summary>ポーズ状態変化イベント（true: ポーズ開始, false: ポーズ解除）。</summary>
        public event Action<bool> PauseStateChanged;

        public PauseService(InputReader input = null)
        {
            inputReader = input;
            if (instance == null)
            {
                instance = this;
            }
        }

        /// <summary>
        /// 制御対象のInputReaderを設定します。
        /// </summary>
        public void SetInputReader(InputReader input)
        {
            inputReader = input;
            if (inputReader != null)
            {
                inputReader.SetGameplayInputEnabled(!IsPaused);
            }
        }

        /// <summary>
        /// 制御対象のInputReaderを解除します。
        /// </summary>
        public void ClearInputReader(InputReader input)
        {
            if (inputReader == input)
            {
                inputReader = null;
            }
        }

        /// <summary>
        /// 指定された発生源からポーズを要求し、解放用トークンを返します。
        /// </summary>
        public IDisposable RequestPause(PauseSource source)
        {
            if (activeSources.Add(source) && activeSources.Count == 1)
            {
                ApplyPauseState(true);
            }
            return new PauseHandle(this, source);
        }

        /// <summary>
        /// 指定された発生源のポーズ要求を明示的に解除します。
        /// </summary>
        public void ReleasePause(PauseSource source)
        {
            if (activeSources.Remove(source) && activeSources.Count == 0)
            {
                ApplyPauseState(false);
            }
        }

        /// <summary>
        /// 指定された発生源が現在ポーズを要求中かどうかを返します。
        /// </summary>
        public bool HasPauseSource(PauseSource source)
        {
            return activeSources.Contains(source);
        }

        /// <summary>
        /// すべてのポーズ要求を一括クリアし、通常速度（1f）とゲームプレイ入力へ復帰させます。
        /// </summary>
        public void ClearAllPauses()
        {
            if (activeSources.Count > 0)
            {
                activeSources.Clear();
                ApplyPauseState(false);
            }
            else
            {
                // 保険：要求源が0件でもカーソルが不整合を起こしている可能性を解消
                ApplyPauseState(false);
            }
        }

        private void ApplyPauseState(bool paused)
        {
            if (paused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (inputReader != null)
            {
                inputReader.SetGameplayInputEnabled(!paused);
            }

            PauseStateChanged?.Invoke(paused);
        }

        public void Dispose()
        {
            ClearAllPauses();
            if (instance == this)
            {
                instance = null;
            }
        }

        private sealed class PauseHandle : IDisposable
        {
            private PauseService service;
            private readonly PauseSource source;

            public PauseHandle(PauseService service, PauseSource source)
            {
                this.service = service;
                this.source = source;
            }

            public void Dispose()
            {
                if (service != null)
                {
                    service.ReleasePause(source);
                    service = null;
                }
            }
        }
    }
}
