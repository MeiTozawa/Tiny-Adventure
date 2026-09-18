using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    public sealed class RecordingVfxSpawner : IVfxSpawner
    {
        public readonly struct SpawnRecord
        {
            public readonly GameObject Prefab;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public SpawnRecord(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Prefab = prefab;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }

        public readonly List<SpawnRecord> SpawnRecords = new List<SpawnRecord>();
        public readonly List<(GameObject Instance, float Lifetime)> ScheduledDestroys = new List<(GameObject, float)>();
        public bool ThrowOnSpawn { get; set; }

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (ThrowOnSpawn)
            {
                throw new InvalidOperationException("模擬 VFX 生成例外。");
            }

            SpawnRecords.Add(new SpawnRecord(prefab, position, rotation, scale));
            var go = new GameObject("MockVfxInstance");
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = scale;
            return go;
        }

        public void ScheduleDestroy(GameObject instance, float lifetime)
        {
            ScheduledDestroys.Add((instance, lifetime));
            if (instance != null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }

    public sealed class RecordingAudioPlaybackAdapter : IAudioPlaybackAdapter
    {
        public readonly struct AudioPlayRecord
        {
            public readonly AudioClip Clip;
            public readonly Vector3 WorldPosition;
            public readonly float Volume;
            public readonly float Pitch;
            public readonly bool Spatialized;

            public AudioPlayRecord(AudioClip clip, Vector3 worldPosition, float volume, float pitch, bool spatialized)
            {
                Clip = clip;
                WorldPosition = worldPosition;
                Volume = volume;
                Pitch = pitch;
                Spatialized = spatialized;
            }
        }

        public readonly List<AudioPlayRecord> PlayRecords = new List<AudioPlayRecord>();
        public bool ThrowOnPlay { get; set; }

        public void PlayOneShot(AudioClip clip, Vector3 worldPosition, float volume, float pitch, bool spatialized)
        {
            if (ThrowOnPlay)
            {
                throw new InvalidOperationException("模擬オーディオ再生例外。");
            }

            PlayRecords.Add(new AudioPlayRecord(clip, worldPosition, volume, pitch, spatialized));
        }
    }

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

    public sealed class RecordingAnimationFeedback : ICombatAnimationFeedback, ICombatFeedbackModule
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

        public bool Register(CombatantMarker combatant) => combatant != null && combatants.Add(combatant);
        public bool Unregister(CombatantMarker combatant) => combatant != null && combatants.Remove(combatant);
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
            DamageRequest.TryCreate(
                registry,
                source,
                target,
                amount,
                sequenceId,
                kind,
                target != null ? target.transform.position : Vector3.zero,
                0d,
                out var request,
                out _);
            return request;
        }
    }
}



