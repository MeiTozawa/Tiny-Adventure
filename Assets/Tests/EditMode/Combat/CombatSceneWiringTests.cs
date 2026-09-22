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
            Assert.That(knightPrefab, Is.Not.Null, "Assets/Prefabs/KayKitBattle/Knight.prefab が見つかりません。");

            var attackFeedback = knightPrefab.GetComponent<CombatAttackFeedback>();
            Assert.That(attackFeedback, Is.Not.Null, "Knight.prefab に CombatAttackFeedback コンポーネントが不足しています。修正案：Knight.prefab のルートノードに CombatAttackFeedback をアタッチしてください。");

            var hitStopParticipant = knightPrefab.GetComponent<HitStopParticipant>();
            Assert.That(hitStopParticipant, Is.Not.Null, "Knight.prefab に HitStopParticipant コンポーネントが不足しています。修正案：Knight.prefab のルートノードに HitStopParticipant をアタッチしてください。");

            var swordTrail = knightPrefab.GetComponentInChildren<SwordTrailController>(true);
            Assert.That(swordTrail, Is.Not.Null, "Knight.prefab に SwordTrailController コンポーネントが不足しています。修正案：武器の SwordSocket ノードに SwordTrailController をアタッチしてください。");

            var viewmodel = knightPrefab.GetComponentInChildren<FirstPersonViewmodelController>(true);
            Assert.That(viewmodel, Is.Not.Null, "Knight.prefab に FirstPersonViewmodelController コンポーネントが不足しています。修正案：CameraTarget 配下に FirstPersonViewmodel をアタッチしてください。");

            var trailRenderer = swordTrail.GetComponent<TrailRenderer>();
            Assert.That(trailRenderer, Is.Not.Null, "Knight.prefab の SwordTrail ノードに TrailRenderer コンポーネントが不足しています。");
            Assert.That(trailRenderer.sharedMaterial, Is.Not.Null, "Knight.prefab の SwordTrail TrailRenderer にマテリアルが不足しています。");
            Assert.That(trailRenderer.sharedMaterial.shader, Is.Not.Null, "Knight.prefab の SwordTrail TrailRenderer マテリアルのシェーダーが空です。");
            Assert.That(trailRenderer.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), "Knight.prefab の SwordTrail TrailRenderer でエラーシェーダーを使用することはできません。");
        }

        [Test]
        public void EnemyMeleePrefab_ContainsHitStopParticipant()
        {
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab");
            Assert.That(enemyPrefab, Is.Not.Null, "Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab が見つかりません。");

            var hitStopParticipant = enemyPrefab.GetComponent<HitStopParticipant>();
            Assert.That(hitStopParticipant, Is.Not.Null, "Enemy_Melee.prefab に HitStopParticipant コンポーネントが不足しています。修正案：Enemy_Melee.prefab のルートノードに HitStopParticipant をアタッチしてください。");
        }

        [Test]
        public void CombatFeedbackProfileAsset_ExistsAndReferencesAllSevenAudiosAndVfx()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CombatFeedbackProfile>("Assets/Settings/CombatFeedbackProfile.asset");
            Assert.That(profile, Is.Not.Null, "Assets/Settings/CombatFeedbackProfile.asset アセットが見つかりません。修正案：CombatFeedbackProfile アセットを作成して設定してください。");

            // 7つのオーディオ参照を検証
            Assert.That(profile.NormalHit.hitClip, Is.Not.Null, "Profile に通常ヒット効果音 SFX_Hit_Normal が不足しています。");
            Assert.That(profile.LethalHit.hitClip, Is.Not.Null, "Profile に致命ヒット効果音 SFX_Hit_Lethal が不足しています。");
            Assert.That(profile.PlayerDeathClip, Is.Not.Null, "Profile にプレイヤー死亡効果音 SFX_Player_Die が不足しています。");
            Assert.That(profile.EnemyDeathClip, Is.Not.Null, "Profile に敵死亡効果音 SFX_Enemy_Die が不足しています。");
            Assert.That(profile.PlayerHurtClip, Is.Not.Null, "Profile にプレイヤー被弾効果音 SFX_Player_Hurt が不足しています。");
            Assert.That(profile.EnemyHurtClip, Is.Not.Null, "Profile に敵被弾効果音 SFX_Enemy_Hurt が不足しています。");
            Assert.That(profile.SwordWhooshClip, Is.Not.Null, "Profile に剣を振る効果音 SFX_Sword_Whoosh. が不足しています。");

            // 2つの VFX Prefab 参照を検証
            Assert.That(profile.NormalHit.impactPrefab, Is.Not.Null, "Profile に通常ヒットエフェクト Impact_Normal.prefab が不足しています。");
            Assert.That(profile.LethalHit.impactPrefab, Is.Not.Null, "Profile に致命ヒットエフェクト Impact_Lethal.prefab が不足しています。");

            var normalPsr = profile.NormalHit.impactPrefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            Assert.That(normalPsr, Is.Not.Null, "Impact_Normal.prefab に ParticleSystemRenderer が不足しています。");
            Assert.That(normalPsr.sharedMaterial, Is.Not.Null, "Impact_Normal.prefab の ParticleSystemRenderer にマテリアルが不足しています。");
            Assert.That(normalPsr.sharedMaterial.shader, Is.Not.Null, "Impact_Normal.prefab のマテリアルのシェーダーが空です。");
            Assert.That(normalPsr.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), "Impact_Normal.prefab のマテリアルでエラーシェーダーを使用することはできません。");

            var lethalPsr = profile.LethalHit.impactPrefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            Assert.That(lethalPsr, Is.Not.Null, "Impact_Lethal.prefab に ParticleSystemRenderer が不足しています。");
            Assert.That(lethalPsr.sharedMaterial, Is.Not.Null, "Impact_Lethal.prefab の ParticleSystemRenderer にマテリアルが不足しています。");
            Assert.That(lethalPsr.sharedMaterial.shader, Is.Not.Null, "Impact_Lethal.prefab のマテリアルのシェーダーが空です。");
            Assert.That(lethalPsr.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), "Impact_Lethal.prefab のマテリアルでエラーシェーダーを使用することはできません。");
        }

        [Test]
        public void SampleSceneGameRoot_ContainsSingleInstanceOfAllControllers()
        {
            var gameRoot = FindInSampleScene("GameRoot");
            Assert.That(gameRoot, Is.Not.Null, "SampleScene に GameRoot オブジェクトが見つかりません。");

            Assert.That(gameRoot.GetComponents<CombatFeedbackController>().Length, Is.EqualTo(1), "GameRoot には CombatFeedbackController が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<CombatAnimationFeedback>().Length, Is.EqualTo(1), "GameRoot には CombatAnimationFeedback が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<CombatVfxController>().Length, Is.EqualTo(1), "GameRoot には CombatVfxController が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<CombatAudioController>().Length, Is.EqualTo(1), "GameRoot には CombatAudioController が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<CombatDeathAudioRouter>().Length, Is.EqualTo(1), "GameRoot には CombatDeathAudioRouter が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<HitStopController>().Length, Is.EqualTo(1), "GameRoot には HitStopController が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<CombatCameraFeedback>().Length, Is.EqualTo(1), "GameRoot には CombatCameraFeedback が1つのみ存在する必要があります。");
            Assert.That(gameRoot.GetComponents<CombatTimeSlowController>().Length, Is.EqualTo(1), "GameRoot には CombatTimeSlowController が1つのみ存在する必要があります。");
        }

        [Test]
        public void CameraRig_ContainsImpulseListenerAndNoDirectTransformShake()
        {
            var cmCam = FindInSampleScene("CM_FirstPerson");
            Assert.That(cmCam, Is.Not.Null, "SampleScene に CM_FirstPerson 仮想カメラが見つかりません。");

            var listener = cmCam.GetComponent<CinemachineImpulseListener>();
            Assert.That(listener, Is.Not.Null, $"{cmCam.name} に CinemachineImpulseListener コンポーネントが不足しています。修正案：カメラに CinemachineImpulseListener をアタッチしてください。");

            // Transform を直接変更するサードパーティの画面揺れスクリプトが存在しないことを検証
            var mainCam = FindInSampleScene("MainCamera");
            Assert.That(mainCam, Is.Not.Null, "SampleScene に MainCamera が見つかりません。");

            foreach (var comp in mainCam.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name.ToLower();
                Assert.That(typeName.Contains("camerashake") || typeName.Contains("screenshake"), Is.False,
                    $"MainCamera に Transform を直接変更する画面揺れスクリプト（{comp.GetType().Name}）をアタッチすることは禁止されています。Cinemachine Impulse で実装してください。");
            }
        }

        [Test]
        public void KnightPrefab_ContainsExplicitCombatHurtbox()
        {
            var knightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Knight.prefab");
            Assert.That(knightPrefab, Is.Not.Null, "Assets/Prefabs/KayKitBattle/Knight.prefab が見つかりません。");

            var hurtbox = knightPrefab.GetComponentInChildren<CombatHurtbox>(true);
            Assert.That(hurtbox, Is.Not.Null, "Knight.prefab に CombatHurtbox コンポーネントが不足しています。");
            Assert.That(hurtbox.Type, Is.EqualTo(HurtboxType.Torso), "Knight.prefab の Hurtbox は Torso 部位である必要があります。");
            Assert.That(hurtbox.HurtboxCollider, Is.Not.Null, "Knight.prefab の CombatHurtbox に Collider が割り当てられていません。");
            Assert.That(hurtbox.HurtboxCollider.isTrigger, Is.True, "Knight.prefab の Hurtbox Collider は isTrigger = true である必要があります。");
            Assert.That(hurtbox.Owner, Is.EqualTo(knightPrefab.GetComponent<CombatantMarker>()), "CombatHurtbox の Owner が Knight ルートの CombatantMarker に一致しません。");
            Assert.That(hurtbox.TargetHealth, Is.EqualTo(knightPrefab.GetComponent<HealthComponent>()), "CombatHurtbox の TargetHealth が Knight ルートの HealthComponent に一致しません。");
        }

        [Test]
        public void EnemyMeleePrefab_ContainsExplicitCombatHurtbox()
        {
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab");
            Assert.That(enemyPrefab, Is.Not.Null, "Assets/Prefabs/KayKitBattle/Enemy_Melee.prefab が見つかりません。");

            var hurtbox = enemyPrefab.GetComponentInChildren<CombatHurtbox>(true);
            Assert.That(hurtbox, Is.Not.Null, "Enemy_Melee.prefab に CombatHurtbox コンポーネントが不足しています。");
            Assert.That(hurtbox.Type, Is.EqualTo(HurtboxType.Torso), "Enemy_Melee.prefab の Hurtbox は Torso 部位である必要があります。");
            Assert.That(hurtbox.HurtboxCollider, Is.Not.Null, "Enemy_Melee.prefab の CombatHurtbox に Collider が割り当てられていません。");
            Assert.That(hurtbox.HurtboxCollider.isTrigger, Is.True, "Enemy_Melee.prefab の Hurtbox Collider は isTrigger = true である必要があります。");
            Assert.That(hurtbox.Owner, Is.EqualTo(enemyPrefab.GetComponent<CombatantMarker>()), "CombatHurtbox の Owner が Enemy_Melee ルートの CombatantMarker に一致しません。");
            Assert.That(hurtbox.TargetHealth, Is.EqualTo(enemyPrefab.GetComponent<HealthComponent>()), "CombatHurtbox の TargetHealth が Enemy_Melee ルートの HealthComponent に一致しません。");
        }

        [Test]
        public void SampleScene_ContainsSemanticCategoryRoots()
        {
            Assert.That(sampleScene.isLoaded, Is.True, "SampleScene がロードされていません。");
            var rootNames = new HashSet<string>();
            foreach (var r in sampleScene.GetRootGameObjects())
            {
                rootNames.Add(r.name);
            }

            Assert.That(rootNames.Contains("_MANAGEMENT_"), Is.True, "SampleScene に _MANAGEMENT_ ルートが存在しません。");
            Assert.That(rootNames.Contains("_ENVIRONMENT_"), Is.True, "SampleScene に _ENVIRONMENT_ ルートが存在しません。");
            Assert.That(rootNames.Contains("_CHARACTERS_"), Is.True, "SampleScene に _CHARACTERS_ ルートが存在しません。");
            Assert.That(rootNames.Contains("_CAMERAS_"), Is.True, "SampleScene に _CAMERAS_ ルートが存在しません。");
            Assert.That(rootNames.Contains("_UI_"), Is.True, "SampleScene に _UI_ ルートが存在しません。");
        }

        [Test]
        public void SampleScene_ContainsGameLifetimeScopeOnGameRoot()
        {
            var gameRoot = FindInSampleScene("GameRoot");
            Assert.That(gameRoot, Is.Not.Null, "SampleScene に GameRoot が見つかりません。");
            var scope = gameRoot.GetComponent<GameLifetimeScope>();
            Assert.That(scope, Is.Not.Null, "GameRoot に GameLifetimeScope がアタッチされていません。");
        }
    }
}
