using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 出刀運動学（ViewmodelAttackKinetics）および ScriptableObject（ViewmodelAttackKineticsConfig）の
    /// データ駆動化、[SerializeField] 排除、動的設定注入を検証するテストスイートです。
    /// </summary>
    public sealed class ViewmodelAttackKineticsConfigTests
    {
        [Test]
        public void ViewmodelAttackKinetics_ContainsNoSerializedFields()
        {
            var fields = typeof(ViewmodelAttackKinetics).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var field in fields)
            {
                var serializeFieldAttr = field.GetCustomAttribute<SerializeField>();
                Assert.That(serializeFieldAttr, Is.Null,
                    $"ViewmodelAttackKinetics のフィールド '{field.Name}' に [SerializeField] が残存しています。データは ScriptableObject で管理される必要があります。");
            }
        }

        [Test]
        public void Config_Defaults_HaveValidPosesAndDuration()
        {
            var config = ScriptableObject.CreateInstance<ViewmodelAttackKineticsConfig>();
            try
            {
                Assert.That(config.BaseAttackDuration, Is.GreaterThan(0.05f));
                Assert.That(config.HitStopJitterAmplitude, Is.GreaterThan(0f));

                var pose0 = config.GetPose(0);
                Assert.That(pose0.IsValid, Is.True, "初段コンボポーズが有効である必要があります。");

                var pose1 = config.GetPose(1);
                Assert.That(pose1.IsValid, Is.True, "第2段コンボポーズが有効である必要があります。");

                var pose2 = config.GetPose(2);
                Assert.That(pose2.IsValid, Is.True, "第3段コンボポーズが有効である必要があります。");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Kinetics_WithoutConfig_UsesRobustDefaults()
        {
            var kinetics = new ViewmodelAttackKinetics(null);

            Assert.That(kinetics.BaseAttackDuration, Is.GreaterThan(0.1f));
            kinetics.TriggerAttack(0, 1f, 0.1f, 0.4f);

            Assert.That(kinetics.IsAttacking, Is.True);
            kinetics.Evaluate(0.05f, out Vector3 offsetPos, out Quaternion offsetRot, out float progress, out bool justCompleted);

            Assert.That(progress, Is.GreaterThan(0f));
            Assert.That(offsetPos, Is.Not.EqualTo(Vector3.zero));
        }

        [Test]
        public void Kinetics_WithConfig_ReflectsConfiguredValues()
        {
            var config = ScriptableObject.CreateInstance<ViewmodelAttackKineticsConfig>();
            config.BaseAttackDuration = 0.25f;
            config.HitStopJitterAmplitude = 0.05f;

            var kinetics = new ViewmodelAttackKinetics(config);

            Assert.That(kinetics.BaseAttackDuration, Is.EqualTo(0.25f));
            Assert.That(kinetics.HitStopJitterAmplitude, Is.EqualTo(0.05f));

            kinetics.TriggerAttack(0, 1f);
            Assert.That(kinetics.AttackDuration, Is.EqualTo(0.25f));

            Object.DestroyImmediate(config);
        }

        [Test]
        public void Kinetics_Configure_DynamicallySwapsConfig()
        {
            var kinetics = new ViewmodelAttackKinetics(null);
            Assert.That(kinetics.Config, Is.Null);

            var config = ScriptableObject.CreateInstance<ViewmodelAttackKineticsConfig>();
            config.BaseAttackDuration = 0.80f;

            kinetics.Configure(config);
            Assert.That(kinetics.Config, Is.SameAs(config));
            Assert.That(kinetics.BaseAttackDuration, Is.EqualTo(0.80f));

            Object.DestroyImmediate(config);
        }
    }
}
