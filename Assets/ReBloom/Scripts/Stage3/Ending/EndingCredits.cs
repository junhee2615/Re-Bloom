using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 엔딩이 검게 닫힌 뒤 나오는 엔딩 크레딧.
///
///  1. 검은 공간에 크레딧 화면이 페이드인되며 노래가 시작된다.
///  2. 화면 전체에 장면 사진(Stage1 복원 전 → 후, Stage2 전 → 후, Stage3 전 → 후)이 천천히 다가가며 이어지고,
///     왼쪽 검은 번짐 위에 "역할 / 이름"이 하나씩 떠올랐다 사라진다(영화 크레딧처럼, 아래에서 위로 흐르지 않는다).
///  3. 다 나오면 화면이 비워지고 "- The End -"가 떴다가 노래와 함께 사라진다.
///
/// VR이라 화면 공간 UI가 아니라, 보는 사람 앞 <see cref="distance"/>m에 월드 공간 화면을 띄운다.
/// 보는 카메라는 크레딧 레이어만 그리고 배경을 검게 지우므로 주변 월드는 보이지 않는다.
/// UI는 런타임에 코드로 만든다(씬에 따로 배치할 것이 없다).
///
/// <see cref="Stage3EndingCutscene"/>이 엔딩 타임라인이 끝나면 <see cref="Play"/>를 부른다.
/// 장면 이미지는 메뉴 Re_Bloom/Credits/Capture ... 로 찍고, 엔딩 셋업(Apply)이 여기에 넣는다.
/// </summary>
public class EndingCredits : MonoBehaviour
{
    [Serializable]
    public class CreditLine
    {
        public string role;
        public string name;
    }

    // 크레딧만 그리기 위한 비어 있는 레이어. "UI"(5)를 쓰면 손목 인벤토리 같은 다른 월드 UI까지 보인다.
    private const int CreditsLayer = 30;
    private const float CanvasWidth = 1920f;
    private const float CanvasHeight = 1080f;

    // 다음 영상을 겹치기 전에 미리 준비하는 시간(초)
    private const float VideoPrepareLead = 1f;
    private const int VideoWidth = 1920;   // 녹화 해상도 그대로(줄이면 흐려진다)
    private const int VideoHeight = 1080;

    [Header("내용")]
    [Tooltip("차례로 보여 줄 컷씬 영상. Stage1 복원 전, 후, Stage2 전, 후, Stage3 전, 후 순서. " +
             "칸이 비어 있으면 같은 칸의 이미지(Shots)를 대신 보여 준다.")]
    [SerializeField] private VideoClip[] clips = Array.Empty<VideoClip>();

    [Tooltip("영상이 없는 칸에 대신 보여 줄 장면 이미지. 순서는 Clips와 같다.")]
    [SerializeField] private Texture2D[] shots = Array.Empty<Texture2D>();

    [SerializeField] private CreditLine[] lines = Array.Empty<CreditLine>();

    [SerializeField] private string endText = "- The End -";

    [Tooltip("한글이 들어 있는 TextMeshPro 폰트(KoPub Dotum 등).")]
    [SerializeField] private TMP_FontAsset font;

    [Header("음악")]
    [SerializeField] private AudioClip music;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.8f;

    [Header("시간(초)")]
    [SerializeField, Min(0f)] private float fadeIn = 2.5f;
    [Tooltip("장면 하나가 보이는 시간.")]
    [SerializeField, Min(1f)] private float shotDuration = 4f;
    [Tooltip("장면이 다음 장면으로 겹쳐 넘어가는 시간.")]
    [SerializeField, Min(0f)] private float shotCrossfade = 1.5f;
    [Tooltip("장면이 보이는 동안 천천히 다가가는 정도(1 = 그대로).")]
    [SerializeField, Min(1f)] private float shotZoom = 1f;
    [Tooltip("첫 크레딧이 뜨기 전 뜸.")]
    [SerializeField, Min(0f)] private float firstLineDelay = 1.5f;
    [SerializeField, Min(0f)] private float lineFade = 0.8f;
    [SerializeField, Min(0f)] private float lineHold = 2.4f;
    [SerializeField, Min(0f)] private float lineGap = 0.5f;
    [Tooltip("장면과 크레딧이 다 끝나고 화면이 비워지는 시간.")]
    [SerializeField, Min(0f)] private float contentFadeOut = 2f;
    [SerializeField, Min(0f)] private float endFadeIn = 1.5f;
    [SerializeField, Min(0f)] private float endHold = 3.5f;
    [Tooltip("The End와 노래가 함께 사라지는 시간.")]
    [SerializeField, Min(0f)] private float finalFadeOut = 3f;

    [Header("글씨 뒤 검은 번짐")]
    [Tooltip("화면 왼쪽에서 오른쪽으로 옅어지는 어둠의 진하기.")]
    [SerializeField, Range(0f, 1f)] private float leftShadeOpacity = 1f;
    [Tooltip("크레딧 글씨를 감싸는 부드러운 그림자의 진하기.")]
    [SerializeField, Range(0f, 1f)] private float textShadeOpacity = 0.95f;

    [Header("마지막 손 흔들기 컷")]
    [Tooltip("크레딧 마지막에 온실 앞에서 손 흔드는 두 사람을 실시간으로 보여 줄 엔딩. 비우면 사진만 나온다.")]
    [SerializeField] private Stage3EndingCutscene liveShotSource;
    [Tooltip("손 흔들기 컷을 보여 주는 시간(초).")]
    [SerializeField, Min(1f)] private float liveShotDuration = 6f;

    [Header("배치")]
    [Tooltip("장면 사진을 오른쪽으로 미는 거리(크레딧 화면 가로 1920 기준 픽셀).")]
    [SerializeField] private float shotOffsetX = 200f;
    [Tooltip("크레딧 이름이 시작하는 가로 위치(크레딧 화면 왼쪽 끝에서 픽셀, 가로 1920 기준). 클수록 오른쪽.")]
    [SerializeField] private float textLeft = 380f;
    [Tooltip("크레딧 화면을 시선 정면에서 위(+)·아래(-)로 옮기는 거리(m). Play 중에 바꾸면 바로 움직인다.")]
    [SerializeField, Range(-2f, 2f)] private float heightOffset = 1.1f;

    [Tooltip("시선이 크레딧에서 벗어나면 크레딧이 천천히 다시 시선 정면으로 따라온다.")]
    [SerializeField] private bool followGaze = true;
    [Tooltip("시선이 크레딧 가운데에서 이만큼(도) 넘게 벗어나면 따라오기 시작한다. 작게 움직일 때는 가만히 있다.")]
    [SerializeField, Range(5f, 45f)] private float recenterAngle = 15f;
    [Tooltip("따라오는 빠르기. 클수록 빨리 따라온다.")]
    [SerializeField, Range(0.5f, 6f)] private float followSpeed = 2f;

    [Tooltip("보는 사람 앞 거리(m).")]
    [SerializeField, Min(0.5f)] private float distance = 2.6f;
    [Tooltip("크레딧 화면의 가로 크기(m).")]
    [SerializeField, Min(0.5f)] private float width = 3.2f;

    [Header("이벤트")]
    [Tooltip("The End까지 사라져 화면이 완전히 검어진 뒤.")]
    public UnityEvent onCreditsFinished;

    private CanvasGroup rootGroup;
    private CanvasGroup contentGroup;
    private Transform viewerTransform;
    private Transform canvasTransform;
    private Vector3 anchor;   // 높이 조정 전 크레딧 위치(시선 정면)
    private bool recentering;

    // 장면 두 층. 지금 장면과 겹쳐 들어오는 다음 장면을 번갈아 맡는다.
    private readonly List<(VideoClip clip, Texture2D image, bool live)> slots = new List<(VideoClip, Texture2D, bool)>();
    private int liveLayer = -1;
    private RawImage leftShade;
    private RawImage textShade;
    private RawImage[] layerImages;
    private VideoPlayer[] layerPlayers;
    private RenderTexture[] layerTextures;
    private readonly int[] layerSlot = { -1, -1 };
    private CanvasGroup lineGroup;
    private TMP_Text roleText;
    private TMP_Text nameText;
    private CanvasGroup endGroup;
    private AudioSource musicSource;

    public bool IsPlaying { get; private set; }

    /// <summary>
    /// 크레딧을 시작한다. <paramref name="viewer"/>는 지금 화면을 그리는 카메라(VR이면 HMD 카메라).
    /// 첫 프레임 전에 카메라를 크레딧 전용(검은 배경)으로 바꿔 두므로, 호출한 쪽은 바로 검은 화면 덮개를 걷어도 된다.
    /// </summary>
    public void Play(Camera viewer)
    {
        if (IsPlaying || viewer == null) return;

        IsPlaying = true;
        CollectSlots();
        Debug.Log($"[EndingCredits] 크레딧 시작 — 장면 {slots.Count}개(영상 {slots.Count(s => s.clip != null)}개), " +
                  $"크레딧 {lines.Length}줄, 노래 {(music != null ? music.name : "없음")}", this);
        viewerTransform = viewer.transform;
        BuildCanvas(viewer.transform);
        PrepareCamera(viewer);
        StartCoroutine(Run());
    }

    // 시선 정면(위아래 ±30°까지 따름) distance 거리 + 높이 조정
    private Vector3 GazePoint()
    {
        Vector3 forward = viewerTransform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (flat.sqrMagnitude < 0.001f) flat = Vector3.forward;
        flat.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, flat);
        float pitch = Mathf.Clamp(Vector3.SignedAngle(flat, forward, right), -30f, 30f);   // 양수 = 아래
        Vector3 direction = Quaternion.AngleAxis(pitch, right) * flat;

        return viewerTransform.position + direction * distance;
    }

    // 크레딧 화면을 보는 사람 쪽으로 좌우로만 돌리고, 화면은 늘 똑바로 세운다.
    // 눈을 정면으로 향하게 하면 눈보다 높이 둔 화면이 뒤로 젖혀져 기울어 보인다.
    private void FaceViewer()
    {
        Vector3 away = Vector3.ProjectOnPlane(canvasTransform.position - viewerTransform.position, Vector3.up);
        if (away.sqrMagnitude > 0.0001f)
            canvasTransform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    // 크레딧 위치 = 기준점(anchor) + 높이 조정. 높이 조정은 매 프레임 그대로 더하므로
    // Play 중에 Inspector에서 Height Offset을 바꾸면 바로 위아래로 움직인다.
    // 시선이 기준점에서 recenterAngle 넘게 벗어나면 기준점이 부드럽게 다시 시선 정면으로 따라온다.
    // 작게 둘러볼 때는 가만히 있어 어지럽지 않다.
    private void LateUpdate()
    {
        if (!IsPlaying || viewerTransform == null || canvasTransform == null) return;

        if (followGaze)
        {
            Vector3 toAnchor = anchor - viewerTransform.position;
            if (!recentering && Vector3.Angle(viewerTransform.forward, toAnchor) > recenterAngle)
                recentering = true;

            if (recentering)
            {
                Vector3 target = GazePoint();
                float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
                anchor = Vector3.Lerp(anchor, target, k);

                if ((anchor - target).sqrMagnitude < 0.0004f)   // 2cm 안으로 들어오면 멈춘다
                    recentering = false;
            }
        }

        canvasTransform.position = anchor + Vector3.up * heightOffset;
        FaceViewer();
    }

    private IEnumerator Run()
    {
        if (music != null)
        {
            musicSource.clip = music;
            musicSource.volume = 0f;
            musicSource.Play();
        }

        // 1. 페이드인
        yield return Fade(fadeIn, k =>
        {
            rootGroup.alpha = k;
            musicSource.volume = musicVolume * k;
        });

        // 2. 장면과 크레딧. 둘 중 긴 쪽에 맞춘다.
        float lineSlot = lineFade * 2f + lineHold + lineGap;
        float linesLength = firstLineDelay + lines.Length * lineSlot;
        // 마지막 손 흔들기 컷은 사진보다 길게 보여 준다
        bool endsWithLive = slots.Count > 0 && slots[slots.Count - 1].live;
        float shotsLength = slots.Count * shotDuration + (endsWithLive ? Mathf.Max(0f, liveShotDuration - shotDuration) : 0f);
        float body = Mathf.Max(linesLength, shotsLength);

        for (float t = 0f; t < body; t += Time.deltaTime)
        {
            UpdateShots(t, body);
            UpdateLine(t - firstLineDelay, lineSlot);
            yield return null;
        }

        // 3. 화면을 비우고 The End
        yield return Fade(contentFadeOut, k => contentGroup.alpha = 1f - k);
        StopVideos();
        yield return Fade(endFadeIn, k => endGroup.alpha = k);
        yield return new WaitForSeconds(endHold);
        yield return Fade(finalFadeOut, k =>
        {
            rootGroup.alpha = 1f - k;
            musicSource.volume = musicVolume * (1f - k);
        });

        musicSource.Stop();
        IsPlaying = false;
        onCreditsFinished?.Invoke();
    }

    // 장면 하나 = 영상(있으면) 또는 이미지. 둘 다 없는 칸은 건너뛴다.
    // 마지막에 온실 앞에서 손 흔드는 두 사람(실시간 컷)을 붙인다.
    private void CollectSlots()
    {
        slots.Clear();
        int count = Mathf.Max(clips.Length, shots.Length);
        for (int i = 0; i < count; i++)
        {
            VideoClip clip = i < clips.Length ? clips[i] : null;
            Texture2D image = i < shots.Length ? shots[i] : null;
            if (clip != null || image != null)
                slots.Add((clip, image, false));
        }

        if (liveShotSource != null && liveShotSource.HasWaveShot)
            slots.Add((null, null, true));
    }

    // 지금 장면을 보여 주고, 끝나기 직전 다음 장면을 겹쳐 띄운다. 두 층(layer)을 번갈아 쓴다.
    // 영상은 겹치기 1초 전부터 미리 준비해 첫 프레임이 늦게 뜨지 않게 한다.
    private void UpdateShots(float t, float body)
    {
        int count = slots.Count;
        if (count == 0) return;

        // 크레딧이 더 길면 마지막 장면이 그만큼 더 머문다
        int index = Mathf.Min((int)(t / shotDuration), count - 1);
        float local = t - index * shotDuration;
        float length = index == count - 1 ? Mathf.Max(shotDuration, body - index * shotDuration) : shotDuration;

        int current = index % 2;
        int next = 1 - current;

        ShowSlot(current, index, true, 1f, local / length);

        // 손 흔들기 컷이 보이는 만큼(0~1) 글씨 뒤 검은 번짐을 걷는다 — 그 장면엔 크레딧 글씨가 없다
        float liveVisible = slots[index].live ? 1f : 0f;

        bool hasNext = index < count - 1;
        float crossStart = shotDuration - shotCrossfade;
        if (hasNext && local > crossStart - VideoPrepareLead)
        {
            bool crossing = local > crossStart;
            float k = crossing ? Mathf.SmoothStep(0f, 1f, (local - crossStart) / Mathf.Max(0.01f, shotCrossfade)) : 0f;

            // 새로 겹쳐 오는 층이 위에 그려지도록
            if (crossing && layerImages[next].transform.GetSiblingIndex() < layerImages[current].transform.GetSiblingIndex())
                layerImages[next].transform.SetAsLastSibling();

            ShowSlot(next, index + 1, crossing, k, 0f);

            if (slots[index + 1].live)
                liveVisible = k;
        }
        else
        {
            layerImages[next].color = Color.clear;
            if (layerSlot[next] != index)
                ClearLayer(next);
        }

        SetShade(1f - liveVisible);
    }

    private void SetShade(float visible)
    {
        leftShade.color = new Color(1f, 1f, 1f, leftShadeOpacity * visible);
        textShade.color = new Color(1f, 1f, 1f, textShadeOpacity * visible);
    }

    private void ShowSlot(int layer, int slot, bool play, float alpha, float progress)
    {
        (VideoClip clip, Texture2D image, bool live) = slots[slot];
        VideoPlayer player = layerPlayers[layer];

        if (layerSlot[layer] != slot)
        {
            layerSlot[layer] = slot;
            if (live)
            {
                // 겹치기 1초 전부터 미리 시작해, 화면에 떠오를 때 이미 손을 흔들고 있게 한다
                player.Stop();
                layerImages[layer].texture = liveShotSource.BeginWaveShot();
                liveLayer = layer;
            }
            else if (clip != null)
            {
                ClearTexture(layerTextures[layer]);
                player.clip = clip;
                player.Prepare();
                layerImages[layer].texture = layerTextures[layer];
            }
            else
            {
                player.Stop();
                layerImages[layer].texture = image;
            }
        }

        if (play && clip != null && !player.isPlaying)
            player.Play();

        layerImages[layer].color = new Color(1f, 1f, 1f, alpha);

        // 영상은 자체 카메라 움직임이 있으므로 아주 조금만 다가간다
        float zoom = clip != null ? Mathf.Lerp(1f, shotZoom, 0.35f) : shotZoom;
        float scale = Mathf.Lerp(1f, zoom, Mathf.Clamp01(progress));
        layerImages[layer].rectTransform.localScale = new Vector3(scale, scale, 1f);
    }

    private void ClearLayer(int layer)
    {
        if (layerSlot[layer] == -1) return;

        layerSlot[layer] = -1;
        layerPlayers[layer].Stop();
        layerImages[layer].texture = null;

        if (liveLayer == layer)
        {
            liveLayer = -1;
            liveShotSource.EndWaveShot();
        }
    }

    private void StopVideos()
    {
        for (int i = 0; i < layerPlayers.Length; i++)
            ClearLayer(i);
    }

    private static void ClearTexture(RenderTexture texture)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = previous;
    }

    // 역할 / 이름이 하나씩 떠올랐다가 사라진다.
    private void UpdateLine(float t, float slot)
    {
        if (t < 0f || lines.Length == 0)
        {
            lineGroup.alpha = 0f;
            return;
        }

        int index = (int)(t / slot);
        if (index >= lines.Length)
        {
            lineGroup.alpha = 0f;
            return;
        }

        CreditLine line = lines[index];
        if (roleText.text != line.role) roleText.text = line.role;
        if (nameText.text != line.name) nameText.text = line.name;

        float local = t - index * slot;
        float alpha = local < lineFade ? local / lineFade
            : local < lineFade + lineHold ? 1f
            : local < lineFade * 2f + lineHold ? 1f - (local - lineFade - lineHold) / lineFade
            : 0f;
        lineGroup.alpha = Mathf.SmoothStep(0f, 1f, alpha);
    }

    private static IEnumerator Fade(float duration, Action<float> apply)
    {
        if (duration <= 0f)
        {
            apply(1f);
            yield break;
        }

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            apply(Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        apply(1f);
    }

    // 카메라가 크레딧만 그리고 나머지는 검게 지운다.
    // 후처리(Bloom·색 보정 볼륨)와 FlatKit 안개는 빈 검은 화면 위에도 색을 덮어
    // 보라색 안개처럼 번지므로 크레딧 동안 끈다. 안개 피처는 Play 종료 시 원래대로 돌아온다.
    private static void PrepareCamera(Camera viewer)
    {
        viewer.clearFlags = CameraClearFlags.SolidColor;
        viewer.backgroundColor = Color.black;
        viewer.cullingMask = 1 << CreditsLayer;

        if (viewer.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            cameraData.renderPostProcessing = false;

        FogRendererFeatureSceneController.SetFogSuppressed(true);
        RenderSettings.fog = false;
    }

    private void OnDestroy()
    {
        if (layerTextures == null) return;

        foreach (RenderTexture texture in layerTextures)
            if (texture != null)
                texture.Release();
    }

    // ── 테스트용 ────────────────────────────────────────────────────────
    // 컴포넌트 헤더 우클릭 → 메뉴. 엔딩을 처음부터 보지 않고 크레딧만 확인한다.
    // 헤드셋 없이 Play했으면 Main Camera가 없으므로 임시 카메라를 만들어 Game 뷰에 보여 준다.

    [ContextMenu("테스트: 크레딧만 바로 재생 (Play 모드)")]
    private void DebugPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EndingCredits] 크레딧은 Play 모드에서만 재생됩니다.", this);
            return;
        }

        Camera viewer = Camera.main;
        if (viewer == null)
        {
            var cameraObject = new GameObject("CreditsPreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            viewer = cameraObject.GetComponent<Camera>();
            viewer.depth = 100f;
            Debug.LogWarning("[EndingCredits] Main Camera가 없어 임시 카메라로 Game 뷰에 보여 줍니다.", this);
        }

        // AudioListener가 없으면 노래가 들리지 않는다(XR 카메라가 없는 미리보기)
        if (FindFirstObjectByType<AudioListener>() == null)
            viewer.gameObject.AddComponent<AudioListener>();

        Play(viewer);
    }

    // ── 화면 만들기 ─────────────────────────────────────────────────────

    // 보는 사람이 지금 보고 있는 시선 정면에 화면을 세운다(시선에 수직으로).
    // 이후 시선이 크게 벗어나면 LateUpdate가 천천히 다시 정면으로 데려온다.
    private void BuildCanvas(Transform viewer)
    {
        var canvasObject = new GameObject("CreditsCanvas", typeof(Canvas), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        // 월드 공간으로 먼저 바꾼 뒤 위치를 잡는다. 새 Canvas는 화면 오버레이 모드라
        // 그 상태에서 정한 위치·크기는 화면 해상도 기준 값으로 덮어써진다.
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        canvasRect.localScale = Vector3.one * (width / CanvasWidth);
        canvasTransform = canvasRect;
        anchor = GazePoint();
        canvasRect.position = anchor + Vector3.up * heightOffset;
        FaceViewer();
        recentering = false;

        rootGroup = canvasObject.GetComponent<CanvasGroup>();
        rootGroup.alpha = 0f;

        contentGroup = CreateGroup(canvasRect, "Content");

        // 장면은 크레딧 화면 전체에 깐다. 글씨 쪽(왼쪽)은 아래의 검은 번짐이 덮는다.
        RectTransform shotFrame = CreateRect(contentGroup.transform, "Shots");
        Stretch(shotFrame);
        shotFrame.anchoredPosition = new Vector2(shotOffsetX, 0f);   // 글씨 쪽을 비우도록 오른쪽으로 조금 민다
        shotFrame.gameObject.AddComponent<RectMask2D>();

        // 두 층은 따로 묶어 둔다 — 층 순서를 바꿔도 왼쪽 그라데이션은 늘 맨 위에 남는다
        RectTransform layers = CreateRect(shotFrame, "Layers");
        Stretch(layers);

        layerImages = new RawImage[2];
        layerPlayers = new VideoPlayer[2];
        layerTextures = new RenderTexture[2];
        for (int i = 0; i < layerImages.Length; i++)
        {
            RectTransform shotRect = CreateRect(layers, "Shot" + i);
            Stretch(shotRect);
            layerImages[i] = shotRect.gameObject.AddComponent<RawImage>();
            layerImages[i].color = Color.clear;
            layerImages[i].raycastTarget = false;

            layerTextures[i] = new RenderTexture(VideoWidth, VideoHeight, 0, RenderTextureFormat.ARGB32);
            layerTextures[i].Create();

            VideoPlayer player = shotRect.gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;   // 영상이 장면 시간보다 짧아도 끊기지 않게
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = layerTextures[i];
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            layerPlayers[i] = player;
        }

        // 글씨 뒤 검은 번짐: 왼쪽에서 오른쪽으로 옅어지는 어둠 + 글씨 자리를 감싸는 부드러운 그림자.
        // 사진이 밝아도 크레딧이 또렷하게 읽힌다.
        RectTransform edge = CreateRect(shotFrame, "LeftShade");
        edge.anchorMin = new Vector2(0f, 0f);
        edge.anchorMax = new Vector2(0f, 1f);
        edge.pivot = new Vector2(0f, 0.5f);
        edge.sizeDelta = new Vector2(1300f, 0f);
        RawImage edgeImage = leftShade = edge.gameObject.AddComponent<RawImage>();
        edgeImage.texture = CreateEdgeGradient();
        edgeImage.color = new Color(1f, 1f, 1f, leftShadeOpacity);
        edgeImage.raycastTarget = false;

        RectTransform glow = CreateRect(shotFrame, "TextShade");
        glow.anchorMin = glow.anchorMax = new Vector2(0f, 0.5f);
        glow.pivot = new Vector2(0.5f, 0.5f);
        glow.sizeDelta = new Vector2(1150f, 620f);
        glow.anchoredPosition = new Vector2(textLeft + 310f, -40f);   // 크레딧 글씨 가운데
        RawImage glowImage = textShade = glow.gameObject.AddComponent<RawImage>();
        glowImage.texture = CreateSoftShadow();
        glowImage.color = new Color(1f, 1f, 1f, textShadeOpacity);
        glowImage.raycastTarget = false;

        // 왼쪽 크레딧: 역할(작게, 회색) 위에 이름(크게, 흰색)
        lineGroup = CreateGroup(contentGroup.transform, "Line");
        var lineRect = (RectTransform)lineGroup.transform;
        lineRect.anchorMin = lineRect.anchorMax = new Vector2(0f, 0.5f);
        lineRect.pivot = new Vector2(0f, 0.5f);
        lineRect.sizeDelta = new Vector2(620f, 200f);
        lineRect.anchoredPosition = new Vector2(textLeft, -40f);
        lineGroup.alpha = 0f;

        roleText = CreateText(lineRect, "Role", 32f, new Color(0.72f, 0.72f, 0.72f), TextAlignmentOptions.BottomLeft);
        var roleRect = roleText.rectTransform;
        roleRect.anchorMin = new Vector2(0f, 0.55f);
        roleRect.anchorMax = new Vector2(1f, 1f);
        roleRect.offsetMin = roleRect.offsetMax = Vector2.zero;

        nameText = CreateText(lineRect, "Name", 56f, Color.white, TextAlignmentOptions.TopLeft);
        var nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0.5f);
        nameRect.offsetMin = nameRect.offsetMax = Vector2.zero;

        // The End
        endGroup = CreateGroup(canvasRect, "End");
        endGroup.alpha = 0f;
        TMP_Text end = CreateText(endGroup.transform, "EndText", 72f, Color.white, TextAlignmentOptions.Center);
        Stretch(end.rectTransform);
        end.text = endText;

        musicSource = gameObject.GetComponent<AudioSource>();
        if (musicSource == null)
            musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = false;
        musicSource.spatialBlend = 0f;   // 2D. VR에서 머리 방향과 상관없이 같은 크기로

        SetLayerRecursively(canvasObject, CreditsLayer);
    }

    private TMP_Text CreateText(Transform parent, string name, float size, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(parent, name);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static CanvasGroup CreateGroup(Transform parent, string name)
    {
        RectTransform rect = CreateRect(parent, name);
        Stretch(rect);
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 가운데가 검고 가장자리로 갈수록 부드럽게 투명해지는 타원 그림자(흐릿한 검은 번짐)
    private static Texture2D CreateSoftShadow()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = 1f - Mathf.SmoothStep(0.25f, 1f, d);
            texture.SetPixel(x, y, new Color(0f, 0f, 0f, a));
        }
        texture.Apply();
        return texture;
    }

    // 왼쪽 끝 SolidBlack 비율까지는 완전한 검정, 그 뒤로 오른쪽 끝까지 부드럽게 투명해지는 가로 그라데이션
    private static Texture2D CreateEdgeGradient()
    {
        const int size = 256;
        const float solidBlack = 0.4f;
        var texture = new Texture2D(size, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < size; x++)
        {
            float u = x / (size - 1f);
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(solidBlack, 1f, u));
            texture.SetPixel(x, 0, new Color(0f, 0f, 0f, a));
        }
        texture.Apply();
        return texture;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
