using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class HitFlashReceiverTests
    {
        private GameObject targetGo;
        private HitFlashReceiver receiver;
        private MeshRenderer testRenderer;

        [SetUp]
        public void SetUp()
        {
            targetGo = new GameObject("TestTarget");
            var filter = targetGo.AddComponent<MeshFilter>();
            var mesh = new Mesh();
            filter.mesh = mesh;
            testRenderer = targetGo.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            testRenderer.sharedMaterial = mat;

            receiver = targetGo.AddComponent<HitFlashReceiver>();
            receiver.Configure(new Renderer[] { testRenderer }, 0.08f, 0.16f);
        }

        [TearDown]
        public void TearDown()
        {
            if (targetGo != null)
            {
                Object.DestroyImmediate(targetGo);
            }
        }

        [Test]
        public void TriggerFlash_SetsIsFlashing_AndAppliesPropertyBlock()
        {
            Assert.That(receiver.IsFlashing, Is.False);

            receiver.TriggerFlash(CombatHitType.Normal);

            Assert.That(receiver.IsFlashing, Is.True);
            var mpb = new MaterialPropertyBlock();
            testRenderer.GetPropertyBlock(mpb);
            Color emission = mpb.GetColor(Shader.PropertyToID("_EmissionColor"));
            Assert.That(emission.r, Is.GreaterThan(1.0f), "通常ヒット時に高輝度発光が適用される必要があります。");
        }

        [Test]
        public void LethalFlash_AppliesLethalColor()
        {
            receiver.TriggerFlash(CombatHitType.Lethal);

            Assert.That(receiver.IsFlashing, Is.True);
            var mpb = new MaterialPropertyBlock();
            testRenderer.GetPropertyBlock(mpb);
            Color emission = mpb.GetColor(Shader.PropertyToID("_EmissionColor"));
            Assert.That(emission.r, Is.GreaterThan(2.0f), "致命ヒット時に致死発光色が適用される必要があります。");
        }

        [Test]
        public void ResetFlash_ClearsEmissionAndResetsIsFlashing()
        {
            receiver.TriggerFlash(CombatHitType.Normal);
            Assert.That(receiver.IsFlashing, Is.True);

            receiver.ResetFlash();

            Assert.That(receiver.IsFlashing, Is.False);
            var mpb = new MaterialPropertyBlock();
            testRenderer.GetPropertyBlock(mpb);
            Assert.That(mpb.isEmpty, Is.True, "リセット後に PropertyBlock がクリアされている必要があります。");
        }
    }
}
