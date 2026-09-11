using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗打击顿挫（Hit Stop）中央控制器。
    /// 管理所有已注册参与者（Player、Enemy）的局部时间冻结与恢复。
    /// 绝对不修改 Time.timeScale，使用未缩放时间（IUnscaledTimeSource）进行计时与恢复。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopController : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("配置引用")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        private ICombatFeedbackProfileProvider profileProvider;
        private IUnscaledTimeSource timeSource;
        private IHitStopParticipantRegistry participantRegistry;
        private readonly List<IHitStopParticipant> registeredParticipants = new List<IHitStopParticipant>();

        private HitStopToken currentToken;
        private double startedAtUnscaled;
        private double deadlineUnscaled;
        private int nextTokenId;

        /// <summary>诊断通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;

        public bool IsActive => currentToken.Id != 0 && (timeSource != null ? timeSource.Now : Time.realtimeSinceStartupAsDouble) < deadlineUnscaled;
        public float RemainingUnscaledSeconds => IsActive
            ? (float)Math.Max(0d, deadlineUnscaled - (timeSource != null ? timeSource.Now : Time.realtimeSinceStartupAsDouble))
            : 0f;

        private void Awake()
        {
            EnsureTimeSource();
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>
        /// 供 Update 循环或测试驱动的状态步进。
        /// </summary>
        public void Tick()
        {
            if (currentToken.Id == 0) return;

            double now = timeSource != null ? timeSource.Now : Time.realtimeSinceStartupAsDouble;
            if (now >= deadlineUnscaled)
            {
                EndHitStopInternal();
            }
        }

        /// <summary>
        /// 测试用配置与依赖注入。
        /// </summary>
        public void ConfigureForTests(
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
        /// 注册参与者。
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
        /// 注销参与者。
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
        /// 命中反馈入口：触发局部顿挫。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("未配置 CombatFeedbackProfile，跳过 Hit Stop。", false);
                return;
            }

            EnsureTimeSource();

            var settings = profile.HitStop;
            float requestedDuration = request.HitType == CombatHitType.Lethal
                ? settings.lethalSeconds
                : settings.normalSeconds;

            float duration = Mathf.Clamp(requestedDuration, 0.01f, settings.maximumSeconds);
            double now = timeSource.Now;

            if (IsActive)
            {
                // 合并/延长停顿，但不超过最大总时长
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
        /// 强制清理运行时顿挫状态，恢复所有参与者。
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
            LastDiagnostic = string.Empty;
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
                        ReportDiagnostic($"参与者「{p.GetType().Name}」开始 Hit Stop 异常：{ex.Message}", true);
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
                        ReportDiagnostic($"参与者「{p.GetType().Name}」结束 Hit Stop 异常：{ex.Message}", true);
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

        private void EnsureTimeSource()
        {
            if (timeSource == null)
            {
                timeSource = new RealtimeUnscaledTimeSource();
            }
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[HitStop 诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[HitStop 诊断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }

        private sealed class RealtimeUnscaledTimeSource : IUnscaledTimeSource
        {
            public double Now => Time.realtimeSinceStartupAsDouble;
        }
    }
}
