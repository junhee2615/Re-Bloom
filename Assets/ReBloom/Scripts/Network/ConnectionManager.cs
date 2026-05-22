using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ConnectionManager : MonoBehaviour
{
    public void CreateRoom()
    {
        NetworkManager.Instance.CreateSession("TestRoom");
    }

    public void JoinRoom()
    {
        NetworkManager.Instance.JoinSession("TestRoom");
    }
}


