using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 一人の攻撃者について、(攻撃者, AttackSequenceId) をキーとした
    /// 一つの攻撃有効ウィンドウだけを管理します。プレイヤーと敵は同じ型を使い、
    /// それぞれ異なる攻撃者・攻撃範囲・ダメージ設定を持つ別インスタンスとして共有します。
    /// </summary>
    public sealed class AttackWindowTracker
    {
        private readonly CombatantMarker attacker;
        private readonly HashSet<CombatantMarker> hitTargetsThisSequence = new HashSet<CombatantMarker>();

        private float attackRange;
        private int openSequenceId;

        public AttackWindowTracker(CombatantMarker attacker, float attackRange)
        {
            this.attacker = attacker;
            this.attackRange = Mathf.Max(0f, attackRange);
        }

        /// <summary>この追跡器が担当する攻撃者です。</summary>
        public CombatantMarker Attacker => attacker;

        /// <summary>現在有効な攻撃有効ウィンドウが開いているかを返します。</summary>
        public bool IsWindowOpen => openSequenceId != 0;

        /// <summary>現在開いているウィンドウの攻撃系列IDです。開いていない場合は0です。</summary>
        public int OpenSequenceId => openSequenceId;

        /// <summary>命中判定に使う攻撃範囲です。0以上にクランプされます。</summary>
        public float AttackRange
        {
            get => attackRange;
            set => attackRange = Mathf.Max(0f, value);
        }

        /// <summary>攻撃有効ウィンドウが開いたときに発火します。</summary>
        public event Action<int> WindowOpened;

        /// <summary>攻撃有効ウィンドウが閉じたときに一度だけ発火します。</summary>
        public event Action<int> WindowClosed;

        /// <summary>対象が命中候補として受理されたときに発火します。</summary>
        public event Action<CombatantMarker, int> TargetRegistered;

        /// <summary>
        /// 閉じている状態からだけ攻撃有効ウィンドウを開きます。既に開いている場合は失敗します。
        /// </summary>
        public Result BeginWindow(int sequenceId)
        {
            if (sequenceId <= 0)
            {
                return GameError.InvalidParameter;
            }

            UnityEngine.Assertions.Assert.IsNotNull(attacker, "AttackWindowTracker: 攻撃者が未設定です。");

            if (IsWindowOpen)
            {
                return GameError.ActionInProgress;
            }

            openSequenceId = sequenceId;
            hitTargetsThisSequence.Clear();
            WindowOpened?.Invoke(sequenceId);
            return Result.Ok();
        }

        /// <summary>
        /// ウィンドウが開いていて、対象が有効かつ生存していて、攻撃範囲内で、
        /// 同一攻撃系列でまだ命中していない場合だけ命中候補として受理します。
        /// </summary>
        public Result RegisterTarget(CombatantMarker target)
        {
            if (!IsWindowOpen)
            {
                return GameError.AttackWindowClosed;
            }

            if (!target.IsIdentityValid)
            {
                return GameError.InvalidParameter;
            }

            if (target == attacker)
            {
                return GameError.InvalidParameter;
            }

            if (!target.IsAvailableForCombat)
            {
                return GameError.TargetUnavailable;
            }

            if (!IsWithinRange(target))
            {
                return GameError.OutOfRange;
            }

            if (!hitTargetsThisSequence.Add(target))
            {
                return GameError.DuplicateHitInSequence;
            }

            TargetRegistered?.Invoke(target, openSequenceId);
            return Result.Ok();
        }

        /// <summary>
        /// 指定した攻撃系列のウィンドウを閉じます。既に閉じている場合や
        /// 別の攻撃系列を指定した場合は失敗を返す、冪等な操作です。
        /// </summary>
        public Result EndWindow(int sequenceId)
        {
            if (!IsWindowOpen)
            {
                return GameError.AttackWindowClosed;
            }

            if (sequenceId != openSequenceId)
            {
                return GameError.InvalidParameter;
            }

            int closingSequenceId = openSequenceId;
            openSequenceId = 0;
            hitTargetsThisSequence.Clear();
            WindowClosed?.Invoke(closingSequenceId);
            return Result.Ok();
        }

        private bool IsWithinRange(CombatantMarker target)
        {
            Transform attackerTransform = attacker.transform;
            Transform targetTransform = target.transform;
            float sqrDistance = (targetTransform.position - attackerTransform.position).sqrMagnitude;
            return sqrDistance <= attackRange * attackRange;
        }
    }
}
