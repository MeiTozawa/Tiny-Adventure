using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatSceneWiringTests
    {
        private Scene sampleScene;
        private bool sceneOpenedAdditively;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            sampleScene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            sceneOpenedAdditively = true;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (sceneOpenedAdditively && sampleScene.isLoaded)
            {
                EditorSceneManager.CloseScene(sampleScene, true);
            }
        }

        private GameObject FindInSampleScene(string name)
        {
            if (!sampleScene.isLoaded) return null;

            var roots = sampleScene.GetRootGameObjects();
            foreach (var r in roots)
            {
                if (r.name == name) return r;
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name) return t.gameObject;
                }
            }
            return null;
        }

        [Test]
        public void KnightPrefab_ContainsRequiredCombatFeedbackComponents()
        {
            var knightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Knight.prefab");
            Assert.That(knightPrefab, Is.Not.Null, "找不到 Assets/Prefabs/KayKitBattle/Knight.prefab。");

            var attackFeedback = knightPrefab.GetComponent<CombatAttackFeedback>();
            Assert.That(attackFeedback, Is.Not.Null, "Knight.prefab 缺少 CombatAttackFeedback 组件。修复建议：在 Knight.prefab 根节点挂载 CombatAttackFeedback。");

            var hitStopParticipant = knightPrefab.GetComponent<HitStopParticipant>();
            Assert.That(hitStopParticipant, Is.Not.Null, "Knight.prefab 缺少 HitStopParticipant 组件。修复建议：在 Knight.prefab 根节点挂载 HitStopParticipant。");

            var swordTrail = knightPrefab.GetComponentInChildren<SwordTrailController>(true);
            Assert.That(swordTrail, Is.Not.Null, "Knight.prefab 缺少 SwordTrailController 组件。修复建议：在武器 SwordSocket 节点挂载 SwordTrailController。");

            var viewmodel = knightPrefab.GetComponentInChildren<FirstPersonViewmodelController>(true);
            Assert.That(viewmodel, Is.Not.Null, "Knight.prefab 缺少 FirstPersonViewmodelController 组件。修复建议：在 CameraTarget 下挂载 FirstPersonViewmodel。");

            var trailRenderer = swordTrail.GetComponent<TrailRenderer>();
            Assert.That(trailRenderer, Is.Not.Null, "Knight.prefab 的 SwordTrail 节点缺少 TrailRenderer 组件。");
            Assert.That(trailRenderer.sharedMaterial, Is.Not.Null, "Knight.prefab 的 SwordTrail TrailRenderer 缺少材质。");
            Assert.That(trailRenderer.sharedMaterial.shader, Is.Not.Null, "Knight.prefab 的 SwordTrail TrailRenderer 材质着色器为空。");
            Assert.That(trailRenderer.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), "Knight.prefab 的 SwordTrail TrailRenderer 不能使用错误 Shader。");
        }

        [Test]
        public void EnemyMeleePrefab_ContainsHitStopParticipant()
        {
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab");
            Assert.That(enemyPrefab, Is.Not.Null, "找不到 Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab。");

            var hitStopParticipant = enemyPrefab.GetComponent<HitStopParticipant>();
            Assert.That(hitStopParticipant, Is.Not.Null, "Enemy_Melee.prefab 缺少 HitStopParticipant 组件。修复建议：在 Enemy_Melee.prefab 根节点挂载 HitStopParticipant。");
        }

        [Test]
        public void CombatFeedbackProfileAsset_ExistsAndReferencesAllSevenAudiosAndVfx()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CombatFeedbackProfile>("Assets/Settings/CombatFeedbackProfile.asset");
            Assert.That(profile, Is.Not.Null, "找不到 Assets/Settings/CombatFeedbackProfile.asset 资产。修复建议：创建并配置 CombatFeedbackProfile 资产。");

            // 验证 7 个音频引用
            Assert.That(profile.NormalHit.hitClip, Is.Not.Null, "Profile 缺少普通命中音效 SFX_Hit_Normal。");
            Assert.That(profile.LethalHit.hitClip, Is.Not.Null, "Profile 缺少致死命中音效 SFX_Hit_Lethal。");
            Assert.That(profile.PlayerDeathClip, Is.Not.Null, "Profile 缺少玩家死亡音效 SFX_Player_Die。");
            Assert.That(profile.EnemyDeathClip, Is.Not.Null, "Profile 缺少敌人死亡音效 SFX_Enemy_Die。");
            Assert.That(profile.PlayerHurtClip, Is.Not.Null, "Profile 缺少玩家受击音效 SFX_Player_Hurt。");
            Assert.That(profile.EnemyHurtClip, Is.Not.Null, "Profile 缺少敌人受击音效 SFX_Enemy_Hurt。");
            Assert.That(profile.SwordWhooshClip, Is.Not.Null, "Profile 缺少挥刀音效 SFX_Sword_Whoosh.。");

            // 验证 2 个 VFX Prefab 引用
            Assert.That(profile.NormalHit.impactPrefab, Is.Not.Null, "Profile 缺少普通命中特效 Impact_Normal.prefab。");
            Assert.That(profile.LethalHit.impactPrefab, Is.Not.Null, "Profile 缺少致死命中特效 Impact_Lethal.prefab。");

            var normalPsr = profile.NormalHit.impactPrefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            Assert.That(normalPsr, Is.Not.Null, "Impact_Normal.prefab 缺少 ParticleSystemRenderer。");
            Assert.That(normalPsr.sharedMaterial, Is.Not.Null, "Impact_Normal.prefab 的 ParticleSystemRenderer 缺少材质。");
            Assert.That(normalPsr.sharedMaterial.shader, Is.Not.Null, "Impact_Normal.prefab 材质着色器为空。");
            Assert.That(normalPsr.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), "Impact_Normal.prefab 材质不能使用错误 Shader。");

            var lethalPsr = profile.LethalHit.impactPrefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            Assert.That(lethalPsr, Is.Not.Null, "Impact_Lethal.prefab 缺少 ParticleSystemRenderer。");
            Assert.That(lethalPsr.sharedMaterial, Is.Not.Null, "Impact_Lethal.prefab 的 ParticleSystemRenderer 缺少材质。");
            Assert.That(lethalPsr.sharedMaterial.shader, Is.Not.Null, "Impact_Lethal.prefab 材质着色器为空。");
            Assert.That(lethalPsr.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), "Impact_Lethal.prefab 材质不能使用错误 Shader。");
        }

        [Test]
        public void SampleSceneGameRoot_ContainsSingleInstanceOfAllControllers()
        {
            var gameRoot = FindInSampleScene("GameRoot");
            Assert.That(gameRoot, Is.Not.Null, "SampleScene 中未找到 GameRoot 对象。");

            Assert.That(gameRoot.GetComponents<CombatFeedbackController>().Length, Is.EqualTo(1), "GameRoot 上必须有且仅有一个 CombatFeedbackController。");
            Assert.That(gameRoot.GetComponents<CombatVfxController>().Length, Is.EqualTo(1), "GameRoot 上必须有且仅有一个 CombatVfxController。");
            Assert.That(gameRoot.GetComponents<CombatAudioController>().Length, Is.EqualTo(1), "GameRoot 上必须有且仅有一个 CombatAudioController。");
            Assert.That(gameRoot.GetComponents<CombatDeathAudioRouter>().Length, Is.EqualTo(1), "GameRoot 上必须有且仅有一个 CombatDeathAudioRouter。");
            Assert.That(gameRoot.GetComponents<HitStopController>().Length, Is.EqualTo(1), "GameRoot 上必须有且仅有一个 HitStopController。");
            Assert.That(gameRoot.GetComponents<CombatCameraFeedback>().Length, Is.EqualTo(1), "GameRoot 上必须有且仅有一个 CombatCameraFeedback。");
        }

        [Test]
        public void CameraRig_ContainsImpulseListenerAndNoDirectTransformShake()
        {
            var cmCam = FindInSampleScene("CM_FirstPerson") ?? FindInSampleScene("CM_ThirdPerson");
            Assert.That(cmCam, Is.Not.Null, "SampleScene 中未找到 CM_FirstPerson 虚拟相机。");

            var listener = cmCam.GetComponent<CinemachineImpulseListener>();
            Assert.That(listener, Is.Not.Null, $"{cmCam.name} 缺少 CinemachineImpulseListener 组件。修复建议：在相机上挂载 CinemachineImpulseListener。");

            // 验证不存在直接修改 Transform 的第三方震屏脚本
            var mainCam = FindInSampleScene("MainCamera");
            Assert.That(mainCam, Is.Not.Null, "SampleScene 中未找到 MainCamera。");

            foreach (var comp in mainCam.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name.ToLower();
                Assert.That(typeName.Contains("camerashake") || typeName.Contains("screenshake"), Is.False,
                    $"MainCamera 上不允许挂载直接修改 Transform 的震屏脚本：{comp.GetType().Name}。必须通过 Cinemachine Impulse 实现。");
            }
        }
    }
}
