using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 短時間の先行入力を保持し、アニメーション後隙や硬直が解けた直後に先行アクションを消化するための汎用バッファです。
    /// 指定された有効期間（デフォルト0.25秒）を過ぎた入力は自動的に失効します。
    /// </summary>
    public sealed class InputBuffer
    {
        public const float DefaultBufferDuration = 0.25f;
        public const string ActionAttack = "Attack";

        private readonly float defaultDuration;
        private readonly Dictionary<string, double> actionTimestamps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public float DefaultDuration => defaultDuration;

        public InputBuffer(float defaultDuration = DefaultBufferDuration)
        {
            this.defaultDuration = Mathf.Max(0.01f, defaultDuration);
        }

        /// <summary>
        /// アクションの入力を記録します。
        /// </summary>
        public void BufferAction(string actionName, double timestamp)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return;
            }

            actionTimestamps[actionName] = timestamp;
        }

        /// <summary>
        /// 指定したアクションの先行入力が現在有効期間内にあるか判定します。
        /// </summary>
        public bool HasBufferedAction(string actionName, double currentTimestamp, float maxAge = -1f)
        {
            if (string.IsNullOrWhiteSpace(actionName) || !actionTimestamps.TryGetValue(actionName, out double inputTime))
            {
                return false;
            }

            float allowedDuration = maxAge > 0f ? maxAge : defaultDuration;
            double elapsed = currentTimestamp - inputTime;
            return elapsed >= 0d && elapsed <= allowedDuration;
        }

        /// <summary>
        /// 有効な先行入力を消費します。有効期間内であれば true を返し、バッファから削除します。
        /// </summary>
        public bool ConsumeAction(string actionName, double currentTimestamp, float maxAge = -1f)
        {
            if (HasBufferedAction(actionName, currentTimestamp, maxAge))
            {
                actionTimestamps.Remove(actionName);
                return true;
            }

            actionTimestamps.Remove(actionName);
            return false;
        }

        /// <summary>
        /// すべての先行入力をクリアします。
        /// </summary>
        public void Clear()
        {
            actionTimestamps.Clear();
        }

        /// <summary>
        /// 特定のアクションの先行入力をクリアします。
        /// </summary>
        public void ClearAction(string actionName)
        {
            if (!string.IsNullOrWhiteSpace(actionName))
            {
                actionTimestamps.Remove(actionName);
            }
        }
    }
}
