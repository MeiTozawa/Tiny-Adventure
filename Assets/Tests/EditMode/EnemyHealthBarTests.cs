using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 敵頭上HPバー（EnemyHealthBar）の残量即時反映、緩衝バー遅延追従、フェードイン・フェードアウト、死亡時挙動を検証します。
    /// </summary>
    public sealed class EnemyHealthBarTests
    {
        private sealed class CombatantRegistryStub : ICombatantRegistry
        {
            private readonly HashSet<CombatantMarker> combatants = new();

            public void Register(CombatantMarker combatant)
            {
                if (combatant != null) combatants.Add(combatant);
            }

            public void Unregister(CombatantMarker combatant)
            {
                if (combatant != null) combatants.Remove(combatant);
            }

            public bool IsRegistered(CombatantMarker combatant)
            {
                return combatant != null && combatants.Contains(combatant);
            }
        }

        private CombatantRegistryStub registry;
        private GameObject playerObject;
        private CombatantMarker playerMarker;
        private GameObject enemyObject;
        private CombatantMarker enemyMarker;
        private HealthComponent healthComponent;
        private GameObject healthBarObject;
        private EnemyHealthBar healthBar;
        private CanvasGroup canvasGroup;
        private Image mainFillImage;
        private Image bufferFillImage;

        [SetUp]
        public void SetUp()
        {
            registry = new CombatantRegistryStub();

            playerObject = new GameObject("TestPlayer");
            playerMarker = playerObject.AddComponent<CombatantMarker>();
            playerMarker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");
            registry.Register(playerMarker);

            enemyObject = new GameObject("TestEnemy");
            enemyMarker = enemyObject.AddComponent<CombatantMarker>();
            enemyMarker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            registry.Register(enemyMarker);

            healthComponent = enemyObject.AddComponent<HealthComponent>();
            healthComponent.Configure(100f);
            healthComponent.EnterDemo();

            healthBarObject = new GameObject("TestEnemyHealthBar");
            healthBarObject.transform.SetParent(enemyObject.transform, false);

            canvasGroup = healthBarObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            var mainGo = new GameObject("MainFill");
            mainGo.transform.SetParent(healthBarObject.transform, false);
            mainFillImage = mainGo.AddComponent<Image>();
            mainFillImage.type = Image.Type.Filled;
            mainFillImage.fillAmount = 1f;

            var bufferGo = new GameObject("BufferFill");
            bufferGo.transform.SetParent(healthBarObject.transform, false);
            bufferFillImage = bufferGo.AddComponent<Image>();
            bufferFillImage.type = Image.Type.Filled;
            bufferFillImage.fillAmount = 1f;

            healthBar = healthBarObject.AddComponent<EnemyHealthBar>();
            healthBar.SetDependencies(healthComponent, canvasGroup, mainFillImage, bufferFillImage, 3.5f, 0.25f);
        }

        [TearDown]
        public void TearDown()
        {
            if (healthBarObject != null)
            {
                Object.DestroyImmediate(healthBarObject);
            }
            if (enemyObject != null)
            {
                Object.DestroyImmediate(enemyObject);
            }
            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }
        }

        private void ApplyDamage(float amount)
        {
            var request = DamageRequest.Create(
                registry,
                playerMarker,
                enemyMarker,
                amount,
                1,
                AttackKinds.KnightSword,
                enemyObject.transform.position,
                0d).Value;

            healthComponent.Receive(request);
        }

        [Test]
        public void InitialState_IsFullHealth_AndHidden()
        {
            Assert.That(healthBar.TargetFill, Is.EqualTo(1f).Within(0.001f));
            Assert.That(healthBar.BufferFill, Is.EqualTo(1f).Within(0.001f));
            Assert.That(healthBar.Alpha, Is.EqualTo(0f).Within(0.001f));
            Assert.That(healthBar.IsVisible, Is.False);
        }

        [Test]
        public void DamageTaken_ImmediatelyReducesMainFill_AndKeepsBufferLagging()
        {
            ApplyDamage(30f);

            Assert.That(healthBar.TargetFill, Is.EqualTo(0.70f).Within(0.001f), "メインゲージは被弾時に即時減少する必要があります。");
            Assert.That(healthBar.BufferFill, Is.EqualTo(1.0f).Within(0.001f), "緩衝バーは待機遅延中（0.25秒）は減少してはなりません。");
            Assert.That(mainFillImage.fillAmount, Is.EqualTo(0.70f).Within(0.001f));
        }

        [Test]
        public void DamageTaken_FadesInHealthBar()
        {
            ApplyDamage(20f);

            // 0.2秒アニメーションを進める
            healthBar.UpdateBarAnimation(0.2f);

            Assert.That(healthBar.Alpha, Is.GreaterThan(0.5f), "被弾後はHPバーがフェードインして可視化される必要があります。");
            Assert.That(healthBar.IsVisible, Is.True);
        }

        [Test]
        public void DamageBuffer_CatchesUpAfterDelay()
        {
            ApplyDamage(50f);

            // 遅延時間（0.25秒）を経過させる
            healthBar.UpdateBarAnimation(0.30f);

            // 緩衝バーが追従を開始していることを検証
            Assert.That(healthBar.BufferFill, Is.LessThan(1.0f), "遅延時間経過後は緩衝バーが追従を開始する必要があります。");

            // 十分な時間を進めるとメインゲージに合流する
            healthBar.UpdateBarAnimation(1.0f);
            Assert.That(healthBar.BufferFill, Is.EqualTo(0.50f).Within(0.01f), "十分な追従時間経過後は緩衝バーが現在の残量に合流する必要があります。");
        }

        [Test]
        public void NoDamage_FadesOutAfterShowDuration()
        {
            ApplyDamage(20f);

            // フェードイン完了
            healthBar.UpdateBarAnimation(0.5f);
            Assert.That(healthBar.Alpha, Is.EqualTo(1.0f).Within(0.01f));

            // 表示継続時間（3.5秒）を経過させる
            healthBar.UpdateBarAnimation(3.6f);

            // フェードアウトが進行していることを検証
            healthBar.UpdateBarAnimation(0.5f);
            Assert.That(healthBar.Alpha, Is.LessThan(0.1f), "非被弾で3.5秒経過後はHPバーがフェードアウトして非表示になる必要があります。");
        }

        [Test]
        public void Died_SetsFillToZero_AndTriggersFadeOut()
        {
            // 致命ダメージ（100HP削り切り）
            ApplyDamage(100f);

            Assert.That(healthBar.TargetFill, Is.EqualTo(0f));
            Assert.That(healthBar.BufferFill, Is.EqualTo(0f));

            // 死亡フェードアウト
            healthBar.UpdateBarAnimation(0.5f);
            Assert.That(healthBar.Alpha, Is.EqualTo(0f).Within(0.01f), "死亡後はHPバーが完全に非表示になる必要があります。");
        }

        [Test]
        public void EnsureSprite_AssignsValidSprite_WhenSpriteIsNull()
        {
            Assert.That(mainFillImage.sprite, Is.Not.Null, "uGUI Filled Image requires a non-null sprite to render fillAmount correctly.");
            Assert.That(bufferFillImage.sprite, Is.Not.Null, "uGUI Filled Image requires a non-null sprite to render fillAmount correctly.");
        }
    }
}
