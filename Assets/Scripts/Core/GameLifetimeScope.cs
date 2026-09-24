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
        private DamageService damageService;
        private GameFlowController gameFlowController;
        private GameplayClock gameplayClock;
        private SceneReferenceRegistry sceneRegistry;
        private CombatFeedbackController feedbackController;
        private HitStopController hitStopController;
        private CombatAudioController audioController;
        private CombatVfxController vfxController;
        private CombatDeathAudioRouter deathAudioRouter;
        private CombatCameraFeedback cameraFeedback;
        private CombatTimeSlowController timeSlowController;

        private GameSettingsService settingsService;

        public DamageService DamageService => damageService;
        public GameFlowController GameFlowController => gameFlowController;
        public GameplayClock GameplayClock => gameplayClock;
        public SceneReferenceRegistry SceneRegistry => sceneRegistry;
        public CombatFeedbackController FeedbackController => feedbackController;
        public HitStopController HitStopController => hitStopController;
        public CombatAudioController AudioController => audioController;
        public CombatVfxController VfxController => vfxController;
        public CombatDeathAudioRouter DeathAudioRouter => deathAudioRouter;
        public CombatCameraFeedback CameraFeedback => cameraFeedback;
        public CombatTimeSlowController TimeSlowController => timeSlowController;
        public GameSettingsService SettingsService => settingsService;

        protected override void Awake()
        {
            damageService = GetComponent<DamageService>();
            gameFlowController = GetComponent<GameFlowController>();
            gameplayClock = GetComponent<GameplayClock>();
            sceneRegistry = GetComponent<SceneReferenceRegistry>();
            feedbackController = GetComponent<CombatFeedbackController>();
            hitStopController = GetComponent<HitStopController>();
            audioController = GetComponent<CombatAudioController>();
            vfxController = GetComponent<CombatVfxController>();
            deathAudioRouter = GetComponent<CombatDeathAudioRouter>();
            cameraFeedback = GetComponent<CombatCameraFeedback>();
            timeSlowController = GetComponent<CombatTimeSlowController>();
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
                audioController,
                vfxController,
                deathAudioRouter,
                cameraFeedback,
                timeSlowController,
                settingsService);

            // 階層内のコンポーネントに対するDI注入登録
            builder.RegisterComponentInHierarchy<PlayerCombatController>();
            builder.RegisterComponentInHierarchy<PlayerController>();
            builder.RegisterComponentInHierarchy<FirstPersonCameraController>();
            builder.RegisterComponentInHierarchy<FirstPersonViewmodelController>();
            builder.RegisterComponentInHierarchy<DemoHudController>();
            builder.RegisterComponentInHierarchy<SettingsDialogController>();
            builder.RegisterComponentInHierarchy<EnemyMeleeCombat>();
            builder.RegisterComponentInHierarchy<EnemyBrain>();
            builder.RegisterComponentInHierarchy<EnemyLifecycle>();
            builder.RegisterComponentInHierarchy<HitStopParticipant>();
            builder.RegisterComponentInHierarchy<CombatAttackFeedback>();
            builder.RegisterComponentInHierarchy<CameraInputReader>();
        }

        public void ConfigureServices(
            IContainerBuilder builder,
            DamageService damage,
            GameFlowController flow,
            GameplayClock clock,
            SceneReferenceRegistry registry,
            CombatFeedbackController feedback = null,
            HitStopController hitStop = null,
            CombatAudioController audio = null,
            CombatVfxController vfx = null,
            CombatDeathAudioRouter deathAudio = null,
            CombatCameraFeedback camFeedback = null,
            CombatTimeSlowController timeSlow = null,
            GameSettingsService settings = null)
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

            if (hitStop != null)
            {
                builder.RegisterComponent(hitStop);
            }

            if (audio != null)
            {
                builder.RegisterComponent(audio);
            }

            if (vfx != null)
            {
                builder.RegisterComponent(vfx);
            }

            if (deathAudio != null)
            {
                builder.RegisterComponent(deathAudio);
            }

            if (camFeedback != null)
            {
                builder.RegisterComponent(camFeedback);
            }

            if (timeSlow != null)
            {
                builder.RegisterComponent(timeSlow);
            }

            if (settings != null)
            {
                builder.RegisterInstance(settings);
            }
            else
            {
                builder.Register<ISettingsStorage, PlayerPrefsSettingsStorage>(Lifetime.Singleton);
                builder.Register<GameSettingsService>(Lifetime.Singleton);
            }
        }
    }
}
