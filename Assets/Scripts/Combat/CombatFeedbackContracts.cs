using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 命中反馈的分类。第一版仅包含普通命中与致死命中，不包含重击。
    /// </summary>
    public enum CombatHitType
    {
        Normal,
        Lethal
    }

    /// <summary>
    /// 命中反馈去重键，确保同一攻击序列对同一目标仅响应一次。
    /// </summary>
    public readonly struct FeedbackDeduplicationKey : IEquatable<FeedbackDeduplicationKey>
    {
        public readonly CombatantMarker Source;
        public readonly CombatantMarker Target;
        public readonly int AttackSequenceId;

        public FeedbackDeduplicationKey(CombatantMarker source, CombatantMarker target, int attackSequenceId)
        {
            Source = source;
            Target = target;
            AttackSequenceId = attackSequenceId;
        }

        public bool Equals(FeedbackDeduplicationKey other)
        {
            return Equals(Source, other.Source) && Equals(Target, other.Target) && AttackSequenceId == other.AttackSequenceId;
        }

        public override bool Equals(object obj)
        {
            return obj is FeedbackDeduplicationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Source != null ? Source.GetHashCode() : 0;
                hash = (hash * 397) ^ (Target != null ? Target.GetHashCode() : 0);
                hash = (hash * 397) ^ AttackSequenceId;
                return hash;
            }
        }

        public static bool operator ==(FeedbackDeduplicationKey left, FeedbackDeduplicationKey right) => left.Equals(right);
        public static bool operator !=(FeedbackDeduplicationKey left, FeedbackDeduplicationKey right) => !left.Equals(right);
    }

    /// <summary>
    /// 不可变命中反馈请求，由 CombatFeedbackController 生成并分发给各表现子模块。
    /// </summary>
    public readonly struct CombatFeedbackRequest
    {
        public readonly CombatHitType HitType;
        public readonly CombatantMarker Source;
        public readonly CombatantMarker Target;
        public readonly DamageRequest Damage;
        public readonly Vector3 HitPoint;
        public readonly Vector3 Direction;
        public readonly bool IsPlayerAttack;
        public readonly bool IsPlayerTarget;
        public readonly FeedbackDeduplicationKey DeduplicationKey;

        public CombatFeedbackRequest(
            CombatHitType hitType,
            CombatantMarker source,
            CombatantMarker target,
            DamageRequest damage,
            Vector3 hitPoint,
            Vector3 direction,
            bool isPlayerAttack,
            bool isPlayerTarget,
            FeedbackDeduplicationKey deduplicationKey)
        {
            HitType = hitType;
            Source = source;
            Target = target;
            Damage = damage;
            HitPoint = hitPoint;
            Direction = direction;
            IsPlayerAttack = isPlayerAttack;
            IsPlayerTarget = isPlayerTarget;
            DeduplicationKey = deduplicationKey;
        }
    }

    /// <summary>
    /// 角色死亡音频播放请求，由 CombatDeathAudioRouter 独立路由。
    /// </summary>
    public readonly struct DeathAudioRequest
    {
        public readonly CombatantMarker Target;
        public readonly bool IsPlayer;
        public readonly string DeathTransitionId;
        public readonly Vector3 WorldPosition;
        public readonly string DeathDeduplicationKey;

        public DeathAudioRequest(
            CombatantMarker target,
            bool isPlayer,
            string deathTransitionId,
            Vector3 worldPosition,
            string deathDeduplicationKey)
        {
            Target = target;
            IsPlayer = isPlayer;
            DeathTransitionId = deathTransitionId;
            WorldPosition = worldPosition;
            DeathDeduplicationKey = deathDeduplicationKey;
        }
    }

    /// <summary>
    /// 攻击动作反馈上下文（用于挥刀音与刀光）。
    /// </summary>
    public readonly struct AttackFeedbackContext
    {
        public readonly CombatantMarker Attacker;
        public readonly int SequenceId;
        public readonly Vector3 Position;

        public AttackFeedbackContext(CombatantMarker attacker, int sequenceId, Vector3 position)
        {
            Attacker = attacker;
            SequenceId = sequenceId;
            Position = position;
        }
    }

    /// <summary>
    /// Hit Stop 顿挫标识令牌。
    /// </summary>
    public readonly struct HitStopToken
    {
        public readonly int Id;
        public readonly double StartedAtUnscaled;
        public readonly double DeadlineUnscaled;

        public HitStopToken(int id, double startedAtUnscaled, double deadlineUnscaled)
        {
            Id = id;
            StartedAtUnscaled = startedAtUnscaled;
            DeadlineUnscaled = deadlineUnscaled;
        }
    }

    /// <summary>
    /// 伤害反馈事件源契约，通常由 DamageService 提供。
    /// </summary>
    public interface IDamageFeedbackSource
    {
        event Action<CombatantMarker, DamageRequest> HitFeedbackRequested;
    }

    /// <summary>
    /// 表现子模块统一生命周期契约。
    /// </summary>
    public interface ICombatFeedbackModule
    {
        void Play(CombatFeedbackRequest request);
        void ClearRuntimeState();
    }

    /// <summary>
    /// 受击动画反馈驱动接口。
    /// </summary>
    public interface ICombatAnimationFeedback
    {
        void PlayNormalHit(CombatFeedbackRequest request);
        void PlayLethalHit(CombatFeedbackRequest request);
        void ClearRuntimeState();
    }

    /// <summary>
    /// 特效生成器接口。
    /// </summary>
    public interface IVfxSpawner
    {
        GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale);
        void ScheduleDestroy(GameObject instance, float lifetime);
    }

    /// <summary>
    /// 音频播放适配器接口。
    /// </summary>
    public interface IAudioPlaybackAdapter
    {
        void PlayOneShot(AudioClip clip, Vector3 worldPosition, float volume, float pitch, bool spatialized);
    }

    /// <summary>
    /// 角色死亡事件源契约。
    /// </summary>
    public interface IHealthDeathSource
    {
        event Action Died;
        CombatantMarker Marker { get; }
        bool IsAlive { get; }
    }

    /// <summary>
    /// Hit Stop 局部参与者接口。
    /// </summary>
    public interface IHitStopParticipant
    {
        bool IsHitStopParticipant { get; }
        void BeginHitStop(HitStopToken token);
        void EndHitStop(HitStopToken token);
    }

    /// <summary>
    /// Hit Stop 参与者注册表。
    /// </summary>
    public interface IHitStopParticipantRegistry
    {
        IReadOnlyList<IHitStopParticipant> Participants { get; }
    }

    /// <summary>
    /// 未缩放时间源接口，用于保证 Hit Stop 恢复不受全局时间缩放影响。
    /// </summary>
    public interface IUnscaledTimeSource
    {
        double Now { get; }
    }

    /// <summary>
    /// 相机冲量发射器接口（封装 Cinemachine 3 Impulse）。
    /// </summary>
    public interface ICameraImpulseEmitter
    {
        void Generate(ImpulseFeedbackSettings settings, Vector3 direction);
    }

    /// <summary>
    /// 相机 FOV 冲击适配器接口。
    /// </summary>
    public interface IFovPunchAdapter
    {
        void Punch(float offset, float enterSeconds, float recoverSeconds);
        void ClearRuntimeState();
    }
}
