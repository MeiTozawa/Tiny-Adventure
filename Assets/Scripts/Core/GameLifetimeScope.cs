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
    [RequireComponent(typeof(DamageService))]
    [RequireComponent(typeof(GameFlowController))]
    [RequireComponent(typeof(GameplayClock))]
    [RequireComponent(typeof(SceneReferenceRegistry))]
    [RequireComponent(typeof(CombatFeedbackController))]
    [RequireComponent(typeof(HitStopController))]
    [RequireComponent(typeof(CombatCameraFeedback))]
    public sealed class GameLifetimeScope : LifetimeScope
    {
        private DamageService damageService;
        private GameFlowController gameFlowController;
        private GameplayClock gameplayClock;
        private SceneReferenceRegistry sceneRegistry;
        private CombatFeedbackController feedbackController;
        private HitStopController hitStopController;
        private CombatCameraFeedback cameraFeedback;
        private GameSettingsService settingsService;

        public DamageService DamageService => damageService;
        public GameFlowController GameFlowController => gameFlowController;
        public GameplayClock GameplayClock => gameplayClock;
        public SceneReferenceRegistry SceneRegistry => sceneRegistry;
        public CombatFeedbackController FeedbackController => feedbackController;
        public HitStopController HitStopController => hitStopController;
        public CombatCameraFeedback CameraFeedback => cameraFeedback;
        public GameSettingsService SettingsService => settingsService;

        protected override void Awake()
        {
            damageService = GetComponent<DamageService>();
            gameFlowController = GetComponent<GameFlowController>();
            gameplayClock = GetComponent<GameplayClock>();
            sceneRegistry = GetComponent<SceneReferenceRegistry>();
            feedbackController = GetComponent<CombatFeedbackController>();
            hitStopController = GetComponent<HitStopController>();
            cameraFeedback = GetComponent<CombatCameraFeedback>();

            if (sceneRegistry != null)
            {
                autoInjectGameObjects ??= new System.Collections.Generic.List<GameObject>();
                if (sceneRegistry.Player != null && !autoInjectGameObjects.Contains(sceneRegistry.Player.gameObject))
                {
                    autoInjectGameObjects.Add(sceneRegistry.Player.gameObject);
                }
                foreach (var enemy in sceneRegistry.ConfiguredEnemies)
                {
                    if (enemy != null && !autoInjectGameObjects.Contains(enemy.gameObject))
                    {
                        autoInjectGameObjects.Add(enemy.gameObject);
                    }
                }
            }

            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            ConfigureServices(
                builder,
                damageService,
                gameFlowController,
                gameplayClock,
                sceneRegistry,
                feedbackController,
                hitStopController,
                cameraFeedback,
                settingsService);

            // 階層内のコンポーネントに対するDI注入登録（シーン内に単一存在するオブジェクト群）
            builder.RegisterComponentInHierarchy<PlayerCombatController>();
            builder.RegisterComponentInHierarchy<PlayerController>();
            builder.RegisterComponentInHierarchy<FirstPersonCameraController>();
            builder.RegisterComponentInHierarchy<FirstPersonViewmodelController>().As<IPlayerViewmodel>().AsSelf();
            builder.RegisterComponentInHierarchy<DemoHudController>();
            builder.RegisterComponentInHierarchy<SettingsDialogController>();
            builder.RegisterComponentInHierarchy<CameraInputReader>();
            builder.RegisterComponentInHierarchy<InputReader>();
        }

        public void ConfigureServices(
            IContainerBuilder builder,
            DamageService damage,
            GameFlowController flow,
            GameplayClock clock,
            SceneReferenceRegistry registry,
            CombatFeedbackController feedback = null,
            HitStopController hitStop = null,
            CombatCameraFeedback camFeedback = null,
            GameSettingsService settings = null)
        {
            if (damage != null)
            {
                builder.RegisterComponent(damage).As<DamageService>().As<IDamageService>().As<IDamageFeedbackSource>();
            }

            if (flow != null)
            {
                builder.RegisterComponent(flow).As<GameFlowController>().As<IGameplayStateProvider>();
            }

            if (clock != null)
            {
                builder.RegisterComponent(clock)
                    .As<GameplayClock>()
                    .As<IGameplayClock>()
                    .As<ITickable>()
                    .As<IFixedTickable>();
            }

            if (registry != null)
            {
                builder.RegisterComponent(registry).As<SceneReferenceRegistry>().As<ICombatantRegistry>();
            }

            if (feedback != null)
            {
                builder.RegisterComponent(feedback).As<CombatFeedbackController>().As<IHitFeedbackReceiver>();
            }

            if (hitStop != null)
            {
                builder.RegisterComponent(hitStop).As<HitStopController>().As<IHitStopController>().As<ICombatFeedbackModule>();
            }

            if (camFeedback != null)
            {
                builder.RegisterComponent(camFeedback);
            }

            if (settings != null)
            {
                builder.RegisterInstance(settings).As<GameSettingsService>().As<IGameSettingsService>();
            }
            else
            {
                builder.Register<ISettingsStorage, PlayerPrefsSettingsStorage>(Lifetime.Singleton);
                builder.Register<GameSettingsService>(Lifetime.Singleton).As<GameSettingsService>().As<IGameSettingsService>();
            }
        }
    }
}
