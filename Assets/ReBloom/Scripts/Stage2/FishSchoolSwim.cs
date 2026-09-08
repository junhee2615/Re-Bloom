using System.Collections.Generic;
using UnityEngine;

public class FishSchoolSwim : MonoBehaviour
{
    [Header("이동 속도")]
    [SerializeField] private float minSpeed = 0.12f;
    [SerializeField] private float maxSpeed = 0.30f;

    [Header("배회 범위")]
    [SerializeField] private float wanderRadius = 2.5f;
    [SerializeField] private float targetReachDistance = 0.25f;
    [SerializeField] private float minTargetChangeTime = 2f;
    [SerializeField] private float maxTargetChangeTime = 5f;

    [Header("방향 전환")]
    [SerializeField] private float turnSpeed = 1.8f;

    [Header("헤엄 애니메이션")]
    [Tooltip("몸을 좌우로 흔드는 최대 각도")]
    [SerializeField] private float swimWiggleAngle = 15f;

    [Tooltip("가장 느린 몸 흔들림 속도")]
    [SerializeField] private float minSwimWiggleSpeed = 1.5f;

    [Tooltip("가장 빠른 몸 흔들림 속도")]
    [SerializeField] private float maxSwimWiggleSpeed = 2.8f;

    [Tooltip("몸을 살짝 기울이는 Roll 각도")]
    [SerializeField] private float swimRollAngle = 4f;

    [Header("위아래 움직임")]
    [SerializeField] private float verticalBobAmount = 0.035f;
    [SerializeField] private float verticalBobSpeed = 1.2f;

    [Header("카메라 회피")]
    [Tooltip("비우면 Main Camera를 자동으로 사용")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("이 거리 안으로 들어오면 카메라를 피한다")]
    [SerializeField] private float cameraAvoidDistance = 1.5f;

    [Tooltip("카메라에서 물고기가 도망갈 목표 거리")]
    [SerializeField] private float cameraEscapeDistance = 2f;

    [Tooltip("카메라를 피할 때 방향 전환 속도 배율")]
    [SerializeField] private float cameraAvoidTurnMultiplier = 2f;

    [Header("Terrain 충돌 방지")]
    [Tooltip("Inspector에서 Teleport 레이어를 선택")]
    [SerializeField] private LayerMask terrainLayer;

    [Tooltip("Terrain에서 유지할 최소 높이")]
    [SerializeField] private float groundClearance = 0.15f;

    [SerializeField] private float groundCheckHeight = 2f;

    [Tooltip("진행 방향 Terrain 감지 거리")]
    [SerializeField] private float obstacleCheckDistance = 0.45f;

    [SerializeField] private float obstacleCheckRadius = 0.08f;


    private class FishData
    {
        public Transform mover;
        public Transform visual;

        public Vector3 startPosition;
        public Vector3 targetPosition;

        public Quaternion visualBaseLocalRotation;

        public float speed;
        public float nextTargetChangeTime;

        public float wiggleSpeed;
        public float wigglePhase;

        public float bobPhase;
    }

    private readonly List<FishData> fishes =
        new List<FishData>();


    // =================================================
    // 초기화
    // =================================================

    private void Start()
    {
        fishes.Clear();

        if (cameraTransform == null &&
            Camera.main != null)
        {
            cameraTransform =
                Camera.main.transform;
        }

        List<Transform> originalFish =
            new List<Transform>();

        foreach (Transform child in transform)
        {
            originalFish.Add(child);
        }

        foreach (Transform fishVisual in originalFish)
        {
            if (fishVisual == null)
                continue;

            CreateFish(fishVisual);
        }
    }


    private void CreateFish(Transform fishVisual)
    {
        Vector3 worldPosition =
            fishVisual.position;

        Quaternion worldRotation =
            fishVisual.rotation;

        GameObject pivotObject =
            new GameObject(
                fishVisual.name + "_SwimPivot");

        Transform pivot =
            pivotObject.transform;

        pivot.SetParent(
            transform,
            true);

        pivot.position =
            worldPosition;

        pivot.rotation =
            worldRotation;

        pivot.localScale =
            Vector3.one;

        fishVisual.SetParent(
            pivot,
            true);

        fishVisual.localPosition =
            Vector3.zero;

        fishVisual.localRotation =
            Quaternion.identity;

        FishData fish =
            new FishData
            {
                mover = pivot,
                visual = fishVisual,

                startPosition =
                    pivot.position,

                visualBaseLocalRotation =
                    fishVisual.localRotation,

                speed =
                    Random.Range(
                        minSpeed,
                        maxSpeed),

                wiggleSpeed =
                    Random.Range(
                        minSwimWiggleSpeed,
                        maxSwimWiggleSpeed),

                wigglePhase =
                    Random.Range(
                        0f,
                        Mathf.PI * 2f),

                bobPhase =
                    Random.Range(
                        0f,
                        Mathf.PI * 2f)
            };

        PickNewTarget(fish);

        fishes.Add(fish);
    }


    // =================================================
    // 업데이트
    // =================================================

    private void Update()
    {
        // Main Camera가 늦게 생성되는 경우 대비
        if (cameraTransform == null &&
            Camera.main != null)
        {
            cameraTransform =
                Camera.main.transform;
        }

        foreach (FishData fish in fishes)
        {
            if (fish.mover == null ||
                fish.visual == null)
            {
                continue;
            }

            UpdateMovement(fish);
            UpdateSwimAnimation(fish);
        }
    }


    // =================================================
    // 물고기 이동
    // =================================================

    private void UpdateMovement(FishData fish)
    {
        Transform mover =
            fish.mover;


        // -----------------------------------------
        // 카메라와의 수평 거리 확인
        // -----------------------------------------

        bool avoidingCamera = false;

        if (cameraTransform != null)
        {
            Vector3 fishFlat =
                new Vector3(
                    mover.position.x,
                    0f,
                    mover.position.z);

            Vector3 cameraFlat =
                new Vector3(
                    cameraTransform.position.x,
                    0f,
                    cameraTransform.position.z);

            Vector3 awayFromCamera =
                fishFlat - cameraFlat;

            float cameraDistance =
                awayFromCamera.magnitude;

            if (cameraDistance <
                    cameraAvoidDistance &&
                cameraDistance > 0.001f)
            {
                avoidingCamera = true;

                Vector3 escapeDirection =
                    awayFromCamera.normalized;

                fish.targetPosition =
                    mover.position +
                    escapeDirection *
                    cameraEscapeDistance;

                fish.targetPosition.y =
                    mover.position.y;

                fish.nextTargetChangeTime =
                    Time.time + 1f;
            }
        }


        // -----------------------------------------
        // 목적지 갱신
        // -----------------------------------------

        Vector3 flatCurrent =
            new Vector3(
                mover.position.x,
                0f,
                mover.position.z);

        Vector3 flatTarget =
            new Vector3(
                fish.targetPosition.x,
                0f,
                fish.targetPosition.z);

        float distanceToTarget =
            Vector3.Distance(
                flatCurrent,
                flatTarget);

        if (!avoidingCamera &&
            (distanceToTarget <= targetReachDistance ||
             Time.time >= fish.nextTargetChangeTime))
        {
            PickNewTarget(fish);
        }


        // -----------------------------------------
        // 목적지 방향
        // -----------------------------------------

        Vector3 direction =
            fish.targetPosition -
            mover.position;

        direction.y = 0f;


        // -----------------------------------------
        // Terrain 전방 검사
        // -----------------------------------------

        Vector3 swimDirection =
            -mover.forward;

        Vector3 castOrigin =
            mover.position +
            Vector3.up *
            groundClearance;

        if (Physics.SphereCast(
            castOrigin,
            obstacleCheckRadius,
            swimDirection,
            out _,
            obstacleCheckDistance,
            terrainLayer,
            QueryTriggerInteraction.Ignore))
        {
            PickNewTarget(fish);

            direction =
                fish.targetPosition -
                mover.position;

            direction.y = 0f;
        }


        // -----------------------------------------
        // 목적지 방향으로 회전
        // -----------------------------------------

        if (direction.sqrMagnitude >
            0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up)
                *
                Quaternion.Euler(
                    0f,
                    180f,
                    0f);

            float currentTurnSpeed =
                avoidingCamera
                    ? turnSpeed *
                      cameraAvoidTurnMultiplier
                    : turnSpeed;

            mover.rotation =
                Quaternion.Slerp(
                    mover.rotation,
                    targetRotation,
                    Time.deltaTime *
                    currentTurnSpeed);
        }


        // -----------------------------------------
        // 실제 이동
        // -----------------------------------------

        swimDirection =
            -mover.forward;

        Vector3 nextPosition =
            mover.position +
            swimDirection *
            fish.speed *
            Time.deltaTime;


        // -----------------------------------------
        // 위아래 작은 움직임
        // -----------------------------------------

        float bob =
            Mathf.Sin(
                Time.time *
                verticalBobSpeed +
                fish.bobPhase)
            *
            verticalBobAmount;

        nextPosition.y +=
            bob *
            Time.deltaTime;


        // -----------------------------------------
        // Terrain 아래로 내려가지 않게 처리
        // -----------------------------------------

        Vector3 rayOrigin =
            nextPosition +
            Vector3.up *
            groundCheckHeight;

        if (Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit groundHit,
            groundCheckHeight * 2f,
            terrainLayer,
            QueryTriggerInteraction.Ignore))
        {
            float minimumY =
                groundHit.point.y +
                groundClearance;

            if (nextPosition.y <
                minimumY)
            {
                nextPosition.y =
                    minimumY;
            }
        }


        mover.position =
            nextPosition;


        // -----------------------------------------
        // 활동 범위를 벗어나면 중심으로 복귀
        // -----------------------------------------

        Vector3 flatPosition =
            new Vector3(
                mover.position.x,
                0f,
                mover.position.z);

        Vector3 flatStart =
            new Vector3(
                fish.startPosition.x,
                0f,
                fish.startPosition.z);

        if (!avoidingCamera &&
            Vector3.Distance(
                flatPosition,
                flatStart) >
            wanderRadius * 1.2f)
        {
            fish.targetPosition =
                fish.startPosition;

            fish.nextTargetChangeTime =
                Time.time + 1f;
        }
    }


    // =================================================
    // 실제 보이는 수영 모션
    // =================================================

    private void UpdateSwimAnimation(
        FishData fish)
    {
        float wave =
            Mathf.Sin(
                Time.time *
                fish.wiggleSpeed *
                Mathf.PI * 2f +
                fish.wigglePhase);

        float yaw =
            wave *
            swimWiggleAngle;

        float roll =
            wave *
            swimRollAngle;

        Quaternion animationRotation =
            Quaternion.Euler(
                0f,
                yaw,
                roll);

        fish.visual.localRotation =
            fish.visualBaseLocalRotation *
            animationRotation;
    }


    // =================================================
    // 새로운 배회 목적지
    // =================================================

    private void PickNewTarget(
        FishData fish)
    {
        Vector2 randomCircle =
            Random.insideUnitCircle *
            wanderRadius;

        Vector3 target =
            fish.startPosition +
            new Vector3(
                randomCircle.x,
                0f,
                randomCircle.y);

        Vector3 rayOrigin =
            target +
            Vector3.up *
            groundCheckHeight;

        if (Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            groundCheckHeight * 2f,
            terrainLayer,
            QueryTriggerInteraction.Ignore))
        {
            target.y =
                Mathf.Max(
                    fish.startPosition.y,
                    hit.point.y +
                    groundClearance);
        }
        else
        {
            target.y =
                fish.startPosition.y;
        }

        fish.targetPosition =
            target;

        fish.nextTargetChangeTime =
            Time.time +
            Random.Range(
                minTargetChangeTime,
                maxTargetChangeTime);
    }
}