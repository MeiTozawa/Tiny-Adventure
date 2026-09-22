using NUnit.Framework;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    [TestFixture]
    public sealed class GameLifetimeScopeTests
    {
        private GameObject scopeObject;
        private GameLifetimeScope scope;
        private DamageService damageService;
        private GameFlowController flowController;
        private GameplayClock clock;
        private SceneReferenceRegistry registry;
        private CombatFeedbackController feedback;

        [SetUp]
        public void SetUp()
        {
            scopeObject = new GameObject("TestLifetimeScope");
            damageService = scopeObject.AddComponent<DamageService>();
            flowController = scopeObject.AddComponent<GameFlowController>();
            clock = scopeObject.AddComponent<GameplayClock>();
            registry = scopeObject.AddComponent<SceneReferenceRegistry>();
            feedback = scopeObject.AddComponent<CombatFeedbackController>();
            scope = scopeObject.AddComponent<GameLifetimeScope>();
        }

        [TearDown]
        public void TearDown()
        {
            if (scopeObject != null)
            {
                Object.DestroyImmediate(scopeObject);
            }
        }

        [Test]
        public void ConfigureServices_BindsAllCoreServicesToContainer()
        {
            var builder = new ContainerBuilder();
            scope.ConfigureServices(builder, damageService, flowController, clock, registry, feedback);
            var container = builder.Build();

            Assert.That(container.Resolve<DamageService>(), Is.SameAs(damageService));
            Assert.That(container.Resolve<IDamageFeedbackSource>(), Is.SameAs(damageService));
            Assert.That(container.Resolve<GameFlowController>(), Is.SameAs(flowController));
            Assert.That(container.Resolve<IGameplayStateProvider>(), Is.SameAs(flowController));
            Assert.That(container.Resolve<GameplayClock>(), Is.SameAs(clock));
            Assert.That(container.Resolve<IGameplayClock>(), Is.SameAs(clock));
            Assert.That(container.Resolve<SceneReferenceRegistry>(), Is.SameAs(registry));
            Assert.That(container.Resolve<ICombatantRegistry>(), Is.SameAs(registry));
            Assert.That(container.Resolve<CombatFeedbackController>(), Is.SameAs(feedback));
        }
    }
}
