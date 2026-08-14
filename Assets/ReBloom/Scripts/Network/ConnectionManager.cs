using UnityEngine;

public class ConnectionManager : MonoBehaviour
{
    public void CreateRoom()
    {
        // Host로 입장하는 플레이어의 Role : mental
        RoleManager.SetLocalRole(Role.mental);

        NetworkManager.Instance.CreateSession("TestRoom");
    }

    // StartScene의 JoinBtn.OnClick에 연결되어 있다.
    public void JoinRoom()
    {
        // Client로 입장하는 플레이어의 Role : ear
        RoleManager.SetLocalRole(Role.ear);

        NetworkManager.Instance.JoinSession("TestRoom");
    }
}


