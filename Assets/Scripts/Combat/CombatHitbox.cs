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

        private readonly HashSet<CombatantMarker> reportedTargetsThisFrameBatch = new();
        private readonly Dictionary<Collider, ICombatHurtbox> hurtboxCache = new();

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
            UnityEngine.Assertions.Assert.IsNotNull(hitboxCollider, "CombatHitbox: Colliderコンポーネントが必要です。");
            attacker = GetComponentInParent<CombatantMarker>();
            hitboxCollider.isTrigger = true;
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
            hurtboxCache.Clear();
        }

        private void OnDestroy()
        {
            if (windowTracker != null)
            {
                windowTracker.WindowOpened -= HandleWindowOpened;
            }

            windowTracker = null;
            reportedTargetsThisFrameBatch.Clear();
            hurtboxCache.Clear();
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
            hurtboxCache.Clear();
            hasPreviousPosition = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterCandidate(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // 同一フレームでの多重コールバックや、Enter取得漏れに対する保険です。
            // 実際の重複排除と有効性判定はAttackWindowTrackerが行います。
            RegisterCandidate(other);
        }

        private void FixedUpdate()
        {
            if (windowTracker == null || !windowTracker.IsWindowOpen)
            {
                hasPreviousPosition = false;
                return;
            }

            Bounds bounds = hitboxCollider.bounds;
            Vector3 currentCenter = bounds.center;
            Quaternion currentRotation = transform.rotation;
            Vector3 extents = bounds.extents;

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
                            RegisterCandidate(overlapBuffer[i]);
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
                RegisterCandidate(overlapBuffer[index]);
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
                RegisterCandidate(overlapBuffer[index]);
            }
        }


        internal void RegisterCandidate(Collider other)
        {
            if (other == null || !EnsureReferencesReady())
            {
                return;
            }

            if (!hurtboxCache.TryGetValue(other, out ICombatHurtbox hurtbox) || hurtbox == null)
            {
                if (!other.TryGetComponent(out hurtbox))
                {
                    hurtbox = other.GetComponentInParent<ICombatHurtbox>();
                }
                if (hurtboxCache.Count > 128)
                {
                    hurtboxCache.Clear();
                }
                hurtboxCache[other] = hurtbox;
            }

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

            Result registerResult = windowTracker.RegisterTarget(candidate);
            if (registerResult.IsOk)
            {
                reportedTargetsThisFrameBatch.Add(candidate);
            }
            else
            {
                // 重複ヒットや射程外など、多重コライダー検知時の想定内スキップ以外を診断警告
                if (registerResult.Error != GameError.DuplicateHitInSequence && registerResult.Error != GameError.OutOfRange)
                {
                    Debug.LogWarning($"[CombatHitbox] 攻撃対象の登録が拒絶されました: {registerResult.Error} (Target: {candidate.name})", this);
                }
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
