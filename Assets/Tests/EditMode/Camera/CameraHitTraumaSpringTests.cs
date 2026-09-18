using NUnit.Framework;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// カメラ受撃物理トラウマスプリング（CameraHitTraumaSpring）および
    /// 二階減衰調和振動子（DampedSpringOscillator）の単体テストです。
    /// </summary>
    public sealed class CameraHitTraumaSpringTests
    {
        [Test]
        public void DampedSpringOscillator_AddImpulse_ReachesPeakAndSettlesToZero()
        {
            var spring = new DampedSpringOscillator(220f, 28f, 10f);
            Assert.That(spring.IsResting, Is.True, "初期状態では静止している必要があります。");

            spring.AddImpulse(5f);
            Assert.That(spring.IsResting, Is.False, "インパルス印加後は運動状態である必要があります。");

            float peak = 0f;
            float time = 0f;
            const float dt = 0.016f;

            // 0.1秒程度でピークに到達することをサンプリング
            while (time < 0.15f)
            {
                spring.Update(dt);
                if (spring.Position > peak) peak = spring.Position;
                time += dt;
            }

            Assert.That(peak, Is.GreaterThan(0.05f), "インパルスによって正の変位ピークに到達する必要があります。");

            // 0.40秒後には滑らかに静止位置（0）へ収束することを検証
            while (time < 0.45f)
            {
                spring.Update(dt);
                time += dt;
            }

            Assert.That(spring.IsResting, Is.True, "0.45秒後には減衰して静止状態に収束する必要があります。");
            Assert.That(Mathf.Abs(spring.Position), Is.EqualTo(0f).Within(0.001f), "定常位置偏差は0である必要があります。");
        }

        [Test]
        public void CameraHitTraumaSpring_ApplyImpact_FromRight_ProducesNegativeRollAndPositivePitch()
        {
            var trauma = new CameraHitTraumaSpring();
            // 右側からの打撃（受力ベクトルは左方向：X < 0）
            Vector3 impactFromRight = new Vector3(-1f, 0f, 0f);

            trauma.ApplyImpact(impactFromRight, 1.0f);

            // 0.03秒（約2フレーム）前進
            trauma.Update(0.033f);

            Assert.That(trauma.CurrentRoll, Is.LessThan(0f), "右側から殴られた場合、左側へのDutch側傾斜（Roll < 0）が発生する必要があります。");
            Assert.That(trauma.CurrentPitch, Is.GreaterThan(0f), "打撃により頭部の後仰（Pitch > 0）が発生する必要があります。");
        }

        [Test]
        public void CameraHitTraumaSpring_ApplyImpact_FromLeft_ProducesPositiveRollAndPositivePitch()
        {
            var trauma = new CameraHitTraumaSpring();
            // 左側からの打撃（受力ベクトルは右方向：X > 0）
            Vector3 impactFromLeft = new Vector3(1f, 0f, 0f);

            trauma.ApplyImpact(impactFromLeft, 1.0f);

            trauma.Update(0.033f);

            Assert.That(trauma.CurrentRoll, Is.GreaterThan(0f), "左側から殴られた場合、右側へのDutch側傾斜（Roll > 0）が発生する必要があります。");
            Assert.That(trauma.CurrentPitch, Is.GreaterThan(0f), "打撃により頭部の後仰（Pitch > 0）が発生する必要があります。");
        }

        [Test]
        public void CameraHitTraumaSpring_ApplyImpact_FromFront_ProducesDefiniteRollAndPitchShake()
        {
            var trauma = new CameraHitTraumaSpring();
            // 正面からの打撃（受力ベクトルは後方：Z < 0、X == 0）
            Vector3 impactFromFront = new Vector3(0f, 0f, -1f);

            trauma.ApplyImpact(impactFromFront, 1.0f);

            trauma.Update(0.033f);

            Assert.That(Mathf.Abs(trauma.CurrentRoll), Is.GreaterThan(1.0f), "正面からの被弾でも非対称側倒力矩により明確なDutch側傾斜（|Roll| > 1.0）が発生する必要があります。");
            Assert.That(trauma.CurrentPitch, Is.GreaterThan(1.0f), "正面被弾により強力な後仰（Pitch > 1.0）が発生する必要があります。");
            Assert.That(trauma.CurrentTrauma, Is.GreaterThan(0.1f), "被弾により高周波トラウマ（Trauma > 0.1）が蓄積される必要があります。");
        }

        [Test]
        public void CameraHitTraumaSpring_SettlesSmoothlyWithinWindow()
        {
            var trauma = new CameraHitTraumaSpring();
            trauma.ApplyImpact(new Vector3(-0.7f, 0f, -0.7f), 1.5f);

            Assert.That(trauma.IsActive, Is.True, "被弾直後はスプリングが活性化している必要があります。");

            // 0.5秒間更新
            for (int i = 0; i < 35; i++)
            {
                trauma.Update(0.016f);
            }

            Assert.That(trauma.IsActive, Is.False, "0.5秒以内には全スプリングが静止位置へ滑らかに整定する必要があります。");
            Assert.That(Mathf.Abs(trauma.CurrentPitch), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(trauma.CurrentRoll), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(trauma.CurrentFovOffset), Is.LessThan(0.01f));
        }
    }
}
