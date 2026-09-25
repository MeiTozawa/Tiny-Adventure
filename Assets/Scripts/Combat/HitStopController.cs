using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットストップ（Hit Stop）中央コントローラー。
    /// 登録された全参加者（Player、Enemy）の局所的な時間停止と復帰を管理します。
    /// Time.timeScale は一切変更せず、非スケール時間（IUnscaledTimeSource）を用いて計測と復帰を行います。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopController : MonoBehaviour, IHitStopController
    {

        [Header("設定")]
        [SerializeField] private CombatFeedbackProfile feedbackProfile;

        private ICombatFeedbackProfileProvider profileProvider;
        private HitStopSettings settings = HitStopSettings.Default;
        private IUnscaledTimeSource timeSource = new RealtimeUnscaledTimeSource();
        private IHitStopParticipantRegistry participantRegistry;
        private readonly List<IHitStopParticipant> registeredParticipants = new List<IHitStopParticipant>();
        private readonly List<IHitStopParticipant> participantBuffer = new();

        private HitStopToken currentToken;
        private double startedAtUnscaled;
        private double deadlineUnscaled;
        private int nextTokenId;

        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;

        public bool IsActive => currentToken.Id != 0 && timeSource.Now < deadlineUnscaled;
        public float RemainingUnscaledSeconds => IsActive
            ? (float)Math.Max(0d, deadlineUnscaled - timeSource.Now)
            : 0f;

        public void SetProfileProvider(ICombatFeedbackProfileProvider profile)
        {
            profileProvider = profile;
            UpdateSettings();
        }

        private void Awake()
        {
            enabled = false;
            if (feedbackProfile == null)
            {
                if (TryGetComponent<CombatFeedbackController>(out var feedback) && feedback.ProfileProvider is CombatFeedbackProfile p)
                {
                    feedbackProfile = p;
                }
            }
            UpdateSettings();
        }

        private void OnValidate()
        {
            UpdateSettings();
        }

        private void UpdateSettings()
        {
            if (profileProvider != null)
            {
                settings = profileProvider.HitStop;
            }
            else if (feedbackProfile != null)
            {
                settings = feedbackProfile.HitStop;
            }
            else
            {
                settings = HitStopSettings.Default;
            }
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
        /// 設定および依存関係を注入します（テスト用）。
        /// </summary>
        public void ConstructForTesting(
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
            UpdateSettings();
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
                enabled = true;
            }
            else
            {
                startedAtUnscaled = now;
                deadlineUnscaled = now + duration;
                currentToken = new HitStopToken(++nextTokenId, startedAtUnscaled, deadlineUnscaled);

                NotifyParticipantsBegin(currentToken);
                enabled = true;
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
            if (enabled)
            {
                enabled = false;
            }
        }

        private void EndHitStopInternal()
        {
            NotifyParticipantsEnd(currentToken);
            currentToken = default;
            startedAtUnscaled = 0d;
            deadlineUnscaled = 0d;
            if (enabled)
            {
                enabled = false;
            }
        }

        private void NotifyParticipantsBegin(HitStopToken token)
        {
            var participants = GetAllParticipants();
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p.IsHitStopParticipant)
                {
                    p.BeginHitStop(token);
                }
            }
        }

        private void NotifyParticipantsEnd(HitStopToken token)
        {
            var participants = GetAllParticipants();
            for (int i = 0; i < participants.Count; i++)
            {
                participants[i].EndHitStop(token);
            }
        }

        private List<IHitStopParticipant> GetAllParticipants()
        {
            participantBuffer.Clear();
            participantBuffer.AddRange(registeredParticipants);
            if (participantRegistry != null && participantRegistry.Participants != null)
            {
                var regList = participantRegistry.Participants;
                foreach (var p in regList)
                {
                    if (p != null && !participantBuffer.Contains(p))
                    {
                        participantBuffer.Add(p);
                    }
                }
            }
            return participantBuffer;
        }
        
        private sealed class RealtimeUnscaledTimeSource : IUnscaledTimeSource
        {
            public double Now => Time.realtimeSinceStartupAsDouble;
        }
    }
}
