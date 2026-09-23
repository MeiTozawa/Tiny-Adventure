using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 戦闘ヒットVFXエフェクトコントローラー。
    /// CombatFeedbackRequest の通常/致命ヒット分類に基づきヒットパーティクルを生成し、
    /// 着弾点と攻撃方向から生成Transformを計算してインスタンス寿命を管理します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVfxController : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("設定参照")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        private IVfxSpawner spawner = new UnityVfxSpawner();
        private ICombatFeedbackProfileProvider profileProvider;
        private readonly List<GameObject> activeInstances = new List<GameObject>();

        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;
        public int ActiveInstanceCount => activeInstances.Count;

        private void Awake()
        {
        }

        /// <summary>
        /// 設定およびスポナーを注入します。
        /// </summary>
        internal void SetDependencies(IVfxSpawner newSpawner, ICombatFeedbackProfileProvider profile = null)
        {
            spawner = newSpawner;
            if (profile != null)
            {
                profileProvider = profile;
            }
        }

        /// <summary>
        /// ヒットフィードバック実行: ヒットエフェクトを生成します。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            // 受撃対象の瞬態閃白（Hit Flash）をトリガー
            if (request.Target != null)
            {
                var flashReceiver = request.Target.FlashReceiver;
                if (flashReceiver != null)
                {
                    flashReceiver.TriggerFlash(request.HitType);
                }
            }

            var profile = ProfileProvider;
            if (profile == null)
            {
                return;
            }

            HitFeedbackVariant variant = request.HitType == CombatHitType.Lethal
                ? profile.LethalHit
                : profile.NormalHit;

            if (variant.impactPrefab == null)
            {
                return;
            }

            Vector3 spawnPosition = request.HitPoint + variant.positionOffset;

            Quaternion spawnRotation;
            if (request.Direction.sqrMagnitude > 0.0001f)
            {
                spawnRotation = Quaternion.LookRotation(request.Direction) * Quaternion.Euler(variant.rotationOffset);
            }
            else if (request.Target != null && request.Target.transform.forward.sqrMagnitude > 0.0001f)
            {
                spawnRotation = Quaternion.LookRotation(request.Target.transform.forward) * Quaternion.Euler(variant.rotationOffset);
            }
            else
            {
                spawnRotation = Quaternion.Euler(variant.rotationOffset);
            }

            Vector3 spawnScale = variant.spawnScale != Vector3.zero ? variant.spawnScale : Vector3.one;

            try
            {
                GameObject instance = spawner.Spawn(variant.impactPrefab, spawnPosition, spawnRotation, spawnScale);
                if (instance != null)
                {
                    activeInstances.Add(instance);
                    spawner.ScheduleDestroy(instance, variant.lifetimeSeconds);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        /// <summary>
        /// 生成済みの全エフェクトをクリアします。
        /// </summary>
        public void ClearSpawnedEffects()
        {
            ClearRuntimeState();
        }

        /// <summary>
        /// ランタイム生成されたエフェクトインスタンスをクリアします。
        /// </summary>
        public void ClearRuntimeState()
        {
            for (int i = activeInstances.Count - 1; i >= 0; i--)
            {
                var instance = activeInstances[i];
                if (instance != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(instance);
                    }
                    else
                    {
                        DestroyImmediate(instance);
                    }
                }
            }

            activeInstances.Clear();
        }

        private void OnDestroy()
        {
            ClearRuntimeState();
        }


        private sealed class UnityVfxSpawner : IVfxSpawner
        {
            public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                if (prefab == null) return null;
                GameObject instance = Instantiate(prefab, position, rotation);
                instance.transform.localScale = scale;
                return instance;
            }

            public void ScheduleDestroy(GameObject instance, float lifetime)
            {
                if (instance != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(instance, Mathf.Max(0.01f, lifetime));
                    }
                    else
                    {
                        DestroyImmediate(instance);
                    }
                }
            }
        }
    }
}
