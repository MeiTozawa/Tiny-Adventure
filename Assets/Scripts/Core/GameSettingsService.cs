using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// ゲーム設定の永続化ストレージインターフェース。
    /// </summary>
    public interface ISettingsStorage
    {
        float GetFloat(string key, float defaultValue);
        void SetFloat(string key, float value);
        void Save();
    }

    /// <summary>
    /// PlayerPrefsに基づく設定永続化ストレージの実装。
    /// </summary>
    public sealed class PlayerPrefsSettingsStorage : ISettingsStorage
    {
        public float GetFloat(string key, float defaultValue)
        {
            return PlayerPrefs.GetFloat(key, defaultValue);
        }

        public void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
        }

        public void Save()
        {
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// ゲーム設定サービス。FOV設定の通知・制限・永続化を管理します。
    /// </summary>
    public sealed class GameSettingsService
    {
        public const string FovStorageKey = "TinyAdventure_Settings_Fov";
        public const float MinFov = 60f;
        public const float MaxFov = 110f;
        public const float DefaultFov = 85f;

        private static GameSettingsService instance;
        private readonly ISettingsStorage storage;
        private float currentFov;

        /// <summary>シングルトンインスタンス。</summary>
        public static GameSettingsService Instance => instance ??= new GameSettingsService();

        /// <summary>現在の一人称FOV視野角（度）。</summary>
        public float CurrentFov => currentFov;

        /// <summary>FOV変更通知イベント。</summary>
        public event Action<float> FovChanged;

        /// <summary>設定サービスを初期化します。</summary>
        /// <param name="storage">ストレージアダプター。null時はPlayerPrefsを使用。</param>
        public GameSettingsService(ISettingsStorage storage = null)
        {
            this.storage = storage ?? new PlayerPrefsSettingsStorage();
            LoadSettings();
        }

        /// <summary>FOVを設定し、変更があれば永続化してイベントを発火します。</summary>
        public void SetFov(float value)
        {
            float clampedFov = Mathf.Clamp(value, MinFov, MaxFov);
            if (Mathf.Approximately(currentFov, clampedFov))
            {
                return;
            }

            currentFov = clampedFov;
            storage.SetFloat(FovStorageKey, currentFov);
            storage.Save();
            FovChanged?.Invoke(currentFov);
        }

        /// <summary>FOVを初期値（85°）にリセットします。</summary>
        public void ResetToDefault()
        {
            SetFov(DefaultFov);
        }

        private void LoadSettings()
        {
            float savedFov = storage.GetFloat(FovStorageKey, DefaultFov);
            currentFov = Mathf.Clamp(savedFov, MinFov, MaxFov);
        }
    }
}
