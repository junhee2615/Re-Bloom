using Fusion;
using System.Collections;
using UnityEngine;

public class TrainController : NetworkBehaviour
{
    [SerializeField] private float moveDistance = 20f;
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float acceleration = 2f;

    [SerializeField, Tooltip("도착 후 로드할 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
    private string nextSceneName = "Stage2";
    [Networked]
    private NetworkBool IsTrainMoving { get; set; }
    [Networked]
    private NetworkBool IsTrainArrived { get; set; }
    private bool parentSet = false;
    private ScreenFade screenFade;

    private bool isMoving = false;
    private bool lastTrainArrived = false;

    private void Start()
    {
        screenFade = FindFirstObjectByType<ScreenFade>();
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        HardwareRig rig = FindFirstObjectByType<HardwareRig>();

        if (rig == null)
            return;

        if (IsTrainMoving && !parentSet)
        {
            parentSet = true;
            rig.SetTrainParent(transform);
        }

        if (!IsTrainMoving && parentSet)
        {
            parentSet = false;
            rig.ClearTrainParent();
        }

        if (IsTrainArrived && !lastTrainArrived)
        {
            lastTrainArrived = true;

            if (screenFade != null)
            {
                StartCoroutine(screenFade.FadeOut(1f));
            }
        }
    }

    public void StartTrain()
    {
        if (!HasStateAuthority || isMoving)
            return;
        IsTrainMoving = true;

        StartCoroutine(MoveTrain());
    }

    private IEnumerator MoveTrain()
    {
        isMoving = true;

        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + Vector3.right * moveDistance;

        float currentSpeed = 0f;

        while (transform.position.x < targetPos.x)
        {
            currentSpeed += acceleration * Runner.DeltaTime;
            currentSpeed = Mathf.Min(currentSpeed, maxSpeed);

            transform.position +=
                Vector3.right * currentSpeed * Runner.DeltaTime;

            yield return null;
        }

        transform.position = targetPos;

        isMoving = false;
        IsTrainMoving = false;
        IsTrainArrived = true;

        if (HasStateAuthority)
        {
            StartCoroutine(LoadNextStage());
        }
    }

    private IEnumerator LoadNextStage()
    {
        yield return new WaitForSeconds(1f); // FadeOut 시간

        if (!HasStateAuthority)
            yield break;

        // 빌드 인덱스는 Scene List에 씬을 추가하면 밀리므로 이름으로 찾는다.
        SceneRef next = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetSceneRef(nextSceneName)
            : SceneRef.None;

        if (next == SceneRef.None)
        {
            Debug.LogError($"[TrainController] 씬 '{nextSceneName}'을 찾을 수 없습니다. Build Profiles > Scene List를 확인하세요.", this);
            yield break;
        }

        Runner.LoadScene(next);
    }
}
