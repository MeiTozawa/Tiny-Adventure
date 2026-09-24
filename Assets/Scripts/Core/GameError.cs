namespace TinyAdventure
{
    /// <summary>
    /// ゲーム実行時に発生し得る回復可能エラーの領域列挙体です。
    /// 致命的な設定不備や依存欠損はこれを用いず、Assert で即時中断してください。
    /// </summary>
    public enum GameError
    {
        None = 0,

        // 共通エラー
        InvalidParameter,
        InvalidState,
        AlreadyExecuted,

        // ゲームフロー
        StateAlreadyTerminal,
        StateTransitionRejected,
        RestartNotAllowed,

        // 戦闘判定・ダメージシステム
        CombatantNotRegistered,
        TargetDead,
        TargetUnavailable,
        InvalidFactionPair,
        AttackWindowClosed,
        OutOfRange,
        DuplicateHitInSequence,
        DamageRejected,

        // プレイヤー・アクション
        ActionNotRequested,
        ActionCooldownActive,
        ActionInProgress,
        RecoveryInProgress,
        NoGroundFound,

        // 敵 AI・動作
        EnemyTargetLost,
        EnemyAttackUnavailable,
        EnemyNavMeshFailure
    }
}
