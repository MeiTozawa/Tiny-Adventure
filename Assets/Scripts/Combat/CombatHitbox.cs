using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 剣や敵の武器に付ける trigger Collider です。接触した戦闘対象を収集し、
    /// AttackWindowTracker.RegisterTarget へ橋渡しするだけで、Health を直接変更しません。
    /// 命中判定（ウィンドウ、範囲、生存、重複排除）は AttackWindowTracker が担います。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CombatHitbox : MonoBehaviour
    {
        // AttackWindowTrackerはUnityオブジェクトではない通常のC#クラスのため、Inspectorから
        // 直接ドラッグ設定することはできません。攻撃系列の開始時にSetWindowTrackerで設定します。
        private AttackWindowTracker windowTracker;

        private Collider hitboxCollider;
        private readonly HashSet<CombatantMarker> reportedTargetsThisFrameBatch = new HashSet<CombatantMarker>();
        private bool missingColliderReported;
        private bool missingTrackerReported;

        /// <summary>現在この Hitbox が橋渡しする AttackWindowTracker です。</summary>
        public AttackWindowTracker WindowTracker => windowTracker;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            reportedTargetsThisFrameBatch.Clear();
        }

        private void OnDestroy()
        {
            windowTracker = null;
            reportedTargetsThisFrameBatch.Clear();
        }

        /// <summary>
        /// この Hitbox が命中候補を橋渡しする先の AttackWindowTracker を設定します。
        /// 新しい攻撃者・攻撃系列に切り替える際に呼び出します。
        /// </summary>
        public void SetWindowTracker(AttackWindowTracker tracker)
        {
            windowTracker = tracker;
            missingTrackerReported = false;
        }

        /// <summary>
        /// 新しい攻撃系列に向けて内部の重複排除状態を初期化します。
        /// AttackWindowTracker自体も系列ごとに命中集合をクリアしますが、
        /// Hitbox側の同フレーム内多重コールバック対策も合わせて初期化します。
        /// </summary>
        public void ResetForNewSequence()
        {
            reportedTargetsThisFrameBatch.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryRegisterCandidate(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // 同一フレームでの多重コールバックや、Enter取得漏れに対する保険です。
            // 実際の重複排除と有効性判定はAttackWindowTrackerが行います。
            TryRegisterCandidate(other);
        }

        private void TryRegisterCandidate(Collider other)
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            if (other == null)
            {
                return;
            }

            CombatantMarker candidate = other.GetComponentInParent<CombatantMarker>();
            if (candidate == null || !candidate.IsIdentityValid)
            {
                return;
            }

            if (reportedTargetsThisFrameBatch.Contains(candidate) && !windowTracker.IsWindowOpen)
            {
                // ウィンドウが既に閉じている場合、以前受理済みの対象への再送は不要です。
                return;
            }

            if (windowTracker.RegisterTarget(candidate, out string diagnostic))
            {
                reportedTargetsThisFrameBatch.Add(candidate);
                return;
            }

            if (!string.IsNullOrEmpty(diagnostic))
            {
                Debug.Log($"[戦闘診断] {diagnostic}", this);
            }
        }

        private bool EnsureReferencesReady()
        {
            ResolveReferences();

            if (hitboxCollider == null)
            {
                ReportMissingCollider();
                return false;
            }

            if (windowTracker == null)
            {
                ReportMissingTracker();
                return false;
            }

            missingColliderReported = false;
            missingTrackerReported = false;
            return true;
        }

        private void ResolveReferences()
        {
            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponent<Collider>();
                if (hitboxCollider != null)
                {
                    hitboxCollider.isTrigger = true;
                }
            }
        }

        private void ReportMissingCollider()
        {
            if (missingColliderReported)
            {
                return;
            }

            missingColliderReported = true;
            Debug.LogError("[戦闘診断] CombatHitboxにColliderが見つかりません。", this);
        }

        private void ReportMissingTracker()
        {
            if (missingTrackerReported)
            {
                return;
            }

            missingTrackerReported = true;
            Debug.LogWarning("[戦闘診断] CombatHitboxにAttackWindowTrackerが設定されていないため、命中候補を無視します。", this);
        }
    }
}
