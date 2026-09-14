using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 游戏设置持久化存储接口，便于单测隔离与 mock。
    /// </summary>
    public interface ISettingsStorage
    {
        float GetFloat(string key, float defaultValue);
        void SetFloat(string key, float value);
        void Save();
    }

    /// <summary>
    /// 基于 Unity PlayerPrefs 的默认持久化存储实现。
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
    /// 游戏设置服务。管理 FOV 等设置状态的分发、限制与持久化。
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

        /// <summary>
        /// 全局单例访问入口。
        /// </summary>
        public static GameSettingsService Instance => instance ??= new GameSettingsService();

        /// <summary>
        /// 当前第一人称 FOV 视野角（度）。
        /// </summary>
        public float CurrentFov => currentFov;

        /// <summary>
        /// FOV 变更通知事件。
        /// </summary>
        public event Action<float> FovChanged;

        /// <summary>
        /// 初始化游戏设置服务。
        /// </summary>
        /// <param name="storage">存储适配器，为 null 时默认使用 PlayerPrefs。</param>
        public GameSettingsService(ISettingsStorage storage = null)
        {
            this.storage = storage ?? new PlayerPrefsSettingsStorage();
            LoadSettings();
        }

        /// <summary>
        /// 设定全局第一人称 FOV，自动限制在 [MinFov, MaxFov] 之间，并在变动时持久化与触发事件。
        /// </summary>
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

        /// <summary>
        /// 重置 FOV 为推荐默认值（85°）。
        /// </summary>
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
