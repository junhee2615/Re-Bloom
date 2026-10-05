using System.Collections;
using Fusion;
using UnityEngine;

/// <summary>
/// PlantCore 의 MentalButton. mental 플레이어가 손으로 누르면
///   씨앗이 떨어짐 → 잠시 뒤 흙으로 빨려 들어감 → 새싹이 자람
/// 순서로 연출한다.
///
/// 네트워크로 공유하는 것은 "눌렸다"(IsPressed) 하나뿐이고, 연출은 각자 로컬에서 재생한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlantCoreSeedButton : NetworkBehaviour
{
    [Networked] public NetworkBool IsPressed { get; set; }

    [Header("연결")]
    [SerializeField] private Transform seeds;
    [SerializeField] private GameObject sprouts;
    [Tooltip("씨앗이 떨어질 때 재생할 소리")]
    [SerializeField] private AudioSource seedDropAudio;

    [Header("씨앗 (Seeds 의 로컬 Y)")]
    [SerializeField] private float dropY = -1.008f;
    [SerializeField] private float dropDuration = 0.35f;
    [Tooltip("떨어진 뒤 빨려 들어가기 전까지 기다리는 시간(초)")]
    [SerializeField] private float waitAfterDrop = 3f;
    [SerializeField] private float sinkY = -1.049f;
    [SerializeField] private float sinkDuration = 2f;

    [Header("새싹")]
    [SerializeField] private float sproutTargetScale = 1f;
    [SerializeField] private float growDuration = 3f;

    private ChangeDetector changes;
    private bool sequenceStarted;

    private bool IsNetworked => Object != null && Object.IsValid;

    public override void Spawned()
    {
        changes = GetChangeDetector(ChangeDetector.Source.SimulationState);

        // 이미 눌린 뒤에 들어온 플레이어는 연출 없이 결과 상태로 맞춘다
        if (IsPressed)
            ApplyFinalState();
    }

    public override void Render()
    {
        foreach (var change in changes.DetectChanges(this))
            if (change == nameof(IsPressed) && IsPressed)
                StartSequence();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Right Controller") && !other.CompareTag("Left Controller"))
            return;

        // mental 역할만 누를 수 있다
        if (!RoleManager.LocalIsMental)
            return;

        Press();
    }

    [ContextMenu("테스트: 버튼 누르기 (Play 모드)")]
    private void Press()
    {
        if (sequenceStarted)
            return;

        if (!IsNetworked)
        {
            // 세션 없이 혼자 테스트할 때
            StartSequence();
            return;
        }

        if (IsPressed)
            return;

        if (HasStateAuthority)
            IsPressed = true;
        else
            RPC_RequestPress();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPress()
    {
        IsPressed = true;
    }

    private void StartSequence()
    {
        if (sequenceStarted)
            return;
        sequenceStarted = true;
        StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        // 1) 씨앗이 위에서 떨어지듯 빠르게 (가속)
        if (seedDropAudio != null)
            seedDropAudio.Play();

        if (seeds != null)
        {
            float startY = seeds.localPosition.y;
            for (float t = 0f; t < dropDuration; t += Time.deltaTime)
            {
                float k = t / dropDuration;
                SetSeedY(Mathf.Lerp(startY, dropY, k * k));
                yield return null;
            }
            SetSeedY(dropY);
        }

        // 2) 잠시 뒤 흙으로 빨려 들어가듯 천천히
        yield return new WaitForSeconds(waitAfterDrop);

        if (seeds != null)
        {
            for (float t = 0f; t < sinkDuration; t += Time.deltaTime)
            {
                SetSeedY(Mathf.Lerp(dropY, sinkY, Mathf.SmoothStep(0f, 1f, t / sinkDuration)));
                yield return null;
            }
            SetSeedY(sinkY);
        }

        // 3) 새싹이 자라듯 천천히 커진다
        if (sprouts != null)
        {
            sprouts.SetActive(true);

            Transform root = sprouts.transform;
            int count = root.childCount;
            Vector3[] startScales = new Vector3[count];
            for (int i = 0; i < count; i++)
                startScales[i] = root.GetChild(i).localScale;

            Vector3 target = Vector3.one * sproutTargetScale;
            for (float t = 0f; t < growDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / growDuration);
                for (int i = 0; i < count; i++)
                    root.GetChild(i).localScale = Vector3.Lerp(startScales[i], target, k);
                yield return null;
            }
            for (int i = 0; i < count; i++)
                root.GetChild(i).localScale = target;
        }
    }

    private void ApplyFinalState()
    {
        sequenceStarted = true;
        SetSeedY(sinkY);

        if (sprouts != null)
        {
            sprouts.SetActive(true);
            foreach (Transform child in sprouts.transform)
                child.localScale = Vector3.one * sproutTargetScale;
        }
    }

    private void SetSeedY(float y)
    {
        if (seeds == null) return;
        Vector3 p = seeds.localPosition;
        p.y = y;
        seeds.localPosition = p;
    }
}
