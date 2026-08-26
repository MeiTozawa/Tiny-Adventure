using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// ゲームプレイで利用する置換可能な時刻源です。
    /// </summary>
    public interface IGameplayClock
    {
        double Now { get; }
        double FixedNow { get; }
    }

    /// <summary>
    /// Unity のゲーム時間を提供し、外部の壁時計に依存しない時刻契約を保ちます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayClock : MonoBehaviour, IGameplayClock
    {
        /// <summary>現在フレームのゲーム時間です。</summary>
        public double Now => Time.timeAsDouble;

        /// <summary>直近の固定更新時点のゲーム時間です。</summary>
        public double FixedNow => Time.fixedTimeAsDouble;

        /// <summary>
        /// ダメージなどのゲームイベントに設定できる時刻かを検査します。
        /// 有限であり、ゲーム開始前を示す負値ではないことが必要です。
        /// </summary>
        public static bool IsValidTimestamp(double timestamp)
        {
            return !double.IsNaN(timestamp) && !double.IsInfinity(timestamp) && timestamp >= 0d;
        }

        /// <summary>
        /// 時刻の契約違反を日本語診断で返します。
        /// </summary>
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
