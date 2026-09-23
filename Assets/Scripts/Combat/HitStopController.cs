using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットストップ（Hit Stop）中央コントローラー。
    /// 登録された全参加者（Player、Enemy）の局所的な時間停止と復帰を管理します。
    /// Time.timeScale は一切変更せず、非スケール時間（IUnscaledTimeSource）を用いて計測と復帰を行います。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopController : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("設定参照")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        private ICombatFeedbackProfileProvider profileProvider;
        private IUnscaledTimeSource timeSource = new RealtimeUnscaledTimeSource();
        private IHitStopParticipantRegistry participantRegistry;
        private readonly List<IHitStopParticipant> registeredParticipants = new List<IHitStopParticipant>();

        private HitStopToken currentToken;
        private double startedAtUnscaled;
        private double deadlineUnscaled;
        private int nextTokenId;

        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;

        public bool IsActive => currentToken.Id != 0 && timeSource.Now < deadlineUnscaled;
        public float RemainingUnscaledSeconds => IsActive
            ? (float)Math.Max(0d, deadlineUnscaled - timeSource.Now)
            : 0f;

        private void Awake()
        {
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>
        /// Update ループまたはテスト駆動用の状態更新ステップ。
        /// </summary>
        public void Tick()
        {
            if (currentToken.Id == 0) return;

            double now = timeSource.Now;
            if (now >= deadlineUnscaled)
            {
                EndHitStopInternal();
            }
        }

        /// <summary>
        /// 設定および依存関係を注入します。
        /// </summary>
        internal void SetDependencies(
            IUnscaledTimeSource time,
            IHitStopParticipantRegistry registry = null,
            ICombatFeedbackProfileProvider profile = null)
        {
            timeSource = time;
            participantRegistry = registry;
            if (profile != null)
            {
                profileProvider = profile;
            }
            ClearRuntimeState();
        }

        /// <summary>
        /// 参加者を登録します。
        /// </summary>
        public void RegisterParticipant(IHitStopParticipant participant)
        {
            if (participant == null || registeredParticipants.Contains(participant)) return;
            registeredParticipants.Add(participant);
            if (IsActive)
            {
                participant.BeginHitStop(currentToken);
            }
        }

        /// <summary>
        /// 参加者の登録を解除します。
        /// </summary>
        public void UnregisterParticipant(IHitStopParticipant participant)
        {
            if (participant == null) return;
            if (IsActive)
            {
                participant.EndHitStop(currentToken);
            }
            registeredParticipants.Remove(participant);
        }

        /// <summary>
        /// ヒットフィードバックエントリ: 局所ヒットストップをトリガーします。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                return;
            }

            var settings = profile.HitStop;
            float requestedDuration = request.HitType == CombatHitType.Lethal
                ? settings.lethalSeconds
                : settings.normalSeconds;

            float duration = Mathf.Clamp(requestedDuration, 0.01f, settings.maximumSeconds);
            double now = timeSource.Now;

            if (IsActive)
            {
                // ヒットストップの合算・延長（最大総時間を超えない範囲）
                double newDeadline = Math.Min(now + duration, startedAtUnscaled + settings.maximumSeconds);
                if (newDeadline > deadlineUnscaled)
                {
                    deadlineUnscaled = newDeadline;
                }
            }
            else
            {
                startedAtUnscaled = now;
                deadlineUnscaled = now + duration;
                currentToken = new HitStopToken(++nextTokenId, startedAtUnscaled, deadlineUnscaled);

                NotifyParticipantsBegin(currentToken);
            }
        }

        /// <summary>
        /// ランタイムヒットストップ状態を強制クリアし、全参加者を復帰させます。
        /// </summary>
        public void ClearRuntimeState()
        {
            if (currentToken.Id != 0)
            {
                NotifyParticipantsEnd(currentToken);
                currentToken = default;
            }

            startedAtUnscaled = 0d;
            deadlineUnscaled = 0d;
        }

        private void EndHitStopInternal()
        {
            NotifyParticipantsEnd(currentToken);
            currentToken = default;
            startedAtUnscaled = 0d;
            deadlineUnscaled = 0d;
        }

        private void NotifyParticipantsBegin(HitStopToken token)
        {
            var participants = GetAllParticipants();
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p != null && p.IsHitStopParticipant)
                {
                    try
                    {
                        p.BeginHitStop(token);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[HitStopController] Hit Stop 開始コールバック例外: {ex.Message}", this);
                    }
                }
            }
        }

        private void NotifyParticipantsEnd(HitStopToken token)
        {
            var participants = GetAllParticipants();
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p != null)
                {
                    try
                    {
                        p.EndHitStop(token);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[HitStopController] Hit Stop 終了コールバック例外: {ex.Message}", this);
                    }
                }
            }
        }

        private List<IHitStopParticipant> GetAllParticipants()
        {
            var list = new List<IHitStopParticipant>(registeredParticipants);
            if (participantRegistry != null && participantRegistry.Participants != null)
            {
                for (int i = 0; i < participantRegistry.Participants.Count; i++)
                {
                    var p = participantRegistry.Participants[i];
                    if (p != null && !list.Contains(p))
                    {
                        list.Add(p);
                    }
                }
            }
            return list;
        }





        private sealed class RealtimeUnscaledTimeSource : IUnscaledTimeSource
        {
            public double Now => Time.realtimeSinceStartupAsDouble;
        }
    }
}
