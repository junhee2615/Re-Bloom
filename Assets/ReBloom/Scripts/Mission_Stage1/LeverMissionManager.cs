using UnityEngine;
using Fusion;

public class LeverMissionManager : NetworkBehaviour
{
    public LeverSwitch leverA;
    public LeverSwitch leverB;
    public GameObject clearText;
    [Networked]
    public NetworkBool IsMissionClear { get; set; } // 미션 클리어 상태 공유

    void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        if (clearText != null)
            clearText.SetActive(IsMissionClear);

        if (!HasStateAuthority)
            return;

        if (IsMissionClear)
            return;

        // 두 레버 활성화 확인
        if (leverA.isActivated && leverB.isActivated)
        {
            IsMissionClear = true;
        }
    }
}