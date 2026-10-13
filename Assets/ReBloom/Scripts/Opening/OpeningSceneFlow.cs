using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class OpeningSceneFlow : MonoBehaviour
{
    [Header("Test")]
    [SerializeField] private float testDelay = 3f;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(testDelay);

        SceneManager.LoadScene("StartScene", LoadSceneMode.Single);
    }
}