using UnityEngine;
using UnityEngine.Rendering;

namespace ReBloom.Solar
{
    /// <summary>
    /// 패널 복구 컷씬의 청록색 에너지 발광 연출.
    ///
    /// 패널 외곽 테두리에 얇은 발광 프레임 메시를 배치하고, Stage 3 Volume의
    /// Bloom(threshold=0.9, intensity=2.7)으로 자연스러운 빛 번짐 효과를 낸다.
    ///
    /// 프레임은 패널 렌더러들의 localBounds를 합산해 자동 계산하고,
    /// <see cref="SolarReflector.Normal"/>으로 표면 방향을 잡는다.
    /// URP Unlit + Additive 블렌드를 써서 패널 본체 텍스처에는 영향을 주지 않는다.
    /// 컷씬 종료·중단 시 프레임 오브젝트를 파괴한다.
    /// </summary>
    public class SolarPanelRepairGlow
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        const float FrameInset = 0.003f;
        const float FrameOutset = 0.03f;
        const float SurfaceOffset = 0.002f;
        const float MaxPanelThickness = 1.0f;

        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        GameObject frameGO;
        Renderer frameRenderer;
        Material frameMaterial;
        Mesh frameMesh;
        Color targetColor;
        bool active;

        public bool IsActive => active;

        public void Setup(SolarPanelCleanView cleanView, Color emissionHDR)
        {
            Cleanup();
            if (cleanView == null) return;
            targetColor = emissionHDR;

            Material broken = cleanView.brokenMaterial;
            Material clean = cleanView.cleanMaterial;
            Transform root = cleanView.transform;

            SolarReflector reflector = cleanView.GetComponentInChildren<SolarReflector>();
            Transform frameParent = reflector != null ? reflector.transform : root;
            int normalAxis = 1;
            bool positiveNormal = true;

            Bounds combinedLocal = default;
            bool first = true;

            foreach (Renderer r in cleanView.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;

                bool match = false;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == broken || mats[i] == clean)
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) continue;

                Bounds lb = r.localBounds;
                Vector3 ext = lb.extents;

                if (reflector != null)
                {
                    Vector3 localDir = r.transform.InverseTransformDirection(
                        reflector.transform.up).normalized;
                    float halfThick = Mathf.Abs(ext.x * localDir.x)
                                    + Mathf.Abs(ext.y * localDir.y)
                                    + Mathf.Abs(ext.z * localDir.z);
                    if (halfThick * 2f > MaxPanelThickness) continue;
                }

                Bounds parentBounds = default;
                bool pbFirst = true;

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 lc = lb.center + new Vector3(ext.x * x, ext.y * y, ext.z * z);
                    Vector3 wc = r.transform.TransformPoint(lc);
                    Vector3 pc = frameParent.InverseTransformPoint(wc);

                    if (pbFirst) { parentBounds = new Bounds(pc, Vector3.zero); pbFirst = false; }
                    else parentBounds.Encapsulate(pc);
                }

                if (first) { combinedLocal = parentBounds; first = false; }
                else
                {
                    combinedLocal.Encapsulate(parentBounds.min);
                    combinedLocal.Encapsulate(parentBounds.max);
                }
            }

            if (first) return;

            if (reflector == null)
            {
                Vector3 size = combinedLocal.size;
                if (size.x <= size.y && size.x <= size.z) normalAxis = 0;
                else if (size.y <= size.x && size.y <= size.z) normalAxis = 1;
                else normalAxis = 2;
            }

            frameMaterial = CreateFrameMaterial();
            if (frameMaterial == null) return;

            frameMesh = BuildFrameMesh(combinedLocal.min, combinedLocal.max,
                                       normalAxis, positiveNormal);

            frameGO = new GameObject("_RepairGlowFrame");
            frameGO.transform.SetParent(frameParent, false);

            MeshFilter mf = frameGO.AddComponent<MeshFilter>();
            mf.sharedMesh = frameMesh;

            MeshRenderer mr = frameGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = frameMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            frameRenderer = mr;
            SetIntensity(0f);
            active = true;
        }

        public void SetIntensity(float t)
        {
            if (frameRenderer == null) return;
            t = Mathf.Clamp01(t);
            Color color = targetColor * t;
            color.a = t;
            block.Clear();
            block.SetColor(BaseColorId, color);
            frameRenderer.SetPropertyBlock(block);
        }

        public void Cleanup()
        {
            if (!active) return;

            if (frameGO != null) Object.Destroy(frameGO);
            if (frameMaterial != null) Object.Destroy(frameMaterial);
            if (frameMesh != null) Object.Destroy(frameMesh);

            frameGO = null;
            frameRenderer = null;
            frameMaterial = null;
            frameMesh = null;
            active = false;
        }

        static Mesh BuildFrameMesh(Vector3 boundsMin, Vector3 boundsMax,
                                    int normalAxis, bool positiveNormal)
        {
            int axis0, axis1;
            switch (normalAxis)
            {
                case 0: axis0 = 1; axis1 = 2; break;
                case 1: axis0 = 0; axis1 = 2; break;
                default: axis0 = 0; axis1 = 1; break;
            }

            float surfaceLevel = positiveNormal
                ? boundsMax[normalAxis] + SurfaceOffset
                : boundsMin[normalAxis] - SurfaceOffset;

            float min0 = boundsMin[axis0];
            float max0 = boundsMax[axis0];
            float min1 = boundsMin[axis1];
            float max1 = boundsMax[axis1];

            float oMin0 = min0 - FrameOutset;
            float oMax0 = max0 + FrameOutset;
            float oMin1 = min1 - FrameOutset;
            float oMax1 = max1 + FrameOutset;

            float iMin0 = min0 + FrameInset;
            float iMax0 = max0 - FrameInset;
            float iMin1 = min1 + FrameInset;
            float iMax1 = max1 - FrameInset;

            if (iMin0 > iMax0) iMin0 = iMax0 = (min0 + max0) * 0.5f;
            if (iMin1 > iMax1) iMin1 = iMax1 = (min1 + max1) * 0.5f;

            Vector3[] verts = new Vector3[8];

            void SetVert(int idx, float a0, float a1)
            {
                verts[idx] = Vector3.zero;
                verts[idx][normalAxis] = surfaceLevel;
                verts[idx][axis0] = a0;
                verts[idx][axis1] = a1;
            }

            SetVert(0, oMin0, oMin1);
            SetVert(1, oMax0, oMin1);
            SetVert(2, oMax0, oMax1);
            SetVert(3, oMin0, oMax1);
            SetVert(4, iMin0, iMin1);
            SetVert(5, iMax0, iMin1);
            SetVert(6, iMax0, iMax1);
            SetVert(7, iMin0, iMax1);

            int[] tris = positiveNormal
                ? new[]
                {
                    0, 4, 1,  1, 4, 5,
                    1, 5, 2,  2, 5, 6,
                    2, 6, 3,  3, 6, 7,
                    3, 7, 0,  0, 7, 4,
                }
                : new[]
                {
                    0, 1, 4,  1, 5, 4,
                    1, 2, 5,  2, 6, 5,
                    2, 3, 6,  3, 7, 6,
                    3, 0, 7,  0, 4, 7,
                };

            Vector3 normal = Vector3.zero;
            normal[normalAxis] = positiveNormal ? 1f : -1f;
            Vector3[] normals = new Vector3[8];
            for (int i = 0; i < 8; i++) normals[i] = normal;

            Mesh mesh = new Mesh();
            mesh.name = "RepairGlowFrame";
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material CreateFrameMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[SolarPanelRepairGlow] URP Unlit 셰이더를 찾지 못했습니다.");
                return null;
            }

            Material mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor(BaseColorId, Color.clear);
            return mat;
        }
    }
}
