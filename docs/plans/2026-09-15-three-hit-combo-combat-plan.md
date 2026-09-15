# プレイヤー3段コンボ攻撃 実装計画書 (3-Hit Combo Implementation Plan)

> **For Antigravity:** REQUIRED WORKFLOW: Use `.agent/workflows/execute-plan.md` to execute this plan in single-flow mode.

**Goal:** プレイヤー（Knight）の攻撃を単一の縦斬りから「第1段：横薙ぎ」「第2段：縦斬り」「第3段：突進刺突」の三段連撃システム（3-Hit Combo）へ拡張し、動能感（Kinetics）・判定・エフェクト・テストを完備する。

**Architecture:** 
- `Rig_Medium` 骨骼階層に適合した新規アニメーションクリップ `Slash_Horizontal.anim` および `Thrust.anim` を生成・配備。
- `CharacterCombat.controller` に `ComboIndex` パラメータと3段の攻撃ステート（`Attack_Horizontal`, `Attack_Vertical`, `Attack_Thrust`）を構築。
- `ComboAttackConfigSO` によるデータ駆動設定（段数ごとのダメージ・射程・踏み込み距離・速度倍率）。
- `PlayerCombatController` によるコンボ状態機械（進行・先行入力受付・タイムアウトリセット）。

**Tech Stack:** Unity 6000.0.38f1, C# (.NET Standard 2.1), NUnit (EditMode / PlayMode), Unity Animator / AnimationUtility.

---

### Task 1: 新規アニメーションクリップの作成 (`Slash_Horizontal.anim` & `Thrust.anim`)

**Files:**
- Create: `Assets/Animations/Clips/Slash_Horizontal.anim`
- Create: `Assets/Animations/Clips/Thrust.anim`
- Test: `Assets/Tests/EditMode/Combat/ComboAnimationClipTests.cs`

**Step 1: 失敗するテストを作成**
`Assets/Tests/EditMode/Combat/ComboAnimationClipTests.cs` を作成し、両クリップが存在し、`Rig_Medium` の主要ボーン（`chest`, `upperarm.r`, `lowerarm.r`, `wrist.r`）に対する回転・位置カーブを保持していることを検証するテストを記述。

**Step 2: テストを実行して失敗を確認**
`execute_code` または NUnit でテストを実行し、クリップ未作成のため FAIL することを確認。

**Step 3: アニメーションクリップを生成**
Unity Editorスクリプト（`execute_code`）を使用し、`Rig_Medium` の骨骼に正確にバインドされたキーフレームカーブを持つ `Slash_Horizontal.anim`（水平薙ぎ払い）と `Thrust.anim`（前方突き刺し）を生成・保存。

**Step 4: テストを実行して成功を確認**
テストを実行し、全カーブバインディングが PASS することを確認。

**Step 5: Gitコミット**
```bash
git add Assets/Animations/Clips/ Assets/Tests/EditMode/Combat/ComboAnimationClipTests.cs
git commit -m "feat(animation): create horizontal slash and thrust animation clips for Rig_Medium"
```

---

### Task 2: Animator Controller のコンボステート構成 (`CharacterCombat.controller`)

**Files:**
- Modify: `Assets/Animations/CharacterCombat.controller`
- Test: `Assets/Tests/EditMode/Combat/CharacterCombatControllerTests.cs`

**Step 1: 失敗するテストを作成**
`CharacterCombat.controller` が `ComboIndex`（Int32）パラメータを持ち、`Attack_Horizontal`、`Attack_Vertical`、`Attack_Thrust` の3つの攻撃ステートと正しい遷移条件を持つことを検証するテストを記述。

**Step 2: テストを実行して失敗を確認**
テストを実行し、未構成ステート・パラメータのため FAIL することを確認。

**Step 3: コントローラーを構成**
`CharacterCombat.controller` を更新：
- パラメータ `ComboIndex` (Int) を追加。
- 3つの攻撃ステートを追加・接続（`ComboIndex == 0`, `1`, `2` で各段へ分岐）。
- 各段の ExitTime（0.65〜0.75）と待機/移動への復帰遷移を整備。

**Step 4: テストを実行して成功を確認**
テストを実行し、ステート機械構造検証が PASS することを確認。

**Step 5: Gitコミット**
```bash
git add Assets/Animations/CharacterCombat.controller Assets/Tests/EditMode/Combat/CharacterCombatControllerTests.cs
git commit -m "feat(animation): configure 3-hit combo states and transitions in CharacterCombat controller"
```

---

### Task 3: コンボ設定アセット (`ComboAttackConfigSO`) の実装

**Files:**
- Create: `Assets/Scripts/Combat/Configs/ComboAttackConfigSO.cs`
- Create: `Assets/Combat/Configs/KnightComboAttackConfig.asset`
- Test: `Assets/Tests/EditMode/Combat/ComboAttackConfigTests.cs`

**Step 1: 失敗するテストを作成**
`ComboAttackConfigSO` が3段分の設定（ダメージ、射程、踏み込み距離、時間、速度倍率、判定終了時間、完了時間）を取得でき、範囲外のインデックスに対して安全なフォールバックを提供することを検証するテストを記述。

**Step 2: テストを実行して失敗を確認**
テストを実行し、クラス未定義のため FAIL することを確認。

**Step 3: 実装**
`ComboAttackConfigSO` を作成：
- `AttackConfigSO` を継承または段数配列を保持。
- 規定パラメータ（第1段: 20ダメ/0.8m突進/1.7倍速, 第2段: 25ダメ/1.2m突進/1.6倍速, 第3段: 40ダメ/2.2m突進/1.5倍速）を設定したアセットを作成。

**Step 4: テストを実行して成功を確認**
テストを実行し、PASS することを確認。

**Step 5: Gitコミット**
```bash
git add Assets/Scripts/Combat/Configs/ComboAttackConfigSO.cs Assets/Combat/Configs/ Assets/Tests/EditMode/Combat/ComboAttackConfigTests.cs
git commit -m "feat(combat): implement ComboAttackConfigSO for multi-step attack definitions"
```

---

### Task 4: `PlayerAnimationDriver` と `PlayerCombatController` のコンボ連携

**Files:**
- Modify: `Assets/Scripts/Player/PlayerAnimationDriver.cs`
- Modify: `Assets/Scripts/Player/PlayerCombatController.cs`
- Test: `Assets/Tests/EditMode/Combat/PlayerCombatComboTests.cs`

**Step 1: 失敗するテストを作成**
`PlayerCombatComboTests.cs` を作成：
- 連続攻撃開始で `ComboIndex` が 0 -> 1 -> 2 -> 0 と進行すること。
- 入力が途絶えて一定時間（0.45秒）経過後に `ComboIndex` が 0 へリセットされること。
- 被撃時（`CancelAttack`）に `ComboIndex` が 0 へリセットされること。
- 各段に応じた踏み込み距離（LungeDistance）およびダメージが適用されること。

**Step 2: テストを実行して失敗を確認**
テストを実行し、単一攻撃仕様のため FAIL することを確認。

**Step 3: 実装**
- `PlayerAnimationDriver`: `SetComboIndex(int index)` メソッドを追加。
- `PlayerCombatController`:
  - `comboIndex` 管理、`comboResetTimeout` タイマー管理。
  - `TryStartAttack` 時に現在のコンボ段数を Animator に反映し、段ごとの Kinetics・ダメージを適用。
  - 攻撃完了時のバッファ先行入力判定により次段へスムーズに接続。

**Step 4: テストを実行して成功を確認**
テストを実行し、PASS することを確認。

**Step 5: Gitコミット**
```bash
git add Assets/Scripts/Player/PlayerAnimationDriver.cs Assets/Scripts/Player/PlayerCombatController.cs Assets/Tests/EditMode/Combat/PlayerCombatComboTests.cs
git commit -m "feat(combat): implement 3-hit combo execution and reset logic in PlayerCombatController"
```

---

### Task 5: プレハブ配備とシーン整合性検証 (`DemoSceneValidator`)

**Files:**
- Modify: `Assets/Prefabs/KayKitBattle/Knight.prefab`
- Modify: `Assets/Scenes/SampleScene.unity`
- Verify: `Assets/Scripts/Validation/DemoSceneValidator.cs`

**Step 1: Knightプレハブとシーンの参照を更新**
Knight の `PlayerCombatController` に `KnightComboAttackConfig.asset` を接続。

**Step 2: シーン検証ツールの実行**
`DemoSceneValidator.ValidateCurrentScene()` を実行し、126項目以上のチェックで「エラー 0件・警告 0件」であることを確認。

**Step 3: 日本語テキスト監査の実行**
新規追加コードのXMLコメント・ログ・例外文がすべて日本語であることを確認。

**Step 4: Gitコミット**
```bash
git add Assets/Prefabs/KayKitBattle/Knight.prefab Assets/Scenes/SampleScene.unity
git commit -m "feat(knight): wire combo configuration to Knight prefab and validate scene integrity"
```

---

### Task 6: PlayMode 統合テストと総合リグレッション検証

**Files:**
- Create: `Assets/Tests/PlayMode/Combat/PlayerComboPlayModeTests.cs`

**Step 1: PlayModeテストの作成**
実際のゲーム実行下で連続左クリックを入力し、以下を検証：
- 第1段（横薙ぎ） -> 第2段（縦斬り） -> 第3段（突進刺突）が正常にAnimatorで再生されること。
- 突進位移が各段で段階的に大きくなること。
- 刀光トレイルおよび効果音が各段で正常にトリガーされること。

**Step 2: 全テストスイートの実行**
EditMode（130+ テスト）および PlayMode（30+ テスト）を全件実行し、100% PASS を確認。

**Step 3: Gitコミット**
```bash
git add Assets/Tests/PlayMode/Combat/PlayerComboPlayModeTests.cs
git commit -m "test(combat): add PlayMode integration tests for 3-hit attack combo sequence"
```
