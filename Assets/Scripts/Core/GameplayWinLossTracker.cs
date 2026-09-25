using System;
using System.Collections.Generic;

namespace TinyAdventure
{
    /// <summary>
    /// Playerおよび敵のHealthComponent状態を監視し、勝利（全敵撃破・消去）および敗北（Player死亡）の
    /// 条件成立を判定・通知する独立モジュールです。
    /// 単一責任：戦闘参加者の生命状態監視と勝敗条件の評価。
    /// </summary>
    public sealed class GameplayWinLossTracker
    {
        private readonly List<HealthComponent> subscribedHealthComponents = new List<HealthComponent>();
        private SceneReferenceRegistry sceneRegistry;
        private bool subscriptionsActive;

        public event Action DefeatConditionMet;
        public event Action VictoryConditionMet;

        public IReadOnlyList<HealthComponent> SubscribedHealthComponents => subscribedHealthComponents;

        /// <summary>
        /// SceneReferenceRegistry内のPlayerおよび全敵のHealthComponentへイベント登録します。
        /// </summary>
        public void SubscribeToCombatants(SceneReferenceRegistry registry)
        {
            if (subscriptionsActive || registry == null)
            {
                return;
            }

            sceneRegistry = registry;
            subscriptionsActive = true;

            SubscribeHealth(registry.Player);
            IReadOnlyList<CombatantMarker> enemies = registry.ConfiguredEnemies;
            if (enemies == null) return;
            foreach (var t in enemies)
            {
                SubscribeHealth(t);
            }
        }

        /// <summary>
        /// 登録された全HealthComponentのイベント購読を解除します。
        /// </summary>
        public void Unsubscribe()
        {
            foreach (var health in subscribedHealthComponents)
            {
                if (health == null) continue;
                health.Died -= HandleHealthDied;
                health.StateChanged -= HandleHealthStateChanged;
            }

            subscribedHealthComponents.Clear();
            subscriptionsActive = false;
            sceneRegistry = null;
        }

        private void SubscribeHealth(CombatantMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            HealthComponent health = marker.Health;
            if (health == null || subscribedHealthComponents.Contains(health))
            {
                return;
            }

            subscribedHealthComponents.Add(health);
            health.Died += HandleHealthDied;
            health.StateChanged += HandleHealthStateChanged;
        }

        private void HandleHealthDied()
        {
            HealthComponent playerHealth = sceneRegistry.Player.Health;

            if (!playerHealth.IsAlive)
            {
                DefeatConditionMet?.Invoke();
            }
        }

        private void HandleHealthStateChanged(HealthState nextState)
        {
            if (nextState != HealthState.Removed)
            {
                return;
            }

            foreach (var health in subscribedHealthComponents)
            {
                if (health.State != HealthState.Removed)
                {
                    continue;
                }

                CombatantMarker marker = health.Marker;
                if (marker.Faction != CombatantMarker.CombatantFaction.Enemy || !sceneRegistry.IsRegistered(marker))
                {
                    continue;
                }

                sceneRegistry.Unregister(marker);
                break;
            }

            if (sceneRegistry.ActiveEnemyCount == 0)
            {
                VictoryConditionMet?.Invoke();
            }
        }
    }
}
