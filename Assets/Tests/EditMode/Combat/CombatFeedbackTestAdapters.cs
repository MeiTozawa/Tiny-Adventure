using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{

    public sealed class RecordingImpulseEmitter : ICameraImpulseEmitter
    {
        public readonly struct ImpulseRecord
        {
            public readonly ImpulseFeedbackSettings Settings;
            public readonly Vector3 Direction;

            public ImpulseRecord(ImpulseFeedbackSettings settings, Vector3 direction)
            {
                Settings = settings;
                Direction = direction;
            }
        }

        public readonly List<ImpulseRecord> ImpulseRecords = new List<ImpulseRecord>();
        public bool ThrowOnGenerate { get; set; }

        public void Generate(ImpulseFeedbackSettings settings, Vector3 direction)
        {
            if (ThrowOnGenerate)
            {
                throw new InvalidOperationException("模擬カメラインパルス例外。");
            }

            ImpulseRecords.Add(new ImpulseRecord(settings, direction));
        }
    }

    public sealed class RecordingFovPunchAdapter : IFovPunchAdapter
    {
        public readonly struct FovPunchRecord
        {
            public readonly float Offset;
            public readonly float EnterSeconds;
            public readonly float RecoverSeconds;

            public FovPunchRecord(float offset, float enterSeconds, float recoverSeconds)
            {
                Offset = offset;
                EnterSeconds = enterSeconds;
                RecoverSeconds = recoverSeconds;
            }
        }

        public readonly List<FovPunchRecord> PunchRecords = new List<FovPunchRecord>();
        public int ClearCount { get; private set; }
        public bool ThrowOnPunch { get; set; }

        public void Punch(float offset, float enterSeconds, float recoverSeconds)
        {
            if (ThrowOnPunch)
            {
                throw new InvalidOperationException("模擬 FOV パンチ例外。");
            }

            PunchRecords.Add(new FovPunchRecord(offset, enterSeconds, recoverSeconds));
        }

        public void ClearRuntimeState()
        {
            ClearCount++;
            PunchRecords.Clear();
        }

        public float BaseFov { get; private set; } = 60f;

        public void SetBaseFov(float baseFov)
        {
            BaseFov = baseFov;
        }
    }

    public sealed class RecordingHitStopParticipant : IHitStopParticipant
    {
        public bool IsHitStopParticipant => true;
        public readonly List<HitStopToken> BeginTokens = new List<HitStopToken>();
        public readonly List<HitStopToken> EndTokens = new List<HitStopToken>();
        public bool ThrowOnBegin { get; set; }
        public bool ThrowOnEnd { get; set; }

        public void BeginHitStop(HitStopToken token)
        {
            if (ThrowOnBegin)
            {
                throw new InvalidOperationException("模擬ヒットストップ開始例外。");
            }

            BeginTokens.Add(token);
        }

        public void EndHitStop(HitStopToken token)
        {
            if (ThrowOnEnd)
            {
                throw new InvalidOperationException("模擬ヒットストップ終了例外。");
            }

            EndTokens.Add(token);
        }
    }

    public sealed class RecordingAnimationFeedback : ICombatFeedbackModule
    {
        public readonly List<CombatFeedbackRequest> NormalHitRequests = new List<CombatFeedbackRequest>();
        public readonly List<CombatFeedbackRequest> LethalHitRequests = new List<CombatFeedbackRequest>();
        public int ClearCount { get; private set; }
        public bool ThrowOnPlay { get; set; }

        public void Play(CombatFeedbackRequest request)
        {
            if (request.HitType == CombatHitType.Lethal)
            {
                PlayLethalHit(request);
            }
            else
            {
                PlayNormalHit(request);
            }
        }

        public void PlayNormalHit(CombatFeedbackRequest request)
        {
            if (ThrowOnPlay)
            {
                throw new InvalidOperationException("模擬被弾アニメーション例外。");
            }

            NormalHitRequests.Add(request);
        }

        public void PlayLethalHit(CombatFeedbackRequest request)
        {
            if (ThrowOnPlay)
            {
                throw new InvalidOperationException("模擬致命アニメーション例外。");
            }

            LethalHitRequests.Add(request);
        }

        public void ClearRuntimeState()
        {
            ClearCount++;
            NormalHitRequests.Clear();
            LethalHitRequests.Clear();
        }
    }

    public sealed class RecordingFeedbackModule : ICombatFeedbackModule
    {
        public readonly List<CombatFeedbackRequest> PlayRequests = new List<CombatFeedbackRequest>();
        public int ClearCount { get; private set; }
        public bool ThrowOnPlay { get; set; }
        public string Name { get; set; } = "RecordingModule";

        public void Play(CombatFeedbackRequest request)
        {
            if (ThrowOnPlay)
            {
                throw new InvalidOperationException($"サブモジュール「{Name}」模擬例外。");
            }

            PlayRequests.Add(request);
        }

        public void ClearRuntimeState()
        {
            ClearCount++;
            PlayRequests.Clear();
        }
    }

    public sealed class StubUnscaledTimeSource : IUnscaledTimeSource
    {
        public double CurrentTime { get; set; }
        public double Now => CurrentTime;
    }

    public sealed class StubHealthDeathSource : IHealthDeathSource
    {
        public event Action Died;
        public CombatantMarker Marker { get; set; }
        public bool IsAlive { get; set; } = true;

        public void TriggerDied()
        {
            IsAlive = false;
            Died?.Invoke();
        }
    }

    public sealed class RecordingDamageFeedbackSource : IDamageFeedbackSource
    {
        public event Action<CombatantMarker, DamageRequest> HitFeedbackRequested;

        public void Raise(CombatantMarker target, DamageRequest request)
        {
            HitFeedbackRequested?.Invoke(target, request);
        }
    }

    public sealed class StubHitStopParticipantRegistry : IHitStopParticipantRegistry
    {
        public List<IHitStopParticipant> List { get; } = new List<IHitStopParticipant>();
        public IReadOnlyList<IHitStopParticipant> Participants => List;
    }

    public sealed class StubGameplayStateProvider : IGameplayStateProvider
    {
        public GameplayState CurrentState { get; set; } = GameplayState.Running;
    }

    public sealed class StubCombatantRegistry : ICombatantRegistry
    {
        private readonly HashSet<CombatantMarker> combatants = new HashSet<CombatantMarker>();

        public void Register(CombatantMarker combatant)
        {
            if (combatant != null) combatants.Add(combatant);
        }
        public void Unregister(CombatantMarker combatant)
        {
            if (combatant != null) combatants.Remove(combatant);
        }
        public bool IsRegistered(CombatantMarker combatant) => combatant != null && combatants.Contains(combatant);
    }

    public static class TestDamageRequestFactory
    {
        public static DamageRequest Create(
            ICombatantRegistry registry,
            CombatantMarker source,
            CombatantMarker target,
            float amount = 10f,
            int sequenceId = 1,
            string kind = AttackKinds.KnightSword)
        {
            if (registry != null)
            {
                registry.Register(source);
                registry.Register(target);
            }
            return DamageRequest.Create(
                registry,
                source,
                target,
                amount,
                sequenceId,
                kind,
                target != null ? target.transform.position : Vector3.zero,
                0d).Value;
        }
    }
}



