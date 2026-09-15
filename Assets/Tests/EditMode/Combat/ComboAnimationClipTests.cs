using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// コンボ攻撃用の新規アニメーションクリップ（横薙ぎ・突進刺突）の整合性テストです。
    /// </summary>
    public sealed class ComboAnimationClipTests
    {
        private const string HorizontalSlashPath = "Assets/Animations/Clips/Slash_Horizontal.anim";
        private const string ThrustPath = "Assets/Animations/Clips/Thrust.anim";

        [Test]
        public void HorizontalSlashClip_ExistsAndHasValidCurves()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(HorizontalSlashPath);
            Assert.That(clip, Is.Not.Null, $"アニメーションクリップが見つかりません: {HorizontalSlashPath}");
            Assert.That(clip.length, Is.GreaterThan(0.3f), "横薙ぎクリップの再生時間が短すぎます。");

            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.GreaterThan(0), "横薙ぎクリップにアニメーションカーブが存在しません。");

            bool hasArmCurve = bindings.Any(b => b.path.Contains("upperarm.r") || b.path.Contains("chest"));
            Assert.That(hasArmCurve, Is.True, "横薙ぎクリップに右腕または胸部のカーブが含まれていません。");
        }

        [Test]
        public void ThrustClip_ExistsAndHasValidCurves()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ThrustPath);
            Assert.That(clip, Is.Not.Null, $"アニメーションクリップが見つかりません: {ThrustPath}");
            Assert.That(clip.length, Is.GreaterThan(0.3f), "突刺クリップの再生時間が短すぎます。");

            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.That(bindings.Length, Is.GreaterThan(0), "突刺クリップにアニメーションカーブが存在しません。");

            bool hasArmCurve = bindings.Any(b => b.path.Contains("upperarm.r") || b.path.Contains("chest"));
            Assert.That(hasArmCurve, Is.True, "突刺クリップに右腕または胸部のカーブが含まれていません。");
        }
    }
}
