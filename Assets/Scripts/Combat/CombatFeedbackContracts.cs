using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// ヒットフィードバックの種別（通常ヒット・撃破ヒット）。
    /// </summary>
    public enum CombatHitType
    {
        Normal,
        Lethal
    }

    /// <summary>
    /// 同一攻撃系列での同一対象への重複フィードバック防止キー。
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
    /// 不変ヒットフィードバック要求値オブジェクト。
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
    /// キャラクター死亡音声再生要求。
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
    /// 攻撃演出フィードバックコンテキスト（空振り音・トレイル用）。
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
    /// ヒットストップ識別トークン。
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
    /// ダメージフィードバック要求イベント源インターフェース。
    /// </summary>
    public interface IDamageFeedbackSource
    {
        event Action<CombatantMarker, DamageRequest> HitFeedbackRequested;
    }

    /// <summary>
    /// フィードバックサブモジュールの統一インターフェース。
    /// </summary>
    public interface ICombatFeedbackModule
    {
        void Play(CombatFeedbackRequest request);
        void ClearRuntimeState();
    }

    /// <summary>
    /// 被弾アニメーションフィードバック駆動インターフェース。
    /// </summary>
    public interface ICombatAnimationFeedback
    {
        void PlayNormalHit(CombatFeedbackRequest request);
        void PlayLethalHit(CombatFeedbackRequest request);
        void ClearRuntimeState();
    }

    /// <summary>
    /// VFXエフェクト生成インターフェース。
    /// </summary>
    public interface IVfxSpawner
    {
        GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale);
        void ScheduleDestroy(GameObject instance, float lifetime);
    }

    /// <summary>
    /// オーディオ再生アダプターインターフェース。
    /// </summary>
    public interface IAudioPlaybackAdapter
    {
        void PlayOneShot(AudioClip clip, Vector3 worldPosition, float volume, float pitch, bool spatialized);
    }

    /// <summary>
    /// キャラクター死亡イベント源インターフェース。
    /// </summary>
    public interface IHealthDeathSource
    {
        event Action Died;
        CombatantMarker Marker { get; }
        bool IsAlive { get; }
    }

    /// <summary>
    /// ヒットストップ参加者インターフェース。
    /// </summary>
    public interface IHitStopParticipant
    {
        bool IsHitStopParticipant { get; }
        void BeginHitStop(HitStopToken token);
        void EndHitStop(HitStopToken token);
    }

    /// <summary>
    /// ヒットストップ参加者レジストリインターフェース。
    /// </summary>
    public interface IHitStopParticipantRegistry
    {
        IReadOnlyList<IHitStopParticipant> Participants { get; }
    }

    /// <summary>
    /// 非スケール実時間源インターフェース。
    /// </summary>
    public interface IUnscaledTimeSource
    {
        double Now { get; }
    }

    /// <summary>
    /// カメラインパルス生成インターフェース。
    /// </summary>
    public interface ICameraImpulseEmitter
    {
        void Generate(ImpulseFeedbackSettings settings, Vector3 direction);
    }

    /// <summary>
    /// カメラFOV衝撃アダプターインターフェース。
    /// </summary>
    public interface IFovPunchAdapter
    {
        void Punch(float offset, float enterSeconds, float recoverSeconds);
        void ClearRuntimeState();
        void SetBaseFov(float baseFov);
    }
}
