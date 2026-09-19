using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatTimeSlowControllerTests
    {
        private GameObject controllerGo;
        private CombatTimeSlowController controller;
        private StubUnscaledTimeSource timeSource;
        private StubCombatantRegistry registry;

        private GameObject playerGo;
        private GameObject enemyGo1;
        private GameObject enemyGo2;
        private CombatantMarker playerMarker;
        private CombatantMarker enemyMarker1;
        private CombatantMarker enemyMarker2;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatTimeSlowController");
            controller = controllerGo.AddComponent<CombatTimeSlowController>();
            timeSource = new StubUnscaledTimeSource { CurrentTime = 100.0 };
            controller.ConfigureForTests(timeSource, 0.05f, 0.20f, 0.03f);

            registry = new StubCombatantRegistry();

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");
            registry.Register(playerMarker);

            enemyGo1 = new GameObject("Enemy1");
            enemyMarker1 = enemyGo1.AddComponent<CombatantMarker>();
            enemyMarker1.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee_1");
            registry.Register(enemyMarker1);

            enemyGo2 = new GameObject("Enemy2");
            enemyMarker2 = enemyGo2.AddComponent<CombatantMarker>();
            enemyMarker2.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee_2");
            registry.Register(enemyMarker2);
        }

        [TearDown]
        public void TearDown()
        {
            if (controller != null)
            {
                controller.ClearRuntimeState();
            }
            Time.timeScale = 1f;

            if (controllerGo != null)
            {
                Object.DestroyImmediate(controllerGo);
            }
            if (playerGo != null)
            {
                Object.DestroyImmediate(playerGo);
            }
            if (enemyGo1 != null)
            {
                Object.DestroyImmediate(enemyGo1);
            }
            if (enemyGo2 != null)
            {
                Object.DestroyImmediate(enemyGo2);
            }
        }

        private CombatFeedbackRequest CreateRequest(
            CombatHitType hitType,
            bool isPlayerAttack,
            bool isPlayerTarget = false,
            int attackSequenceId = 1,
            CombatantMarker targetOverride = null)
        {
            CombatantMarker source = isPlayerAttack ? playerMarker : enemyMarker1;
            CombatantMarker target = targetOverride != null ? targetOverride : (isPlayerTarget ? playerMarker : enemyMarker1);
            var damage = TestDamageRequestFactory.Create(registry, source, target, 10f, attackSequenceId);
            var key = new FeedbackDeduplicationKey(source, target, attackSequenceId);
            return new CombatFeedbackRequest(
                hitType,
                source,
                target,
                damage,
                target != null ? target.transform.position : Vector3.zero,
                Vector3.forward,
                isPlayerAttack,
                isPlayerTarget,
                key);
        }

        [Test]
        public void NormalHit_DoesNotTriggerTimeSlow_PreservingSixtyFps()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 1);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.False, "通常ヒットではグローバル時間減速を発動せず、60FPSの滑らかさを維持する必要があります。");
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void LethalHit_SetsTimeScaleToConfiguredLethalValue_AndRestoresAfterDuration()
        {
            var req = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 2);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 100.15 まで進行：致命減速期間中
            timeSource.CurrentTime = 100.15;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 100.25 まで進行：完全に復帰完了
            timeSource.CurrentTime = 100.25;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void NonPlayerAttack_DoesNotTriggerTimeSlow()
        {
            var req = CreateRequest(CombatHitType.Lethal, isPlayerAttack: false, attackSequenceId: 3);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void PlayerHurt_DoesNotTriggerTimeSlow()
        {
            // 敵がプレイヤーを攻撃（プレイヤー被弾）
            var reqEnemyAttack = CreateRequest(CombatHitType.Lethal, isPlayerAttack: false, isPlayerTarget: true, attackSequenceId: 4);
            controller.Play(reqEnemyAttack);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));

            // 万一 isPlayerAttack が true でも、isPlayerTarget / プレイヤー所属が設定されていれば減速しない
            var reqSelfDamage = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, isPlayerTarget: true, attackSequenceId: 5);
            controller.Play(reqSelfDamage);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void MultipleLethalHitsInSameAttack_TriggersTimeSlowOnlyOnce()
        {
            // 1回目の敵への致命命中（系列ID: 10）
            var reqEnemy1 = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 10, targetOverride: enemyMarker1);
            controller.Play(reqEnemy1);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 同一スイングで2体目の敵に致命被弾（系列ID: 10、時刻: 100.03）
            timeSource.CurrentTime = 100.03;
            var reqEnemy2 = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 10, targetOverride: enemyMarker2);
            controller.Play(reqEnemy2);

            // 2体目の被弾で減速持続時間が再延長されていないことを検証（初回の100.20 + 0.03 = 100.23で終了する）
            timeSource.CurrentTime = 100.25;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void DistinctAttacks_TriggerTimeSlowForEachLethalAttack()
        {
            // 1回目の致命攻撃（系列ID: 31）
            var req1 = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 31);
            controller.Play(req1);
            Assert.That(controller.IsSlowActive, Is.True);

            // 1回目の減速が完了
            timeSource.CurrentTime = 100.25;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));

            // 2回目の異なる攻撃系列（系列ID: 32）
            var req2 = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 32);
            controller.Play(req2);
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));
        }

        [Test]
        public void ClearRuntimeState_And_OnDisable_ForcefullyRestoresTimeScaleToOne()
        {
            var req = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 40);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.LessThan(1.0f));

            controller.ClearRuntimeState();

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(controller.LastHandledAttackSequenceId, Is.EqualTo(0));
        }
    }
}
