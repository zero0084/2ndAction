using Unity.Netcode;

// マルチプレイPhase 3(CO-OP/VERSUS)の拡張点。Phase 2.5の段階では既定の挙動(HP0=その人のRun終了)のまま。
public partial class NetMatch
{
    void OnSceneResetPhase3() { }
    void UpdatePhase3Host(GameManager gm, PlayerController pc) { }
    void UpdatePhase3Client(GameManager gm, PlayerController pc) { }
    void WritePhase3Header(FastBufferWriter w) { }
    void ReadPhase3Header(FastBufferReader r) { }
    void ReadRunOver(FastBufferReader r) { }
    void OnRevivedClient(byte downPn, byte donorPn) { }
    void HostHandleReviveRequest(int requesterPn, int downPn) { }
    bool HandleHpZeroPhase3(Rec rec, string source) => false;
    bool ApplyLocalStatePhase3(GameManager gm, PlayerController pc) => false;
}
