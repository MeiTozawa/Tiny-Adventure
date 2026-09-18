using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// キャラクター死亡オーディオ独立ルーター。
    /// Player および Enemy の HealthComponent.Died イベントを購読し、
    /// 重複排除キーにより各キャラクターの死亡SE（SFX_Player_Die / SFX_Enemy_Die）が高々1回のみ再生されることを保証します。
    /// HitFeedbackRequested の購読は行わず、致命ヒットSE（SFX_Hit_Lethal）と厳密に分離されます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDeathAudioRouter : MonoBehaviour
    {
        [Header("サービス参照")]
        [SerializeField]
        private CombatAudioController audioController;

        private readonly List<IHealthDeathSource> subscribedSources = new List<IHealthDeathSource>();
        private readonly HashSet<string> handledDeathDeduplicationKeys = new HashSet<string>();

        /// <summary>診断メッセージ通知。</summary>
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
        /// テスト用設定および依存関係を注入します。
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
        /// 単一の死亡イベントソースを購読します。
        /// </summary>
        public void Subscribe(IHealthDeathSource source)
        {
            if (source == null) return;
            SubscribeSource(source);
        }

        /// <summary>
        /// プレイヤーおよび敵の死亡イベントソースを一括購読します。
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
        /// 登録済みの全死亡イベントリスナーを解除します。
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
        /// ランタイム状態と重複排除記録をクリアします。
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
                ReportDiagnostic($"キャラクター「{key}」の死亡イベントが重複したため、死亡SEの重複再生をスキップしました。", false);
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
                ReportDiagnostic("CombatAudioController の参照が存在しないため、死亡SEを再生できません。", true);
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
                Debug.LogError($"[死亡音声ルーティング診断] {message}", this);
            }
            else
            {
                Debug.Log($"[死亡音声ルーティング診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }
}
