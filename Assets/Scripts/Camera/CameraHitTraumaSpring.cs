using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// 二階減衰調和振動（Damped Harmonic Oscillator）物理スプリングです。
    /// 衝撃・反動・外力インパルスを瞬時に受け止め、物理的な弾性振動を経て滑らかに整定します。
    /// </summary>
    [Serializable]
    public sealed class DampedSpringOscillator
    {
        [SerializeField, Min(1f)]
        private float stiffness;

        [SerializeField, Min(0.1f)]
        private float damping;

        [SerializeField]
        private float maxDisplacement;

        private float currentPosition;
        private float currentVelocity;

        public DampedSpringOscillator() { }

        public DampedSpringOscillator(float stiffness, float damping, float maxDisplacement)
        {
            this.stiffness = Mathf.Max(1f, stiffness);
            this.damping = Mathf.Max(0.1f, damping);
            this.maxDisplacement = Mathf.Max(0.01f, maxDisplacement);
            currentPosition = 0f;
            currentVelocity = 0f;
        }

        /// <summary>現在の変位位置です。</summary>
        public float Position => currentPosition;

        /// <summary>現在の速度です。</summary>
        public float Velocity => currentVelocity;

        /// <summary>剛性（角固有振動数の二乗）です。</summary>
        public float Stiffness
        {
            get => stiffness;
            set => stiffness = Mathf.Max(1f, value);
        }

        /// <summary>減衰係数です。</summary>
        public float Damping
        {
            get => damping;
            set => damping = Mathf.Max(0.1f, value);
        }

        /// <summary>変位がほぼ静止状態であるかを返します。</summary>
        public bool IsResting => Mathf.Abs(currentPosition) < 0.005f && Mathf.Abs(currentVelocity) < 0.08f;

        /// <summary>インパルス速度を瞬時に付加します。</summary>
        public void AddImpulse(float impulseVelocity)
        {
            currentVelocity += impulseVelocity;
            ClampState();
        }

        /// <summary>位置変位を直接設定します。</summary>
        public void Snap(float displacement)
        {
            currentPosition = maxDisplacement > 0f ? Mathf.Clamp(currentPosition + displacement, -maxDisplacement, maxDisplacement) : currentPosition + displacement;
        }

        /// <summary>指定秒数だけ物理状態を前進させます。</summary>
        public void Update(float deltaTime)
        {
            if (deltaTime <= 0f || stiffness <= 0f) return;

            // 安定化のため最大ステップ（0.010s / 100Hz）でサブステップ積分
            float remaining = deltaTime;
            const float maxStep = 0.010f;

            while (remaining > 0f)
            {
                float dt = Mathf.Min(remaining, maxStep);
                remaining -= dt;

                // F = -k * x - c * v
                float springForce = -stiffness * currentPosition;
                float dampingForce = -damping * currentVelocity;
                float acceleration = springForce + dampingForce;

                currentVelocity += acceleration * dt;
                currentPosition += currentVelocity * dt;

                ClampState();
            }

            if (IsResting)
            {
                currentPosition = 0f;
                currentVelocity = 0f;
            }
        }

        /// <summary>状態を初期位置にリセットします。</summary>
        public void Reset()
        {
            currentPosition = 0f;
            currentVelocity = 0f;
        }

        private void ClampState()
        {
            if (maxDisplacement > 0f)
            {
                currentPosition = Mathf.Clamp(currentPosition, -maxDisplacement, maxDisplacement);
            }
        }
    }

    /// <summary>
    /// 現代アクションゲーム水準の方向性カメラ受撃トラウマスプリングです。
    /// 敵の攻撃飛来方向（前後左右）から、頸椎の生理的反発（仰角後仰 Pitch）、
    /// 受力方向への側倒傾斜（Cinemachine Dutch Roll）、および視野角（FOV）の弾性振動を統合管理します。
    /// 正面受撃時の非対称歪みによる首の傾斜・側翻および高周波トラウマ振動（Trauma Jitter）を重畳します。
    /// </summary>
    [Serializable]
    public sealed class CameraHitTraumaSpring
    {
        [Header("受撃スプリング設定")]
        [SerializeField]
        private DampedSpringOscillator pitchSpring;

        [SerializeField]
        private DampedSpringOscillator rollSpring;

        [SerializeField]
        private DampedSpringOscillator yawSpring;

        [SerializeField]
        private DampedSpringOscillator fovSpring;

        [Header("インパルス強度基準")]
        [SerializeField]
        private float pitchImpulseMultiplier;

        [SerializeField]
        private float rollImpulseMultiplier;

        [SerializeField]
        private float yawImpulseMultiplier;

        [SerializeField]
        private float fovImpulseOffset;

        [Header("高周波トラウマ・揺れ設定")]
        [SerializeField]
        private float traumaPerHit;

        [SerializeField]
        private float traumaDecaySpeed;

        [SerializeField]
        private float glancingBlowFactor;

        [SerializeField]
        private float jitterFrequency;

        [SerializeField]
        private float jitterPitchAmplitude;

        [SerializeField]
        private float jitterRollAmplitude;

        [SerializeField]
        private float jitterYawAmplitude;

        private float currentTrauma;
        private float jitterTimer;
        private float alternateLateralSign = 1f;

        private float CurrentJitterPitch
        {
            get
            {
                if (currentTrauma <= 0.001f) return 0f;
                float shake = currentTrauma * currentTrauma;
                return (Mathf.PerlinNoise(jitterTimer * jitterFrequency, 0.15f) * 2f - 1f) * jitterPitchAmplitude * shake;
            }
        }

        private float CurrentJitterRoll
        {
            get
            {
                if (currentTrauma <= 0.001f) return 0f;
                float shake = currentTrauma * currentTrauma;
                return (Mathf.PerlinNoise(jitterTimer * jitterFrequency, 0.45f) * 2f - 1f) * jitterRollAmplitude * shake;
            }
        }

        private float CurrentJitterYaw
        {
            get
            {
                if (currentTrauma <= 0.001f) return 0f;
                float shake = currentTrauma * currentTrauma;
                return (Mathf.PerlinNoise(jitterTimer * jitterFrequency, 0.75f) * 2f - 1f) * jitterYawAmplitude * shake;
            }
        }

        public float CurrentPitch => (pitchSpring != null ? pitchSpring.Position : 0f) + CurrentJitterPitch;
        public float CurrentRoll => (rollSpring != null ? rollSpring.Position : 0f) + CurrentJitterRoll;
        public float CurrentYaw => (yawSpring != null ? yawSpring.Position : 0f) + CurrentJitterYaw;
        public float CurrentFovOffset => fovSpring != null ? fovSpring.Position : 0f;
        public float CurrentTrauma => currentTrauma;

        /// <summary>いずれかのスプリングまたは高周波トラウマが振動中であるかを返します。</summary>
        public bool IsActive =>
            (pitchSpring != null && !pitchSpring.IsResting) ||
            (rollSpring != null && !rollSpring.IsResting) ||
            (yawSpring != null && !yawSpring.IsResting) ||
            (fovSpring != null && !fovSpring.IsResting) ||
            currentTrauma > 0.01f;

        public void EnsureInitialized()
        {
            pitchSpring ??= new DampedSpringOscillator();
            rollSpring ??= new DampedSpringOscillator();
            yawSpring ??= new DampedSpringOscillator();
            fovSpring ??= new DampedSpringOscillator();
        }

        /// <summary>
        /// 外部またはテストから全受撃パラメータを注入・初期化します。
        /// </summary>
        public void Configure(
            DampedSpringOscillator pitch,
            DampedSpringOscillator roll,
            DampedSpringOscillator yaw,
            DampedSpringOscillator fov,
            float pitchMult,
            float rollMult,
            float yawMult,
            float fovOffset,
            float traumaHit,
            float traumaDecay,
            float glancingBlow,
            float jitterFreq,
            float jitterPitch,
            float jitterRoll,
            float jitterYaw)
        {
            pitchSpring = pitch;
            rollSpring = roll;
            yawSpring = yaw;
            fovSpring = fov;
            pitchImpulseMultiplier = pitchMult;
            rollImpulseMultiplier = rollMult;
            yawImpulseMultiplier = yawMult;
            fovImpulseOffset = fovOffset;
            traumaPerHit = traumaHit;
            traumaDecaySpeed = traumaDecay;
            glancingBlowFactor = glancingBlow;
            jitterFrequency = jitterFreq;
            jitterPitchAmplitude = jitterPitch;
            jitterRollAmplitude = jitterRoll;
            jitterYawAmplitude = jitterYaw;
        }

        /// <summary>
        /// プレイヤー局所座標系における受撃方向ベクトルと強度を受け取り、各スプリングへ角動量インパルスおよび瞬間変位を注入します。
        /// </summary>
        /// <param name="localImpactDir">攻撃者からプレイヤーへ向かう局所受力ベクトル（例：右側から被弾した場合は X < 0）</param>
        /// <param name="intensity">衝撃倍率（1.0が標準）</param>
        public void ApplyImpact(Vector3 localImpactDir, float intensity = 1f)
        {
            float safeIntensity = Mathf.Max(0.1f, intensity);
            Vector3 dir = localImpactDir.sqrMagnitude > 0.0001f ? localImpactDir.normalized : Vector3.back;

            // 1. 仰角（Pitch）: 衝撃方向に応じた仰角後仰インパルス
            float pitchForce = pitchImpulseMultiplier * safeIntensity * (0.75f + 0.25f * Mathf.Abs(dir.z));
            pitchSpring?.Snap(pitchForce);

            // 2. 側傾斜（Roll / Dutch）および 偏航（Yaw）:
            // 正面または背面からの受撃（dir.xがほぼ0）でも、人体受撃時の非対称歪みによる首の傾斜・側翻晃動を保証
            float lateralSign;
            float lateralFactor;
            if (Mathf.Abs(dir.x) >= 0.2f)
            {
                lateralSign = Mathf.Sign(dir.x);
                lateralFactor = Mathf.Abs(dir.x);
            }
            else
            {
                alternateLateralSign = -alternateLateralSign;
                lateralSign = alternateLateralSign;
                lateralFactor = glancingBlowFactor;
            }

            float rollForce = lateralSign * lateralFactor * rollImpulseMultiplier * safeIntensity;
            rollSpring?.Snap(rollForce);

            float yawForce = -lateralSign * lateralFactor * yawImpulseMultiplier * safeIntensity;
            yawSpring?.Snap(yawForce);

            // 3. 視野角（FOV）: 衝撃による瞬間的な視野の圧縮と復帰
            fovSpring?.Snap(fovImpulseOffset * safeIntensity);

            // 4. 高周波トラウマ（Trauma Jitter）を蓄積
            currentTrauma = Mathf.Clamp01(currentTrauma + traumaPerHit * safeIntensity);
        }

        /// <summary>
        /// 時間更新。UnscaledDeltaTime等を用いて命中停止（HitStop）中も滑らかに振動更新可能です。
        /// </summary>
        public void Update(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            pitchSpring?.Update(deltaTime);
            rollSpring?.Update(deltaTime);
            yawSpring?.Update(deltaTime);
            fovSpring?.Update(deltaTime);

            if (currentTrauma > 0f)
            {
                currentTrauma = Mathf.Max(0f, currentTrauma - traumaDecaySpeed * deltaTime);
                jitterTimer += deltaTime;
            }
            else
            {
                jitterTimer = 0f;
            }
        }

        /// <summary>
        /// 全スプリングおよび高周波トラウマを静止状態にリセットします。
        /// </summary>
        public void Reset()
        {
            pitchSpring?.Reset();
            rollSpring?.Reset();
            yawSpring?.Reset();
            fovSpring?.Reset();
            currentTrauma = 0f;
            jitterTimer = 0f;
        }
    }
}
