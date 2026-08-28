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
        public bool BeginWindow(int sequenceId, out string diagnostic)
        {
            if (!DamageRequest.IsValidAttackSequenceId(sequenceId))
            {
                diagnostic = "攻撃系列IDが無効なため、攻撃有効ウィンドウを開けません。";
                return false;
            }

            if (attacker == null)
            {
                diagnostic = "攻撃者が設定されていないため、攻撃有効ウィンドウを開けません。";
                return false;
            }

            if (IsWindowOpen)
            {
                diagnostic = $"攻撃系列{openSequenceId}のウィンドウが既に開いているため、新しいウィンドウを開けません。";
                return false;
            }

            openSequenceId = sequenceId;
            hitTargetsThisSequence.Clear();
            diagnostic = string.Empty;
            WindowOpened?.Invoke(sequenceId);
            return true;
        }

        /// <summary>
        /// ウィンドウが開いていて、対象が有効かつ生存していて、攻撃範囲内で、
        /// 同一攻撃系列でまだ命中していない場合だけ命中候補として受理します。
        /// </summary>
        public bool RegisterTarget(CombatantMarker target, out string diagnostic)
        {
            if (!IsWindowOpen)
            {
                diagnostic = "攻撃有効ウィンドウが開いていないため、命中候補を無視しました。";
                return false;
            }

            if (target == null || !target.IsIdentityValid)
            {
                diagnostic = "無効な対象への命中候補を無視しました。";
                return false;
            }

            if (target == attacker)
            {
                diagnostic = "攻撃者自身への命中候補を無視しました。";
                return false;
            }

            if (!target.IsAvailableForCombat)
            {
                diagnostic = $"行動不能な対象「{target.CombatantId}」への命中候補を無視しました。";
                return false;
            }

            if (!IsWithinRange(target))
            {
                diagnostic = $"攻撃範囲外の対象「{target.CombatantId}」への命中候補を無視しました。";
                return false;
            }

            if (hitTargetsThisSequence.Contains(target))
            {
                diagnostic = $"攻撃系列{openSequenceId}で既に命中済みの対象「{target.CombatantId}」を無視しました。";
                return false;
            }

            hitTargetsThisSequence.Add(target);
            diagnostic = string.Empty;
            TargetRegistered?.Invoke(target, openSequenceId);
            return true;
        }

        /// <summary>
        /// 指定した攻撃系列のウィンドウを閉じます。既に閉じている場合や
        /// 別の攻撃系列を指定した場合は何もせず false を返す、冪等な操作です。
        /// </summary>
        public bool EndWindow(int sequenceId)
        {
            if (!IsWindowOpen)
            {
                return false;
            }

            if (sequenceId != openSequenceId)
            {
                return false;
            }

            int closingSequenceId = openSequenceId;
            openSequenceId = 0;
            hitTargetsThisSequence.Clear();
            WindowClosed?.Invoke(closingSequenceId);
            return true;
        }

        private bool IsWithinRange(CombatantMarker target)
        {
            if (attacker == null || target == null)
            {
                return false;
            }

            Transform attackerTransform = attacker.transform;
            Transform targetTransform = target.transform;
            if (attackerTransform == null || targetTransform == null)
            {
                return false;
            }

            float sqrDistance = (targetTransform.position - attackerTransform.position).sqrMagnitude;
            return sqrDistance <= attackRange * attackRange;
        }
    }
}
