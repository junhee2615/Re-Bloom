using UnityEngine;

public class PersistentVRSystem : MonoBehaviour
{
    private static PersistentVRSystem instance;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
