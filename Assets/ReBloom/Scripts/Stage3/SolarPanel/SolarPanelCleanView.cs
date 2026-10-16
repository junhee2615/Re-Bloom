using System.Collections.Generic;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 고장/수리 상태를 판의 겉모습에 반영한다. 이끼·먼지·균열이 구워진 머티리얼을 깨끗한 쪽으로 바꾸고,
    /// 판 위에 <b>얹어 놓은 것</b>(이끼 메시, <c>Spark</c>·<c>Dust</c> 파티클)을 끈다.
    ///
    /// <b>"얹어 놓은 것"은 이름이 아니라 구조로 가린다.</b> 판의 자기 부품은
    /// <see cref="brokenMaterial"/>/<see cref="cleanMaterial"/>이 꽂힌 렌더러와 그 조상뿐이고,
    /// 루트 아래 그 밖의 오브젝트는 전부 나중에 얹은 것이다. 이름으로 짚으려다 두 번 샜다 —
    /// 대소문자가 섞여 있고(<c>Wild_ivy</c> / <c>Wild_Ivy</c>) 이름 체계도 하나가 아니다(<c>IvyWallCD_2</c>).
    ///
    /// <b><see cref="SolarReflector.broken"/> 값을 매 프레임 따라간다.</b> <see cref="SolarPanelRepair.onRepaired"/>
    /// 같은 이벤트에 걸지 않은 이유가 둘이다.
    ///  - 이벤트는 <see cref="SolarPanelRepair.Repair"/>를 거칠 때만 나간다. 인스펙터에서 <c>broken</c>을
    ///    직접 끄거나 <see cref="SolarPanelRepair.ResetToBroken"/>으로 되돌리면 겉모습이 따라오지 않는다.
    ///  - 구독 수명이 씬 전환·프리팹 재생성과 얽히면 해제를 빠뜨리기 쉽다.
    /// 값 비교는 <c>ResonanceController.SyncRoleGate()</c>가 같은 이유로 쓰는 방식이다.
    ///
    /// <b>끄는 대상은 Awake 시점의 켜짐 여부를 기억한다.</b> 고장 상태로 되돌릴 때 무조건 켜면
    /// 작업자가 일부러 꺼 둔 이끼까지 되살아난다 — "껐는데 플레이하면 다시 보인다"가 된다.
    /// 머티리얼은 반대로 <c>broken</c>을 그대로 따른다(고장 판이 깨끗해 보이면 안 되므로).
    ///
    /// 연출은 로컬 표시일 뿐이라 동기화하지 않는다. 단 <c>broken</c> 자체가 아직 피어별 로컬 판정이므로,
    /// 수리 시점이 갈리면 한쪽만 깨끗해 보인다 — 그건 이 컴포넌트가 아니라 수리 판정 쪽에서 풀어야 한다.
    ///
    /// 배치: <b>패널 프리팹의 루트</b>. 판 본체 렌더러 3개(<c>SolarPanelWhite_low</c>·<c>SolarPanelBlue_low</c>
    /// ·<c>SoloarPanelMetal_low</c>)와 파티클(<c>Spark</c>·<c>Dust</c>)이 서로 다른 가지에 흩어져 있어서,
    /// 판 면(<see cref="SolarReflector"/>가 붙은 곳)에서 찾으면 형제·상위를 놓친다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Panel Clean View")]
    [DefaultExecutionOrder(300)]   // SolarPanelRepair(200)가 Repair()를 부른 뒤에 읽는다.
    public class SolarPanelCleanView : MonoBehaviour
    {
        [Header("머티리얼")]
        [Tooltip("고장 상태에서 쓰는 머티리얼. 이 머티리얼이 꽂힌 슬롯만 교체 대상으로 잡는다.")]
        public Material brokenMaterial;

        [Tooltip("수리된 뒤 쓰는 머티리얼.")]
        public Material cleanMaterial;

        [Header("고장 연출")]
        [Tooltip("수리되면 판 위에 얹어 놓은 오브젝트를 전부 끈다 — 이끼 메시, Spark·Dust 파티클. " +
                 "'얹어 놓은 것' = 판 본체 머티리얼이 꽂힌 렌더러도, 그 조상도 아닌 자식.")]
        public bool hideAddonsWhenClean = true;

        [Tooltip("수리돼도 남겨야 하는 자식의 이름(대소문자 무시). 판에 기능용 오브젝트를 달았을 때의 " +
                 "비상구다. 보통은 비워 둔다.")]
        public string[] alwaysKeepNames;

        SolarReflector reflector;

        // 교체 대상 슬롯. (렌더러, 슬롯 번호)로 들고 있어야 한 렌더러에 여러 슬롯이 있어도 정확히 짚는다.
        readonly List<(Renderer renderer, int slot)> slots = new List<(Renderer, int)>();

        // 수리되면 끌 오브젝트들. 파티클과 이름으로 짚은 고장 표현(이끼)을 함께 담는다.
        //
        // activeSelf를 함께 기억하는 이유: 고장 상태로 되돌릴 때 무조건 켜면 안 된다.
        // 작업자가 일부러 꺼 둔 이끼까지 되살아나 "껐는데 다시 보인다"가 된다(실제로 그랬다).
        readonly List<(GameObject target, bool authoredActive)> decay =
            new List<(GameObject, bool)>();

        // 마지막으로 적용한 상태. null이면 아직 한 번도 적용하지 않았다(첫 프레임에 무조건 맞춘다).
        bool? applied;

        void Awake()
        {
            reflector = GetComponentInChildren<SolarReflector>(true);
            if (reflector == null)
                Debug.LogWarning($"[SolarPanelCleanView] {name}: 자식에 SolarReflector가 없어 " +
                                 $"고장 여부를 알 수 없습니다. 패널 프리팹 루트에 붙였는지 확인하세요.", this);

            Collect();
        }

        // 두 머티리얼 중 하나가 꽂힌 슬롯을 찾아 둔다. 판 본체는 FBX 안의 렌더러 여러 개로 쪼개져 있어
        // 참조를 손으로 배선하면 모델이 바뀔 때마다 다시 해야 한다 — 머티리얼로 짚으면 저절로 따라온다.
        void Collect()
        {
            slots.Clear();
            decay.Clear();

            if (brokenMaterial == null || cleanMaterial == null)
            {
                Debug.LogWarning($"[SolarPanelCleanView] {name}: 머티리얼이 비어 있어 겉모습을 바꾸지 않습니다.", this);
            }
            else
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == brokenMaterial || mats[i] == cleanMaterial)
                            slots.Add((r, i));
                    }
                }
            }

            if (!hideAddonsWhenClean) return;

            // 본체를 하나도 못 찾았으면 '얹은 것'을 가릴 기준이 없다. 그대로 두면 판 전체를 꺼 버린다.
            if (slots.Count == 0)
            {
                Debug.LogWarning($"[SolarPanelCleanView] {name}: 판 본체 렌더러를 찾지 못해 " +
                                 $"얹은 오브젝트를 구분할 수 없습니다. 머티리얼 배선을 확인하세요.", this);
                return;
            }

            CollectAddons();
        }

        // 판의 자기 부품 = 본체 렌더러 + 그 조상. 루트 아래 그 밖의 것은 전부 나중에 얹은 것이다.
        void CollectAddons()
        {
            HashSet<Transform> own = new HashSet<Transform>();
            foreach ((Renderer renderer, int _) in slots)
            {
                for (Transform t = renderer.transform; t != null && t != transform.parent; t = t.parent)
                    own.Add(t);
            }

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform || own.Contains(t)) continue;

                // 기능용 오브젝트를 달았다면 여기서 빠진다.
                if (IsAlwaysKept(t.name)) continue;

                // 이끼는 LODGroup 아래로 메시가 여러 벌이다. 가장 위 하나만 끄면 전부 사라지므로
                // 이미 담긴 것의 자식이면 건너뛴다.
                if (!AlreadyCovered(t)) decay.Add((t.gameObject, t.gameObject.activeSelf));
            }
        }

        bool IsAlwaysKept(string objectName)
        {
            if (alwaysKeepNames == null) return false;

            for (int i = 0; i < alwaysKeepNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(alwaysKeepNames[i]) &&
                    string.Equals(objectName, alwaysKeepNames[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        bool AlreadyCovered(Transform t)
        {
            for (int i = 0; i < decay.Count; i++)
            {
                GameObject already = decay[i].target;
                if (already != null && t != already.transform && t.IsChildOf(already.transform))
                    return true;
            }

            return false;
        }

        void LateUpdate()
        {
            bool broken = reflector != null && reflector.broken;
            if (applied == broken) return;

            applied = broken;
            Apply(broken);
        }

        void Apply(bool broken)
        {
            Material bodyMaterial = broken ? brokenMaterial : cleanMaterial;

            // sharedMaterials의 getter는 복사본을 주므로, 고쳐서 다시 대입해야 반영된다.
            // sharedMaterial 쪽으로 대입하는 것이라 머티리얼 에셋이 인스턴스로 복제되지 않는다.
            foreach ((Renderer renderer, int slot) in slots)
            {
                if (renderer == null) continue;

                Material[] mats = renderer.sharedMaterials;
                if (slot >= mats.Length || mats[slot] == bodyMaterial) continue;

                mats[slot] = bodyMaterial;
                renderer.sharedMaterials = mats;
            }

            // 깨끗해지면 끄고, 고장으로 돌아가면 원래 켜져 있던 것만 되살린다.
            // 무조건 켜면 작업자가 꺼 둔 것까지 되살아난다.
            foreach ((GameObject target, bool authoredActive) in decay)
            {
                if (target == null) continue;

                bool show = broken && authoredActive;
                if (target.activeSelf != show) target.SetActive(show);
            }
        }
    }
}
