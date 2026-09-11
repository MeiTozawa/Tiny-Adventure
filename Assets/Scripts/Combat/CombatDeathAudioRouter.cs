using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 角色死亡音频独立路由器。
    /// 一次性订阅 Player 与 Enemy 的 HealthComponent.Died 事件，
    /// 使用死亡转换去重键保证每个角色死亡音效（SFX_Player_Die / SFX_Enemy_Die）至多播放一次。
    /// 严禁订阅 HitFeedbackRequested，与致死命中音（SFX_Hit_Lethal）严格解耦。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDeathAudioRouter : MonoBehaviour
    {
        [Header("服务引用")]
        [SerializeField]
        private CombatAudioController audioController;

        private readonly List<IHealthDeathSource> subscribedSources = new List<IHealthDeathSource>();
        private readonly HashSet<string> handledDeathDeduplicationKeys = new HashSet<string>();

        /// <summary>诊断通知。</summary>
        public event Action<string> DiagnosticReported;

        public string LastDiagnostic { get; private set; } = string.Empty;
        public int HandledDeathCount => handledDeathDeduplicationKeys.Count;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            AutoSubscribeScene();
        }

        private void OnDisable()
        {
            UnsubscribeAll();
            ClearRuntimeState();
        }

        /// <summary>
        /// 测试用配置与依赖注入。
        /// </summary>
        public void ConfigureForTests(
            IHealthDeathSource player,
            IReadOnlyList<IHealthDeathSource> enemies,
            CombatAudioController audio)
        {
            UnsubscribeAll();
            audioController = audio;
            ClearRuntimeState();

            if (player != null)
            {
                SubscribeSource(player);
            }

            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i] != null)
                    {
                        SubscribeSource(enemies[i]);
                    }
                }
            }
        }

        /// <summary>
        /// 订阅单个死亡事件源。
        /// </summary>
        public void Subscribe(IHealthDeathSource source)
        {
            if (source == null) return;
            SubscribeSource(source);
        }

        /// <summary>
        /// 批量订阅玩家与敌人集合。
        /// </summary>
        public void Subscribe(IHealthDeathSource player, IEnumerable<IHealthDeathSource> enemies)
        {
            if (player != null)
            {
                SubscribeSource(player);
            }

            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy != null)
                    {
                        SubscribeSource(enemy);
                    }
                }
            }
        }

        /// <summary>
        /// 取消所有已注册的死亡事件监听。
        /// </summary>
        public void UnsubscribeAll()
        {
            for (int i = 0; i < subscribedSources.Count; i++)
            {
                var source = subscribedSources[i];
                if (source != null)
                {
                    source.Died -= GetHandlerForSource(source);
                }
            }

            subscribedSources.Clear();
            handlersBySource.Clear();
        }

        /// <summary>
        /// 清理运行时状态与已处理去重记录。
        /// </summary>
        public void ClearRuntimeState()
        {
            handledDeathDeduplicationKeys.Clear();
            LastDiagnostic = string.Empty;
        }

        private readonly Dictionary<IHealthDeathSource, Action> handlersBySource = new Dictionary<IHealthDeathSource, Action>();

        private Action GetHandlerForSource(IHealthDeathSource source)
        {
            if (handlersBySource.TryGetValue(source, out var handler))
            {
                return handler;
            }

            handler = () => OnSourceDied(source);
            handlersBySource[source] = handler;
            return handler;
        }

        private void SubscribeSource(IHealthDeathSource source)
        {
            if (source == null || subscribedSources.Contains(source))
            {
                return;
            }

            subscribedSources.Add(source);
            source.Died += GetHandlerForSource(source);
        }

        private void OnSourceDied(IHealthDeathSource source)
        {
            if (source == null) return;

            string key = source.Marker != null && !string.IsNullOrEmpty(source.Marker.CombatantId)
                ? source.Marker.CombatantId
                : source.GetHashCode().ToString();

            if (handledDeathDeduplicationKeys.Contains(key))
            {
                ReportDiagnostic($"角色「{key}」重复收到死亡事件，跳过死亡音效重复播放。", false);
                return;
            }

            handledDeathDeduplicationKeys.Add(key);

            bool isPlayer = source.Marker != null && source.Marker.Faction == CombatantMarker.CombatantFaction.Player;
            Vector3 position = source.Marker != null ? source.Marker.transform.position : Vector3.zero;

            var request = new DeathAudioRequest(source.Marker, isPlayer, "death", position, key);

            if (audioController != null)
            {
                audioController.PlayDeath(request);
            }
            else
            {
                ReportDiagnostic("CombatAudioController 引用缺失，无法播放死亡音效。", true);
            }
        }

        private void AutoSubscribeScene()
        {
            var registry = SceneReferenceRegistry.ActiveInstance;
            if (registry == null) return;

            if (registry.Player != null)
            {
                var playerHealth = registry.Player.GetComponent<HealthComponent>() ?? registry.Player.GetComponentInParent<HealthComponent>();
                if (playerHealth != null)
                {
                    SubscribeSource(playerHealth);
                }
            }

            if (registry.ConfiguredEnemies != null)
            {
                foreach (var enemyMarker in registry.ConfiguredEnemies)
                {
                    if (enemyMarker != null)
                    {
                        var enemyHealth = enemyMarker.GetComponent<HealthComponent>() ?? enemyMarker.GetComponentInParent<HealthComponent>();
                        if (enemyHealth != null)
                        {
                            SubscribeSource(enemyHealth);
                        }
                    }
                }
            }
        }

        private void ResolveReferences()
        {
            if (audioController == null)
            {
                audioController = GetComponentInChildren<CombatAudioController>(true) ?? FindAnyObjectByType<CombatAudioController>();
            }
        }

        private void ReportDiagnostic(string message, bool asError)
        {
            LastDiagnostic = message;
            if (asError)
            {
                Debug.LogError($"[死亡音频路由] {message}", this);
            }
            else
            {
                Debug.Log($"[死亡音频路由] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
