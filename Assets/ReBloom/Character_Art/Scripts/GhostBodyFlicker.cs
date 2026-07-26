using UnityEngine;

public class GhostBodyFlicker : MonoBehaviour
{
    [Header("캐릭터 본체 렌더러")]
    [SerializeField]
    private Renderer[] bodyRenderers;

    [Header("깜빡임 설정")]
    [SerializeField, Min(0f)]
    private float flickerSpeed = 3f;

    [SerializeField, Range(0f, 1f)]
    private float minimumBrightness = 0.2f;

    private MaterialPropertyBlock propertyBlock;
    private Color[][] originalColors;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        SaveOriginalColors();
    }

    private void Update()
    {
        float wave = Mathf.Abs(Mathf.Sin(Time.time * flickerSpeed));
        float brightness = Mathf.Lerp(minimumBrightness, 1f, wave);

        ApplyBrightness(brightness);
    }

    private void SaveOriginalColors()
    {
        originalColors = new Color[bodyRenderers.Length][];

        for (int rendererIndex = 0;
             rendererIndex < bodyRenderers.Length;
             rendererIndex++)
        {
            Renderer targetRenderer = bodyRenderers[rendererIndex];

            if (targetRenderer == null)
            {
                continue;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            originalColors[rendererIndex] = new Color[materials.Length];

            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];

                if (material == null)
                {
                    originalColors[rendererIndex][materialIndex] = Color.white;
                    continue;
                }

                if (material.HasProperty(BaseColorId))
                {
                    originalColors[rendererIndex][materialIndex] =
                        material.GetColor(BaseColorId);
                }
                else if (material.HasProperty(ColorId))
                {
                    originalColors[rendererIndex][materialIndex] =
                        material.GetColor(ColorId);
                }
                else
                {
                    originalColors[rendererIndex][materialIndex] = Color.white;
                }
            }
        }
    }

    private void ApplyBrightness(float brightness)
    {
        for (int rendererIndex = 0;
             rendererIndex < bodyRenderers.Length;
             rendererIndex++)
        {
            Renderer targetRenderer = bodyRenderers[rendererIndex];

            if (targetRenderer == null ||
                originalColors[rendererIndex] == null)
            {
                continue;
            }

            int materialCount = targetRenderer.sharedMaterials.Length;

            for (int materialIndex = 0;
                 materialIndex < materialCount;
                 materialIndex++)
            {
                targetRenderer.GetPropertyBlock(
                    propertyBlock,
                    materialIndex
                );

                Color originalColor =
                    originalColors[rendererIndex][materialIndex];

                Color flickerColor = new Color(
                    originalColor.r * brightness,
                    originalColor.g * brightness,
                    originalColor.b * brightness,
                    originalColor.a
                );

                propertyBlock.SetColor(BaseColorId, flickerColor);
                propertyBlock.SetColor(ColorId, flickerColor);

                targetRenderer.SetPropertyBlock(
                    propertyBlock,
                    materialIndex
                );
            }
        }
    }

    private void OnDisable()
    {
        if (bodyRenderers == null)
        {
            return;
        }

        foreach (Renderer targetRenderer in bodyRenderers)
        {
            if (targetRenderer != null)
            {
                targetRenderer.SetPropertyBlock(null);
            }
        }
    }
}