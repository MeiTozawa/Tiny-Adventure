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
    [ExecuteAlways]
    [RequireComponent(typeof(Collider))]
    public sealed class CombatHitbox : MonoBehaviour
    {
        // AttackWindowTrackerはUnityオブジェクトではない通常のC#クラスのため、Inspectorから
        // 直接ドラッグ設定することはできません。攻撃系列の開始時にSetWindowTrackerで設定します。
        private AttackWindowTracker windowTracker;

        [SerializeField]
        private Collider hitboxCollider;

        [SerializeField]
        private CombatantMarker attacker;

        private readonly HashSet<CombatantMarker> reportedTargetsThisFrameBatch = new HashSet<CombatantMarker>();

        private const int OverlapBufferCapacity = 64;
        private readonly Collider[] overlapBuffer = new Collider[OverlapBufferCapacity];
        private bool missingColliderReported;
        private bool missingTrackerReported;

        /// <summary>現在この Hitbox が橋渡しする AttackWindowTracker です。</summary>
        public AttackWindowTracker WindowTracker => windowTracker;

        public Collider HitboxCollider => hitboxCollider;
        public CombatantMarker Attacker => attacker;

        public void SetAttacker(CombatantMarker combatant) => attacker = combatant;

        private void Awake()
        {
            hitboxCollider = GetComponent<Collider>();
            attacker = GetComponentInParent<CombatantMarker>();
            if (hitboxCollider != null)
            {
                hitboxCollider.isTrigger = true;
            }
        }

        private void OnEnable()
        {
            if (windowTracker != null)
            {
                windowTracker.WindowOpened -= HandleWindowOpened;
                windowTracker.WindowOpened += HandleWindowOpened;
            }
        }

        private void OnDisable()
        {
            if (windowTracker != null)
            {
                windowTracker.WindowOpened -= HandleWindowOpened;
            }

            reportedTargetsThisFrameBatch.Clear();
        }

        private void OnDestroy()
        {
            if (windowTracker != null)
            {
                windowTracker.WindowOpened -= HandleWindowOpened;
            }

            windowTracker = null;
            reportedTargetsThisFrameBatch.Clear();
        }

        /// <summary>
        /// この Hitbox が命中候補を橋渡しする先の AttackWindowTracker を設定します。
        /// 新しい攻撃者・攻撃系列に切り替える際に呼び出します。
        /// </summary>
        public void SetWindowTracker(AttackWindowTracker tracker)
        {
            if (ReferenceEquals(windowTracker, tracker))
            {
                missingTrackerReported = false;
                return;
            }

            if (windowTracker != null)
            {
                windowTracker.WindowOpened -= HandleWindowOpened;
            }

            windowTracker = tracker;
            if (windowTracker != null)
            {
                windowTracker.WindowOpened += HandleWindowOpened;
            }

            missingTrackerReported = false;
        }

        private Vector3 previousCenter;
        private Quaternion previousRotation;
        private bool hasPreviousPosition;

        /// <summary>
        /// 新しい攻撃系列に向けて内部の重複排除状態を初期化します。
        /// AttackWindowTracker自体も系列ごとに命中集合をクリアしますが、
        /// Hitbox側の同フレーム内多重コールバック対策も合わせて初期化します。
        /// </summary>
        public void ResetForNewSequence()
        {
            reportedTargetsThisFrameBatch.Clear();
            hasPreviousPosition = false;
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

        private void FixedUpdate()
        {
            if (windowTracker == null || !windowTracker.IsWindowOpen)
            {
                hasPreviousPosition = false;
                return;
            }

            Vector3 currentCenter = hitboxCollider != null ? hitboxCollider.bounds.center : transform.position;
            Quaternion currentRotation = transform.rotation;
            Vector3 extents = hitboxCollider != null ? hitboxCollider.bounds.extents : Vector3.one * 0.25f;

            if (hasPreviousPosition)
            {
                Vector3 delta = currentCenter - previousCenter;
                float distance = delta.magnitude;
                // 物理フレーム間の移動が大きい場合、中間点をサンプリングしてすり抜け（Tunneling）を防ぎます。
                if (distance > 0.05f)
                {
                    int steps = Mathf.Clamp(Mathf.CeilToInt(distance / 0.15f), 1, 4);
                    for (int step = 1; step <= steps; step++)
                    {
                        float t = (float)step / (steps + 1);
                        Vector3 interpolatedCenter = Vector3.Lerp(previousCenter, currentCenter, t);
                        Quaternion interpolatedRotation = Quaternion.Slerp(previousRotation, currentRotation, t);
                        int count = Physics.OverlapBoxNonAlloc(
                            interpolatedCenter,
                            extents,
                            overlapBuffer,
                            interpolatedRotation,
                            Physics.AllLayers,
                            QueryTriggerInteraction.Collide);

                        for (int i = 0; i < count; i++)
                        {
                            TryRegisterCandidate(overlapBuffer[i]);
                        }
                    }
                }
            }

            previousCenter = currentCenter;
            previousRotation = currentRotation;
            hasPreviousPosition = true;
        }

        private void HandleWindowOpened(int sequenceId)
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            reportedTargetsThisFrameBatch.Clear();
            Bounds bounds = hitboxCollider.bounds;
            previousCenter = bounds.center;
            previousRotation = transform.rotation;
            hasPreviousPosition = true;

            int overlapCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlapBuffer,
                Quaternion.identity,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            for (int index = 0; index < overlapCount; index++)
            {
                TryRegisterCandidate(overlapBuffer[index]);
            }

            CombatantMarker atk = Attacker;
            if (atk == null || windowTracker.Attacker != atk || windowTracker.AttackRange <= 0f)
            {
                return;
            }

            int nearbyCount = Physics.OverlapSphereNonAlloc(
                atk.transform.position,
                windowTracker.AttackRange,
                overlapBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            for (int index = 0; index < nearbyCount; index++)
            {
                TryRegisterCandidate(overlapBuffer[index]);
            }
        }


        internal void TryRegisterCandidate(Collider other)
        {
            if (!EnsureReferencesReady())
            {
                return;
            }

            ICombatHurtbox hurtbox = other.GetComponent<ICombatHurtbox>() ?? other.GetComponentInParent<ICombatHurtbox>();
            if (hurtbox == null || !hurtbox.IsActive)
            {
                return;
            }

            CombatantMarker candidate = hurtbox.Owner;
            if (candidate == null || !candidate.IsIdentityValid)
            {
                return;
            }

            if (reportedTargetsThisFrameBatch.Contains(candidate))
            {
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
            if (HitboxCollider == null)
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
