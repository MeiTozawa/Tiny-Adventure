using System;

namespace TinyAdventure
{
    /// <summary>
    /// ゲーム設定（FOV、永続化等）の中央サービスインターフェース。
    /// </summary>
    public interface IGameSettingsService
    {
        /// <summary>現在の一人称FOV視野角（度）。</summary>
        float CurrentFov { get; }

        /// <summary>FOV変更通知イベント。</summary>
        event Action<float> FovChanged;

        /// <summary>FOVを設定し、変更があれば永続化してイベントを発火します。</summary>
        void SetFov(float value);

        /// <summary>FOVを初期値にリセットします。</summary>
        void ResetToDefault();
    }
}
