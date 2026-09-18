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
            controller.ConfigureForTests(timeSource, 0.15f, 0.08f, 0.05f, 0.20f, 0.03f);

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
        public void NormalHit_SetsTimeScaleToConfiguredNormalValue_AndRestoresAfterDuration()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 1);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));

            // 推進到 100.04：仍處於減速持續期
            timeSource.CurrentTime = 100.04;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));

            // 推進到 100.095：進入平滑恢復過渡期（0.08 ~ 0.11）
            timeSource.CurrentTime = 100.095;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.GreaterThan(0.15f));
            Assert.That(Time.timeScale, Is.LessThan(1.0f));

            // 推進到 100.12：完全恢復結束
            timeSource.CurrentTime = 100.12;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void LethalHit_SetsTimeScaleToConfiguredLethalValue_AndRestoresAfterDuration()
        {
            var req = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 2);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 推進到 100.15：仍在致死減速期
            timeSource.CurrentTime = 100.15;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 推進到 100.25：完全恢復結束
            timeSource.CurrentTime = 100.25;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void NonPlayerAttack_DoesNotTriggerTimeSlow()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: false, attackSequenceId: 3);
            controller.Play(req);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void PlayerHurt_DoesNotTriggerTimeSlow()
        {
            // 敵がプレイヤーを攻撃（プレイヤー被弾）
            var reqEnemyAttack = CreateRequest(CombatHitType.Normal, isPlayerAttack: false, isPlayerTarget: true, attackSequenceId: 4);
            controller.Play(reqEnemyAttack);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));

            // 万一 isPlayerAttack が true でも、isPlayerTarget / プレイヤー所属が設定されていれば減速しない
            var reqSelfDamage = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, isPlayerTarget: true, attackSequenceId: 5);
            controller.Play(reqSelfDamage);

            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void MultipleEnemiesHitInSameAttack_TriggersTimeSlowOnlyOnce()
        {
            // 1回目の敵への命中（系列ID: 10）
            var reqEnemy1 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 10, targetOverride: enemyMarker1);
            controller.Play(reqEnemy1);

            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));

            // 同一スイングで2体目の敵に被弾（系列ID: 10、時刻: 100.03）
            timeSource.CurrentTime = 100.03;
            var reqEnemy2 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 10, targetOverride: enemyMarker2);
            controller.Play(reqEnemy2);

            // 2体目の被弾で減速持続時間が再延長されていないことを検証（初回の100.08 + 0.03 = 100.11で終了する）
            timeSource.CurrentTime = 100.095;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.GreaterThan(0.15f));

            timeSource.CurrentTime = 100.12;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void MultipleEnemiesHitInSameAttack_UpgradesLethalIntensity_WithoutExtendingDuration()
        {
            // 敵1に通常命中
            var reqEnemy1 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 20, targetOverride: enemyMarker1);
            controller.Play(reqEnemy1);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));

            // 同一攻撃系列内で敵2に致命命中（撃破）
            timeSource.CurrentTime = 100.02;
            var reqEnemy2 = CreateRequest(CombatHitType.Lethal, isPlayerAttack: true, attackSequenceId: 20, targetOverride: enemyMarker2);
            controller.Play(reqEnemy2);

            // スケールは致命の0.05fへ強化される
            Assert.That(Time.timeScale, Is.EqualTo(0.05f).Within(0.001f));

            // 持続時間は致命持続時間（0.20s）へ延長されず、初回の通常持続（100.08 + 0.03 = 100.11）のまま完了する
            timeSource.CurrentTime = 100.12;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void DistinctAttacks_TriggerTimeSlowForEachAttack()
        {
            // 1回目の攻撃（系列ID: 31）
            var req1 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 31);
            controller.Play(req1);
            Assert.That(controller.IsSlowActive, Is.True);

            // 1回目の減速が完了
            timeSource.CurrentTime = 100.15;
            controller.Tick();
            Assert.That(controller.IsSlowActive, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1.0f).Within(0.001f));

            // 2回目の異なる攻撃系列（系列ID: 32）
            var req2 = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 32);
            controller.Play(req2);
            Assert.That(controller.IsSlowActive, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0.15f).Within(0.001f));
        }

        [Test]
        public void ClearRuntimeState_And_OnDisable_ForcefullyRestoresTimeScaleToOne()
        {
            var req = CreateRequest(CombatHitType.Normal, isPlayerAttack: true, attackSequenceId: 40);
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
