using UnityEngine;

public class LeverMissionManager : MonoBehaviour
{
    public LeverSwitch leverA;
    public LeverSwitch leverB;
    public GameObject clearText;
    private bool isMissionClear = false;

    void Update()
    {
        // 이미 클리어했으면 종료
        if (isMissionClear)
            return;

        // 두 레버 모두 활성화 확인
        if (leverA.isActivated && leverB.isActivated)
        {
            isMissionClear = true;

            // 클리어 UI 표시
            if (clearText != null)
            {
                clearText.SetActive(true);
            }
        }
    }
}