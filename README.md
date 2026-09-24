# Tiny Adventure - First-Person Melee Combat Action Demo

[![Unity](https://img.shields.io/badge/Unity-6000.5.9f1%20URP-black.svg?style=flat&logo=unity)](https://unity.com/)
[![Architecture](https://img.shields.io/badge/Architecture-VContainer%20IoC-blue.svg)](https://vcontainer.hadashikick.jp/)
[![Input](https://img.shields.io/badge/Input%20System-1.20.0-green.svg)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/)
[![Tests](https://img.shields.io/badge/Automated%20Tests-260%20Passed-brightgreen.svg)]()

> Language: [English](README.md) | [简体中文](README_zh.md) | [日本語](README_jp.md)  
> See [INTRODUCTION.md](INTRODUCTION.md) for full codebase architecture details.

---

## 1. Overview and Controls

Tiny Adventure is a first-person melee combat demo made in Unity 6 and URP. You play as a light-armored knight in an arena, fighting melee enemies that use NavMesh navigation and independent movement motors. The combat focuses on fast-paced sword combos and multi-layered hit feedback.

| Input | Key | Action |
| :--- | :--- | :--- |
| Move | W A S D | Omnidirectional movement with viewmodel walking bob |
| Look | Mouse Move | Pitch and yaw camera look with weapon sway inertia |
| Sprint | Left Shift hold | Run faster with increased weapon bobbing frequency |
| Jump | Space | Jump with gravity simulation |
| Attack | Left Click | Light attack; repeated clicks chain into a three-hit combo |
| Settings | Esc or screen gear | In-game settings menu for FOV and mouse sensitivity |
| Restart | R | Reload the arena after victory or defeat |

---

## 2. Core Gameplay Design

### 2.1 First-Person Viewmodel and Locomotion
- **Shadows-Only Body Separation**: The third-person player mesh is set to cast shadows only. Looking down reveals realistic ground shadows without viewmodel clipping or camera near-plane issues. The sword is drawn by a dedicated viewmodel camera rig.
- **Weapon Sway and Bobbing**: Mouse angular velocity and character movement feed into procedural weapon inertia. Turning the camera causes the sword to lag smoothly behind, while walking and sprinting apply smooth sinusoidal bobbing curves.
- **Blade Visuals**: High-dynamic sword trails and blade emission toggle on and off to match active swing and recovery windows.

### 2.2 Three-Hit Combo System
Light attacks advance through three sequential steps with distinct animations, timings, forward lunges, and damage numbers:

| Step | Attack | Damage | Range | Forward Lunge | Animation Speed | Tactical Role |
| :---: | :--- | :---: | :---: | :---: | :---: | :--- |
| 1 | Horizontal Slash | 20 | 2.2m | 0.8m / 0.12s | 1.7x | **Quick Opener**: Fast startup and wide horizontal reach to check distance and poke. |
| 2 | Vertical Slash | 25 | 2.2m | 1.2m / 0.15s | 1.6x | **Mid-Combo Cleave**: Strong downward overhead cut with medium step-in to stagger. |
| 3 | Thrust Finisher | 40 | 2.8m | 2.2m / 0.20s | 1.5x | **High-Burst Finisher**: Long-range lunging stab with highest damage and strong pushback. |

### 2.3 Input Buffering and Combo Reset
- **0.25-Second Input Buffer**: Left clicks during an attack recovery phase are saved in an internal buffer. The next combo strike fires the exact frame the current swing reaches completion.
- **0.45-Second Combo Timeout**: After an attack finishes, the combo resets back to Step 1 if no follow-up attack is input within 0.45 seconds.
- **Safety Interrupts**: Taking damage, dying, or jumping immediately clears active combo progress.

### 2.4 Enemy AI and Movement Separation
- **Brain and Motor Decoupled**: The enemy brain runs tactical state logic (Idle, Chase, PrepareAttack, Attack), while an independent motor script handles smooth rotation, translation, and physical knockback.
- **Anti-Clipping Safe Distance**: Enemies halt their approach between 1.15 and 1.8 meters from the player to prevent overlapping models and face-hugging attacks.
- **Attack Fallback Timeout**: Enemy swings feature a 1.5-second safety timer so locomotion-to-attack crossfades never abort axe damage windows before the swing lands.

### 2.5 Game Flow and Persistent Settings
- **State Machine Gatekeeper**: A central flow controller handles match states (Ready, Running, Victory, Defeat). Inputs and damage submissions lock out instantly on game over.
- **Local JSON Settings**: FOV (60 to 100 degrees) and mouse sensitivity (0.1 to 2.0) are adjusted in-game and saved directly to a local JSON file.

---

## 3. Combat Hit Feel Design

First-person melee combat easily feels floaty or disconnected, while heavy screen shake often induces motion sickness. Tiny Adventure handles hit impact through eight complementary feedback layers:

### 3.1 Local Hit Stop
On weapon impact, attacker and target animator speeds drop to zero for 0.05 to 0.12 seconds. Global time scale is never touched. Background cameras, particles, and physics continue running at full frame rate, creating a solid bite without system-wide stutter.

### 3.2 Spring Camera Trauma and Zero Aim Drift
Taking a hit drives camera orientation with a second-order damped harmonic spring:
- **Pitch Kick**: Hits apply a 2.0 to 3.5 degree upward flinch.
- **Directional Roll**: Right-side attacks tilt the camera left, and left-side attacks tilt right.
- **Instant FOV Punch**: Field of view briefly contracts by 2 degrees and eases back via spring physics.
- **Aim Invariance**: All spring offsets are purely additive at the render level. The player mouse look angle is never altered. Once the oscillation settles, the crosshair points exactly where it started.

### 3.3 Viewmodel Recoil Jolt
Landing a hit or taking damage pushes the viewmodel sword backward by 0.03 meters and downward by 0.04 meters. The weapon springs back into ready stance, conveying the mass of steel in hand.

### 3.4 6D Impulse Shake
Cinemachine impulse listeners add brief, high-frequency spatial vibrations that complement the lower-frequency head flinch.

### 3.5 Directional Physical Knockback
Confirmed hits push the enemy motor along the attack vector with natural ground deceleration. Step 3 thrust applies the strongest pushback.

### 3.6 Material Hit Flash and Directional Sparks
Target materials flash pure white for 0.08 seconds via a custom shader property. Metal sparks shoot out along the contact surface normal, matching the direction of the blade.

### 3.7 Spatial 3D Audio with Pitch Jitter
Swing whooshes, flesh impacts, and fatal strike sounds play on distinct audio channels. Impact sounds apply a random pitch offset of plus or minus 5 to 10 percent to avoid repetitive audio fatigue. Entity death audio is routed so it fires at most once per match.

### 3.8 Tactical Micro Slow Motion
Finisher hits and fatal blows drop global time to 0.2x speed for 0.08 seconds before smoothly easing back to normal speed.

---

## 4. Designer Configuration Assets

All combat balancing and timing parameters are stored as ScriptableObject assets in the project:

| Asset File | Responsibility |
| :--- | :--- |
| `Assets/Combat/Configs/KnightComboAttackConfig.asset` | Player combo damages, ranges, lunge distances and durations, animation speeds, window timings |
| `Assets/Combat/Configs/EnemyAttackConfig.asset` | Enemy damage, range, cooldown, axe swing window open and close timings |
| `Assets/Combat/Configs/KnightStatsConfig.asset` | Player maximum health, movement speeds, jump settings |
| `Assets/Combat/Configs/EnemyStatsConfig.asset` | Enemy maximum health, movement speeds, turn rates |
| `Assets/Combat/Configs/CombatFeedbackProfile.asset` | Hit stop durations, shake strengths, flash lengths, audio clips, particle prefabs |

---

## 5. Technical Specifications

- **Engine**: Unity 6 (6000.5.9f1) URP
- **Dependency Injection**: VContainer 1.19.0 scene-scoped IoC, centralized registrations, no static singletons, no runtime FindObject calls
- **Automated Tests**: 260 tests passing (222 EditMode + 38 PlayMode)
