using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 战斗打击 VFX 特效控制器。
    /// 依据 CombatFeedbackRequest 中的普通/致死命中分类生成对应的打击粒子特效，
    /// 精确计算基于受击点与打击朝向的生成变换，管理特效实例生命周期，禁止对象池。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVfxController : MonoBehaviour, ICombatFeedbackModule
    {
        [Header("配置引用")]
        [SerializeField]
        private CombatFeedbackProfile feedbackProfile;

        private IVfxSpawner spawner;
        private ICombatFeedbackProfileProvider profileProvider;
        private readonly List<GameObject> activeInstances = new List<GameObject>();

        /// <summary>VFX 诊断通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public ICombatFeedbackProfileProvider ProfileProvider => profileProvider ?? feedbackProfile;
        public int ActiveInstanceCount => activeInstances.Count;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>
        /// 测试用配置与生成器注入。
        /// </summary>
        public void ConfigureForTests(IVfxSpawner testSpawner, ICombatFeedbackProfileProvider profile = null)
        {
            spawner = testSpawner;
            if (profile != null)
            {
                profileProvider = profile;
            }
        }

        /// <summary>
        /// 命中反馈入口：生成命中特效。
        /// </summary>
        public void Play(CombatFeedbackRequest request)
        {
            var profile = ProfileProvider;
            if (profile == null)
            {
                ReportDiagnostic("未配置 CombatFeedbackProfile，跳过 VFX 生成。", false);
                return;
            }

            EnsureSpawner();

            HitFeedbackVariant variant = request.HitType == CombatHitType.Lethal
                ? profile.LethalHit
                : profile.NormalHit;

            if (variant.impactPrefab == null)
            {
                ReportDiagnostic($"缺少「{request.HitType}」命中特效 Prefab，跳过生成。", false);
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
                ReportDiagnostic($"生成命中特效异常：{ex.Message}", true);
            }
        }

        /// <summary>
        /// 清空所有已生成特效。
        /// </summary>
        public void ClearSpawnedEffects()
        {
            ClearRuntimeState();
        }

        /// <summary>
        /// 清理运行时生成的特效实例。
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
            LastDiagnostic = string.Empty;
        }

        private void OnDestroy()
        {
            ClearRuntimeState();
        }

        private void EnsureSpawner()
        {
            if (spawner == null)
            {
                spawner = new UnityVfxSpawner();
            }
        }

        private void ResolveReferences()
        {
            EnsureSpawner();
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[VFX 诊断] {message}", this);
            }
            else
            {
                Debug.Log($"[VFX 诊断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
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
