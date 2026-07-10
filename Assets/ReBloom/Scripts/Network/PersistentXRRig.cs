using UnityEngine;

public class PersistentXRRig : MonoBehaviour
{
    private static PersistentXRRig instance;

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
