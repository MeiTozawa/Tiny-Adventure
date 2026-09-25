namespace TinyAdventure
{
    /// <summary>
    /// 攻撃判定ウィンドウ内で対象への命中が確定した際の直接コールバック契約です。
    /// </summary>
    public interface IAttackHitListener
    {
        void OnTargetHit(CombatantMarker target, int sequenceId);
    }
}
