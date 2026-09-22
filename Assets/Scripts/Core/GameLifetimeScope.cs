using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TinyAdventure
{
    /// <summary>
    /// シーン管理のルート（_MANAGEMENT_/GameRoot）に配置する VContainer のシーンスコープです。
    /// シーン内のコア単例サービスを DI コンテナへ登録し、各コンポーネントへの依存注入を提供します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [Header("シーンサービス参照")]
        [SerializeField]
        private DamageService damageService;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        [SerializeField]
        private SceneReferenceRegistry sceneRegistry;

        [SerializeField]
        private CombatFeedbackController feedbackController;

        public DamageService DamageService => damageService;
        public GameFlowController GameFlowController => gameFlowController;
        public GameplayClock GameplayClock => gameplayClock;
        public SceneReferenceRegistry SceneRegistry => sceneRegistry;
        public CombatFeedbackController FeedbackController => feedbackController;

        protected override void Configure(IContainerBuilder builder)
        {
            if (damageService == null)
            {
                damageService = GetComponent<DamageService>();
            }

            if (gameFlowController == null)
            {
                gameFlowController = GetComponent<GameFlowController>();
            }

            if (gameplayClock == null)
            {
                gameplayClock = GetComponent<GameplayClock>();
            }

            if (sceneRegistry == null)
            {
                sceneRegistry = GetComponent<SceneReferenceRegistry>();
            }

            if (feedbackController == null)
            {
                feedbackController = GetComponent<CombatFeedbackController>();
            }

            ConfigureServices(builder, damageService, gameFlowController, gameplayClock, sceneRegistry, feedbackController);
            builder.RegisterComponentInHierarchy<PlayerCombatController>();
        }

        public void ConfigureServices(
            IContainerBuilder builder,
            DamageService damage,
            GameFlowController flow,
            GameplayClock clock,
            SceneReferenceRegistry registry,
            CombatFeedbackController feedback = null)
        {
            if (damage != null)
            {
                builder.RegisterComponent(damage).As<DamageService>().As<IDamageFeedbackSource>();
            }

            if (flow != null)
            {
                builder.RegisterComponent(flow).As<GameFlowController>().As<IGameplayStateProvider>();
            }

            if (clock != null)
            {
                builder.RegisterComponent(clock).As<GameplayClock>().As<IGameplayClock>();
            }

            if (registry != null)
            {
                builder.RegisterComponent(registry).As<SceneReferenceRegistry>().As<ICombatantRegistry>();
            }

            if (feedback != null)
            {
                builder.RegisterComponent(feedback);
            }
        }
    }
}
