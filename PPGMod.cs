using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace PPGMod
{
    // ================= СОСТОЯНИЕ КЛАВИШ =================
    public static class KeyState
    {
        public static bool K_enable = false;
        public static bool L_enable = false;
    }

    // ================= ПЛАГИН =================
    [BepInPlugin("com.yourname.ppgmod", "PPG Mod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const float MaxRotationSpeed = 25000f;
        public static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("=== Plugin Awake START ===");

            try
            {
                Physics2D.maxRotationSpeed = MaxRotationSpeed;

                int nukeMask = LayerMask.GetMask("Objects", "Bounds");
                if (nukeMask == 0)
                    Log.LogWarning("LayerMask 'Objects'/'Bounds' returned 0");

                var harmony = new Harmony("com.yourname.ppgmod");
                harmony.PatchAll();

                Log.LogInfo("=== Plugin Awake COMPLETE ===");
            }
            catch (Exception ex)
            {
                Log.LogError("=== Plugin Awake FAILED ===");
                Log.LogError(ex.ToString());
            }
        }

        private void Update()
        {
            KeyState.K_enable = Input.GetKey(KeyCode.K);
            KeyState.L_enable = Input.GetKey(KeyCode.L);
        }
    }

    // ================= МАРКЕРЫ ПРОВОДОВ =================
    public class InvisibleWireMarker : MonoBehaviour
    {
        public bool IsInvisible = true;
        public Color OriginalStart = Color.white;
        public Color OriginalEnd = Color.white;
        public bool ColorsSaved = false;
    }

    public class FastWireMarker : MonoBehaviour
    {
        public bool IsFast = true;
    }

    // ================= ПРОВОДА =================
    [HarmonyPatch(typeof(WireBehaviour), "Initialise")]
    public static class WireBehaviourPatch
    {
        [HarmonyPostfix]
        static void Postfix(WireBehaviour __instance)
        {
            if (__instance == null) return;

            LineRenderer lr = __instance.lineRenderer;
            if (lr == null) lr = __instance.GetComponent<LineRenderer>();
            if (lr == null) lr = __instance.GetComponentInChildren<LineRenderer>(true);

            if (lr != null)
            {
                lr.sortingLayerName = "Top";
                lr.sortingOrder = 1;
            }

            var invisibleMarker = __instance.GetComponent<InvisibleWireMarker>();
            var fastMarker = __instance.GetComponent<FastWireMarker>();

            if (KeyState.K_enable && invisibleMarker == null)
            {
                invisibleMarker = __instance.gameObject.AddComponent<InvisibleWireMarker>();
                invisibleMarker.IsInvisible = true;
            }

            if (KeyState.L_enable && fastMarker == null)
            {
                string typeName = __instance.GetType().Name;
                bool skip = typeName.Contains("Fixed") || typeName.Contains("Rigid") || typeName.Contains("Spring");
                if (!skip)
                {
                    fastMarker = __instance.gameObject.AddComponent<FastWireMarker>();
                    fastMarker.IsFast = true;
                }
            }

            bool shouldBeInvisible = invisibleMarker != null && invisibleMarker.IsInvisible;
            if (lr != null) lr.enabled = !shouldBeInvisible;

            if (fastMarker != null && fastMarker.IsFast)
            {
                if (__instance.physicalBehaviour != null)
                    __instance.physicalBehaviour.ActivationPropagationDelay = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(WireBehaviour))]
    public static class WireInvisiblePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnMouseOverlapEvent")]
        static void PostfixOnMouseOverlap(WireBehaviour __instance, bool overlap)
        {
            var marker = __instance.GetComponent<InvisibleWireMarker>();
            if (marker == null || !marker.IsInvisible) return;

            var lr = __instance.lineRenderer;
            if (lr == null) lr = __instance.GetComponentInChildren<LineRenderer>(true);
            if (lr == null) return;

            if (!marker.ColorsSaved)
            {
                marker.OriginalStart = lr.startColor;
                marker.OriginalEnd = lr.endColor;
                marker.ColorsSaved = true;
            }

            if (overlap && UserPreferenceManager.Current.ShowOutlines)
            {
                lr.enabled = true;
                lr.startColor = Color.red;
                lr.endColor = Color.red;
            }
            else
            {
                lr.enabled = false;
                lr.startColor = marker.OriginalStart;
                lr.endColor = marker.OriginalEnd;
            }
        }
    }

    // ================= МАРКЕР НЕВИДИМОГО ПИНА =================
    public class InvisiblePinMarker : MonoBehaviour
    {
        public bool IsInvisible = true;
    }

    [HarmonyPatch(typeof(PinBehaviour), "Start")]
    public static class InvisiblePinPatch
    {
        [HarmonyPostfix]
        static void Postfix(PinBehaviour __instance)
        {
            if (__instance == null) return;

            if (KeyState.K_enable)
            {
                if (__instance.GetComponent<InvisiblePinMarker>() == null)
                    __instance.gameObject.AddComponent<InvisiblePinMarker>();

                if (__instance.SpriteRenderer != null)
                    __instance.SpriteRenderer.enabled = false;
            }
        }
    }

    [HarmonyPatch(typeof(PinBehaviour), "CreateJoint")]
    public static class InvisiblePinCreatePatch
    {
        [HarmonyPostfix]
        static void Postfix(PinBehaviour __instance)
        {
            if (__instance == null) return;
            var marker = __instance.GetComponent<InvisiblePinMarker>();
            if (marker != null && marker.IsInvisible && __instance.SpriteRenderer != null)
                __instance.SpriteRenderer.enabled = false;
        }
    }

    [HarmonyPatch(typeof(PinBehaviour))]
    public static class InvisiblePinHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnMouseOverlapEvent")]
        static void PostfixOnMouseOverlap(PinBehaviour __instance, bool overlap)
        {
            if (__instance.GetComponent<InvisiblePinMarker>() == null) return;
            if (__instance.SpriteRenderer == null) return;

            if (overlap && UserPreferenceManager.Current.ShowOutlines)
            {
                __instance.SpriteRenderer.enabled = true;
                __instance.SpriteRenderer.color = Color.red;
            }
            else
            {
                __instance.SpriteRenderer.enabled = false;
                __instance.SpriteRenderer.color = Color.white;
            }
        }
    }

    // ================= РЕМЕНЬ БЕЗ СКОЛЬЖЕНИЯ =================
    public class BeltNoGearRatioMarker : MonoBehaviour { }

    [HarmonyPatch(typeof(SerpentineBeltBehaviour))]
    public static class SlipFreeBeltPatch
    {
        private class BeltState
        {
            public RelativeJoint2D Joint;
            public float InitialRotation;
            public float GearRatio;
            public bool NoGearRatio;
        }

        private static readonly Dictionary<SerpentineBeltBehaviour, BeltState> States
            = new Dictionary<SerpentineBeltBehaviour, BeltState>();

        private static readonly FieldInfo GearRatioField =
            AccessTools.Field(typeof(SerpentineBeltBehaviour), "gearRatio");

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        static void StartPostfix(SerpentineBeltBehaviour __instance)
        {
            if (__instance.Self == null || __instance.Other == null) return;
            if (__instance.Self == __instance.Other) return;
            if (__instance.Self.rigidbody == null || __instance.Other.rigidbody == null) return;

            var existing = __instance.Self.gameObject.GetComponent<RelativeJoint2D>();
            if (existing != null) UnityEngine.Object.Destroy(existing);

            if (States.TryGetValue(__instance, out var old) && old.Joint != null)
                UnityEngine.Object.Destroy(old.Joint);

            // Проверяем маркер — сохраняется в постройке
            bool noGearRatio = __instance.GetComponent<BeltNoGearRatioMarker>() != null;

            // Если L зажата при создании — ставим маркер
            if (KeyState.L_enable && __instance.GetComponent<BeltNoGearRatioMarker>() == null)
            {
                __instance.gameObject.AddComponent<BeltNoGearRatioMarker>();
                noGearRatio = true;
            }

            float ratio = 1f;
            if (!noGearRatio && GearRatioField != null)
            {
                ratio = (float)GearRatioField.GetValue(__instance);
                if (ratio <= 0f) ratio = 1f;
            }

            float selfRot = __instance.Self.rigidbody.rotation;
            float otherRot = __instance.Other.rigidbody.rotation;
            float d = selfRot * (ratio - 1f);
            float initialRotation = (otherRot - selfRot) - d;

            var joint = __instance.Self.gameObject.AddComponent<RelativeJoint2D>();
            joint.connectedBody = __instance.Other.rigidbody;
            joint.autoConfigureOffset = false;
            joint.maxForce = 0f;
            joint.maxTorque = 100000f;
            joint.correctionScale = 0.5f;
            joint.enableCollision = false;
            joint.angularOffset = initialRotation + d;

            States[__instance] = new BeltState
            {
                Joint = joint,
                InitialRotation = initialRotation,
                GearRatio = ratio,
                NoGearRatio = noGearRatio
            };
        }

        [HarmonyPrefix]
        [HarmonyPatch("FixedUpdate")]
        static bool FixedUpdatePrefix(SerpentineBeltBehaviour __instance)
        {
            if (__instance.Self == null || __instance.Other == null) return false;
            if (__instance.Self == __instance.Other) return false;
            if (__instance.Self.rigidbody == null || __instance.Other.rigidbody == null) return false;

            if (!States.TryGetValue(__instance, out var state) || state.Joint == null)
            {
                StartPostfix(__instance);
                return false;
            }

            float ratio = state.GearRatio;
            if (!state.NoGearRatio && GearRatioField != null)
            {
                ratio = (float)GearRatioField.GetValue(__instance);
                if (ratio <= 0f) ratio = 1f;

                if (Mathf.Abs(ratio - state.GearRatio) > 0.0001f)
                {
                    float selfRot = __instance.Self.rigidbody.rotation;
                    float otherRot = __instance.Other.rigidbody.rotation;
                    float d = selfRot * (ratio - 1f);
                    state.InitialRotation = (otherRot - selfRot) - d;
                    state.GearRatio = ratio;
                }
            }

            float dNew = __instance.Self.rigidbody.rotation * (ratio - 1f);
            state.Joint.angularOffset = state.InitialRotation + dNew;

            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnDestroy")]
        static void OnDestroyPostfix(SerpentineBeltBehaviour __instance)
        {
            if (States.TryGetValue(__instance, out var state))
            {
                if (state.Joint != null)
                    UnityEngine.Object.Destroy(state.Joint);
                States.Remove(__instance);
            }
        }
    }

    // ================= PULSE DRUM FIX =================
    [HarmonyPatch(typeof(PulseDrumBehaviour), "Shoot")]
    public static class PulseDrumFixPatch
    {
        private static readonly RaycastHit2D[] _buffer = new RaycastHit2D[16];

        [HarmonyPrefix]
        static bool Prefix(PulseDrumBehaviour __instance)
        {
            if (__instance == null) return true;
            if (__instance.PhysicalBehaviour == null) return true;
            if (__instance.PhysicalBehaviour.rigidbody == null) return true;

            if (__instance.Source != null && __instance.Clips != null && __instance.Clips.Length > 0)
                __instance.Source.PlayOneShot(__instance.Clips[UnityEngine.Random.Range(0, __instance.Clips.Length)]);

            Vector2 pos = __instance.BarrelPosition;
            Vector2 dir = __instance.BarrelDirection;
            float force = __instance.BaseForce + __instance.PhysicalBehaviour.Charge * __instance.ChargeInfluence;

            __instance.PhysicalBehaviour.rigidbody.AddForceAtPosition(-force * dir, pos);

            float halfSpread = __instance.SpreadAngle / 2f;
            float halfEmitter = __instance.EmitterSize / 2f;
            int rayCount = __instance.RayCount;
            float step = rayCount > 1 ? (float)(rayCount - 1) : 1f;

            int mask = __instance.Layers;
            int boundsLayer = LayerMask.NameToLayer("Bounds");
            if (boundsLayer >= 0)
                mask &= ~(1 << boundsLayer);

            for (int i = 0; i < rayCount; i++)
            {
                float t = i / step;
                float angle = t * __instance.SpreadAngle - halfSpread;
                float offset = t * __instance.EmitterSize - halfEmitter;

                Vector3 rayDir = Quaternion.Euler(0f, 0f, angle) * dir;
                Vector2 origin = pos + Vector2.Perpendicular(dir) * offset;

                int hitCount = Physics2D.RaycastNonAlloc(origin, rayDir, _buffer, __instance.Range, mask);

                float bestDist = float.MaxValue;
                int bestIndex = -1;
                for (int j = 0; j < hitCount; j++)
                {
                    var h = _buffer[j];
                    if (h.transform == null) continue;
                    if (h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        bestIndex = j;
                    }
                }

                if (bestIndex >= 0)
                {
                    var hit = _buffer[bestIndex];
                    PhysicalBehaviour pb;
                    if (Global.main.PhysicalObjectsInWorldByTransform.TryGetValue(hit.transform, out pb))
                    {
                        if (pb.rigidbody != null)
                        {
                            Vector3 v = force * pb.rigidbody.mass * rayDir;
                            pb.rigidbody.AddForceAtPosition(v, hit.point);
                        }
                    }
                }
            }

            return false;
        }
    }

    // ================= ПЕРЕКРАСКА =================
    public class TintOverlayMarker : MonoBehaviour { }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class TintOnSpawnPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<EditableTintBehaviour>() != null) return;
            if (__instance.GetComponent<NewEditableTintBehaviour>() != null) return;

            __instance.gameObject.AddComponent<NewEditableTintBehaviour>();
        }
    }

    public class NewEditableTintBehaviour : MonoBehaviour
    {
        public Color Tint = Color.white;
        public PhysicalBehaviour phys;
        public bool firstSpawned = true;

        private class Ovl
        {
            public SpriteRenderer overlay;
            public SpriteRenderer baseSR;
            public Sprite lastSprite;
        }

        private readonly List<Ovl> overlays = new List<Ovl>();

        private static readonly FieldInfo CachedColourField =
            typeof(BloodContainer).GetField("cachedComputedColour",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private void Awake()
        {
            phys = GetComponent<PhysicalBehaviour>();
        }

        private void Update()
        {
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                if (o.overlay == null || o.baseSR == null)
                {
                    overlays.RemoveAt(i);
                    i--;
                    continue;
                }
                if (o.baseSR.sprite != o.lastSprite)
                {
                    o.lastSprite = o.baseSR.sprite;
                    o.overlay.sprite = o.lastSprite;
                }
            }
        }

        private void Start()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (firstSpawned)
            {
                if (sr != null) Tint = sr.color;
                firstSpawned = false;
            }
            else if (Tint != Color.white)
            {
                SetTint(Tint);
            }

            if (phys == null || phys.ContextMenuOptions == null) return;
            var buttons = phys.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "setEditableTint") return;

            int selCount = SelectionController.Main != null && SelectionController.Main.SelectedObjects != null
                ? SelectionController.Main.SelectedObjects.Count : 1;

            var btn = new ContextMenuButton(
                "setEditableTint", "Change colour",
                selCount == 1 ? "Change the colour of this object" : "Change the colour of these objects",
                new UnityAction[]
                {
                    () => Utils.OpenColourInputDialog<NewEditableTintBehaviour>(
                        Tint, "Pick a colour",
                        selCount == 1 ? "Set tint for selected object" : "Set tint for selected objects",
                        (obj, c) => obj.SetTint(c))
                });
            btn.LabelWhenMultipleAreSelected = "Change colour";
            buttons.Add(btn);
        }

        public void SetTint(Color color)
        {
            Tint = color;

            foreach (var o in overlays)
                if (o.overlay != null) Destroy(o.overlay.gameObject);
            overlays.Clear();

            ApplyOverlayTo(GetComponent<SpriteRenderer>(), color);

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform) continue;
                if (child.GetComponent<TintOverlayMarker>() != null) continue;
                if (child.GetComponent<LiquidContainerController>() != null) continue;
                if (child.GetComponent<NewEditableTintBehaviour>() != null) continue;

                var csr = child.GetComponent<SpriteRenderer>();
                if (csr != null) ApplyOverlayTo(csr, color);
            }

            var blood = GetComponent<BloodContainer>();
            if (blood != null && GetComponent<LimbBehaviour>() == null)
                SetCachedComputedColour(blood, color);

            var display = GetComponent<DisplayBehaviour>();
            var thermo = GetComponent<ThermometerBehaviour>();
            if (display != null || thermo != null)
            {
                var textT = transform.Find("Text");
                if (textT != null)
                {
                    var jitter = textT.GetComponent<HologramJitter>();
                    if (jitter != null) Destroy(jitter);
                }
                if (display != null && display.TextMesh != null)
                    display.TextMesh.color = color;
            }
        }

        private void ApplyOverlayTo(SpriteRenderer baseSR, Color color)
        {
            if (baseSR == null || baseSR.sprite == null) return;

            var go = new GameObject("TintOverlay");
            go.AddComponent<TintOverlayMarker>();
            go.transform.SetParent(baseSR.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = baseSR.sprite;
            overlay.sortingLayerName = baseSR.sortingLayerName;
            overlay.sortingOrder = baseSR.sortingOrder + 1;

            if (baseSR.sharedMaterial != null)
                overlay.sharedMaterial = baseSR.sharedMaterial;

            overlay.color = color;
            overlay.enabled = color.a > 0.01f;

            overlays.Add(new Ovl { overlay = overlay, baseSR = baseSR, lastSprite = baseSR.sprite });
        }

        private void OnDestroy()
        {
            foreach (var o in overlays)
                if (o.overlay != null) Destroy(o.overlay.gameObject);
        }

        private static void SetCachedComputedColour(BloodContainer bc, Color color)
        {
            if (CachedColourField != null)
                CachedColourField.SetValue(bc, color);
        }
    }

    // ================= RPM DISPLAY =================
    public class RpmDisplay : MonoBehaviour
    {
        public TextMeshPro display;
        public Rigidbody2D rb;
        private GameObject holder;
        private PhysicalBehaviour _pb;

        private void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null) return;

            _pb = GetComponent<PhysicalBehaviour>();

            if (_pb != null && _pb.ContextMenuOptions != null)
            {
                var buttons = _pb.ContextMenuOptions.Buttons;
                for (int i = buttons.Count - 1; i >= 0; i--)
                    if (buttons[i].Identity == "AddRpmDisplay")
                        buttons.RemoveAt(i);
            }

            var holo = ModAPI.FindSpawnable("Holographic Display");
            if (holo == null || holo.Prefab == null)
            {
                Plugin.Log.LogWarning("[PPGMod] Holographic Display prefab not found");
                return;
            }

            var textT = holo.Prefab.transform.Find("Text");
            if (textT == null)
            {
                Plugin.Log.LogWarning("[PPGMod] Holographic Display has no 'Text' child");
                return;
            }

            holder = new GameObject("RpmHolder");
            var cloned = Instantiate(textT.gameObject, holder.transform);
            cloned.transform.localPosition = Vector3.zero;
            display = cloned.GetComponent<TextMeshPro>();

            if (_pb != null && _pb.ContextMenuOptions != null)
            {
                _pb.ContextMenuOptions.Buttons.Add(new ContextMenuButton(
                    "RemoveRpmDisplay", "Remove RPM Display", "Remove RPM Display",
                    () => Destroy(this)));
            }
        }

        private void Update()
        {
            if (display != null && rb != null && holder != null)
            {
                display.text = Math.Round(rb.angularVelocity / 360f * 60f, 2) + " RPM\n"
                             + Math.Round(rb.angularVelocity, 2) + " AV";
                holder.transform.position = transform.position;
                holder.transform.rotation = Quaternion.identity;
            }
        }

        private void OnDestroy()
        {
            if (holder != null) Destroy(holder);

            if (_pb != null && _pb.ContextMenuOptions != null)
            {
                var buttons = _pb.ContextMenuOptions.Buttons;

                for (int i = buttons.Count - 1; i >= 0; i--)
                    if (buttons[i].Identity == "RemoveRpmDisplay")
                        buttons.RemoveAt(i);

                bool hasAdd = false;
                for (int i = 0; i < buttons.Count; i++)
                    if (buttons[i].Identity == "AddRpmDisplay") { hasAdd = true; break; }

                if (!hasAdd)
                {
                    buttons.Add(new ContextMenuButton(
                        "AddRpmDisplay", "Add RPM Display", "Shows RPM and angular velocity",
                        new UnityAction[] { delegate () {
                            if (_pb.GetComponent<RpmDisplay>() == null)
                                _pb.gameObject.AddComponent<RpmDisplay>();
                        }}));
                }
            }
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class RpmButtonPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "AddRpmDisplay") return;

            var btn = new ContextMenuButton(
                "AddRpmDisplay",
                "Add RPM Display",
                "Shows RPM and angular velocity above the object",
                new UnityAction[] { delegate () {
                    if (__instance.GetComponent<RpmDisplay>() == null)
                        __instance.gameObject.AddComponent<RpmDisplay>();
                }}
            );
            btn.LabelWhenMultipleAreSelected = "Add RPM Display";
            buttons.Add(btn);
        }
    }

    // ================= CHANGE GRIP (только для Wheel) =================
    public class WheelGripMarker : MonoBehaviour
    {
        public float GripValue = 70f;
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class WheelGripMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            if (__instance.gameObject.name != "Wheel") return;

            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            var marker = __instance.GetComponent<WheelGripMarker>();
            if (marker == null) marker = __instance.gameObject.AddComponent<WheelGripMarker>();

            // Всегда применяем grip (в т.ч. при загрузке постройки)
            ApplyGrip(__instance, marker.GripValue);

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "changeGrip") return;

            buttons.Add(new ContextMenuButton(
                "changeGrip",
                "Change grip",
                "Set wheel grip. 70 = vanilla, 100 = maximum (no sliding)",
                new UnityAction[] { delegate ()
                {
                    var m = __instance.GetComponent<WheelGripMarker>();
                    if (m == null) return;

                    Utils.OpenFloatInputDialog<WheelGripMarker>(
                        m.GripValue, m,
                        (WheelGripMarker mk, float v) =>
                        {
                            mk.GripValue = Mathf.Clamp(v, 0f, 100f);
                            ApplyGrip(__instance, mk.GripValue);
                        },
                        "Change grip", "Grip (0 - 100)");
                }}
            ));
        }

        static void ApplyGrip(PhysicalBehaviour pb, float value)
        {
            if (pb == null) return;

            var colliders = pb.GetComponents<Collider2D>();
            if (colliders == null || colliders.Length == 0) return;

            float friction;
            if (value >= 100f)
                friction = float.MaxValue;
            else
                friction = (value / 70f) * 2f;

            var mat = new PhysicsMaterial2D("WheelGrip_Custom");
            mat.friction = friction;
            mat.bounciness = 0f;

            foreach (var col in colliders)
            {
                col.sharedMaterial = mat;
                col.enabled = false;
                col.enabled = true;
            }
        }
    }

    // ================= МОЩНОСТЬ РОТОРА =================
    public class RotorPowerMarker : MonoBehaviour
    {
        public float Power = 10f;
    }

    [HarmonyPatch(typeof(RotorBehaviour), "Start")]
    public static class RotorPowerMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(RotorBehaviour __instance)
        {
            if (__instance == null) return;
            var pb = __instance.PhysicalBehaviour;
            if (pb == null || pb.ContextMenuOptions == null) return;

            var buttons = pb.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "rotorSetPower") return;

            var button = new ContextMenuButton(
                "rotorSetPower",
                "Set rotor power",
                "Set rotor power (1 = weak, 100 = very strong). Default 10.",
                new UnityAction[] { delegate () { OpenPowerDialog(__instance); } }
            )
            {
                LabelWhenMultipleAreSelected = "Set rotor power"
            };
            buttons.Add(button);
        }

        static void OpenPowerDialog(RotorBehaviour rotor)
        {
            if (rotor == null) return;
            var m = rotor.GetComponent<RotorPowerMarker>();
            float current = (m != null) ? m.Power : 10f;

            Utils.OpenFloatInputDialog<RotorBehaviour>(
                current, rotor,
                delegate (RotorBehaviour w, float v)
                {
                    if (w == null) return;
                    var marker = w.GetComponent<RotorPowerMarker>();
                    if (marker == null) marker = w.gameObject.AddComponent<RotorPowerMarker>();
                    marker.Power = Mathf.Clamp(v, 1f, 100f);
                },
                "Set rotor power", "Power (1-100)");
        }
    }

    [HarmonyPatch(typeof(RotorBehaviour), "FixedUpdate")]
    public static class RotorPowerFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(RotorBehaviour __instance)
        {
            if (__instance == null || __instance.Hinge == null) return true;
            if (__instance.PhysicalBehaviour == null) return true;

            float powerMul = 1f;
            var marker = __instance.GetComponent<RotorPowerMarker>();
            if (marker != null)
                powerMul = Mathf.Clamp(marker.Power / 10f, 0.1f, 10f);

            JointMotor2D motor = __instance.Hinge.motor;
            motor.maxMotorTorque = (100f + __instance.PhysicalBehaviour.Charge * 4f) * powerMul;
            motor.motorSpeed = (__instance.Activated ? -__instance.GetScaledSpeed() : 0f);
            __instance.Hinge.motor = motor;

            return false;
        }
    }

    [HarmonyPatch(typeof(RotorBehaviour), "SetSpeed")]
    public static class RotorSpeedLimitPatch
    {
        [HarmonyPrefix]
        static bool Prefix(RotorBehaviour __instance, float v)
        {
            if (__instance == null) return false;
            __instance.Speed = v;
            return false;
        }
    }

    // ================= ЯДЕРНЫЙ УДАР =================
#pragma warning disable 0612
    [HarmonyPatch(typeof(AtomBombExplosionBehaviour), "OnTriggerEnter2D")]
    public static class NukeShockwaveBlockPatch
    {
        [HarmonyPrefix]
        static bool Prefix(AtomBombExplosionBehaviour __instance, Collider2D collider)
        {
            if (__instance == null || collider == null) return true;
            if (Global.main == null) return true;

            PhysicalBehaviour pb;
            if (!Global.main.PhysicalObjectsInWorldByTransform.TryGetValue(collider.transform, out pb))
                return true;

            Vector2 origin = __instance.transform.position;
            Vector2 target = pb.transform.position;
            Vector2 dir = target - origin;
            float dist = dir.magnitude;
            if (dist < 0.01f) return true;

            int mask = LayerMask.GetMask("Objects", "Bounds");
            if (mask == 0) return true;

            var hit = Physics2D.Raycast(origin, dir.normalized, dist, mask);
            if (hit.collider != null && hit.collider != collider)
                return false;

            return true;
        }
    }
#pragma warning restore 0612

    // ================= ВЕС ОБЪЕКТА =================
    public class WeightMarker : MonoBehaviour
    {
        public float Value = -1f;
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class ObjectPropertiesMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;

            // Восстанавливаем сохранённый вес
            var marker = __instance.GetComponent<WeightMarker>();
            if (marker != null && marker.Value > 0f)
            {
                __instance.rigidbody.mass = marker.Value;
                __instance.TrueInitialMass = marker.Value;
                __instance.InitialMass = marker.Value;
            }

            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "setWeight") return;

            buttons.Add(new ContextMenuButton(
                "setWeight",
                "Set Weight",
                "Set object weight in kilograms (1000 = 1 ton)",
                new UnityAction[] { delegate ()
                {
                    if (__instance.rigidbody == null) return;
                    float currentKg = __instance.rigidbody.mass * 1000f;

                    Utils.OpenFloatInputDialog<PhysicalBehaviour>(
                        currentKg,
                        __instance,
                        (PhysicalBehaviour pb, float v) =>
                        {
                            if (pb == null || pb.rigidbody == null) return;
                            float mass = Mathf.Max(0.00001f, v / 1000f);
                            pb.rigidbody.mass = mass;
                            pb.TrueInitialMass = mass;
                            pb.InitialMass = mass;

                            var m = pb.GetComponent<WeightMarker>();
                            if (m == null) m = pb.gameObject.AddComponent<WeightMarker>();
                            m.Value = mass;
                        },
                        "Set Weight",
                        "Weight (kg)"
                    );
                }}));
        }
    }

    // ================= FLIP =================
    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class FlipMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "flipLR") return;

            buttons.Add(new ContextMenuButton(
                "flipLR",
                "Flip left/right",
                "Mirror this object horizontally",
                new UnityAction[] { delegate () {
                    var s = __instance.transform.localScale;
                    s.x *= -1f;
                    __instance.transform.localScale = s;
                }}
            ));

            buttons.Add(new ContextMenuButton(
                "flipUD",
                "Flip up/down",
                "Mirror this object vertically",
                new UnityAction[] { delegate () {
                    var s = __instance.transform.localScale;
                    s.y *= -1f;
                    __instance.transform.localScale = s;
                }}
            ));
        }
    }

    // ================= THROTTLE ЗВУКОВ =================
    [HarmonyPatch(typeof(PhysicalBehaviour), "PlayClipOnce", new Type[] { typeof(AudioClip), typeof(float) })]
    public static class AudioThrottlePatch
    {
        public const int MaxClipsPerFrame = 8;
        private static uint frameCounter = 0;
        private static int clipsThisFrame = 0;

        [HarmonyPrefix]
        static bool Prefix(AudioClip clip, float volume)
        {
            uint f = (uint)Time.frameCount;
            if (f != frameCounter)
            {
                frameCounter = f;
                clipsThisFrame = 0;
            }
            if (clipsThisFrame >= MaxClipsPerFrame)
                return false;
            clipsThisFrame++;
            return true;
        }
    }

    // ================= DETECTOR RANGE CUSTOM =================
    [HarmonyPatch(typeof(DetectorBehaviour), "Start")]
    public static class DetectorRangePatch
    {
        [HarmonyPostfix]
        static void Postfix(DetectorBehaviour __instance)
        {
            if (__instance == null || __instance.PhysicalBehaviour == null) return;
            if (__instance.PhysicalBehaviour.ContextMenuOptions == null) return;
            var buttons = __instance.PhysicalBehaviour.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "setDetectorRangeCustom") return;

            buttons.Add(new ContextMenuButton(
                "setDetectorRangeCustom",
                "Set range (custom)",
                "Set detector range from 0.05 to 10 meters",
                new UnityAction[] { delegate ()
                {
                    Utils.OpenFloatInputDialog<DetectorBehaviour>(
                        __instance.Range, __instance,
                        (DetectorBehaviour d, float v) =>
                        {
                            d.Range = Mathf.Clamp(v, 0.05f, 10f);
                        },
                        "Set detector range", "Range (0.05 - 10)");
                }}
            ));
        }
    }

    // ================= POINTER → CAM FOLLOWER =================
    public class PointerCamFollowerBehaviour : MonoBehaviour
    {
        public float TargetAngle = 90f;
        public float Tolerance = 0.97f;
        public bool DoubleTrigger = false;
        public PhysicalBehaviour phys;
        public Transform Wheel;
        public SpriteRenderer Light;

        private bool done;
        private float lastDegree;
        private float currentDegree;
        private float _originalDelay;

        private void Start()
        {
            phys = GetComponent<PhysicalBehaviour>();
            if (phys != null)
            {
                _originalDelay = phys.ActivationPropagationDelay;
                phys.ActivationPropagationDelay = 0;
            }

            // Re-find PointerBehaviour при загрузке (ссылки не сохраняются)
            var pointer = GetComponent<PointerBehaviour>();
            if (pointer != null) Wheel = pointer.RotorWheel;

            var lightT = transform.Find("Active light");
            if (lightT != null) Light = lightT.GetComponent<SpriteRenderer>();

            if (Wheel != null)
                currentDegree = Wheel.eulerAngles.z + 90f;

            if (phys != null && phys.ContextMenuOptions != null)
            {
                var buttons = phys.ContextMenuOptions.Buttons;

                for (int i = buttons.Count - 1; i >= 0; i--)
                    if (buttons[i].Identity == "setCamAngleCaF" || buttons[i].Identity == "switchTriggerModeCaF")
                        buttons.RemoveAt(i);

                buttons.Add(new ContextMenuButton(
                    "setCamAngleCaF",
                    "Set angle CaF",
                    "Set the angle at which the signal is triggered",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<PointerCamFollowerBehaviour>(
                            TargetAngle, this,
                            (PointerCamFollowerBehaviour b, float v) => b.TargetAngle = v,
                            "Set angle CaF", "Angle");
                    }}
                ));

                buttons.Add(new ContextMenuButton(
                    "switchTriggerModeCaF",
                    () => DoubleTrigger ? "Switch trigger: Single trigger" : "Switch trigger: Double trigger",
                    "Single = one signal on reach. Double = signal on reach and on release.",
                    new UnityAction[] { delegate () { DoubleTrigger = !DoubleTrigger; } }
                ));
            }
        }

        private void OnDestroy()
        {
            if (phys != null)
                phys.ActivationPropagationDelay = _originalDelay;
        }

        private void FixedUpdate()
        {
            if (Wheel == null || phys == null) return;

            lastDegree = currentDegree;
            currentDegree = Wheel.eulerAngles.z + 90f;

            float rpm = Mathf.Abs(AVtoRPM(Wheel));
            bool fastSpin = rpm > 80f;

            bool nowReached = fastSpin
                ? AnglePassed(lastDegree, currentDegree, TargetAngle)
                : Reached;

            if (!done && nowReached)
            {
                done = true;
                phys.ForceSendUse();
                if (Light != null) Light.enabled = true;
            }
            else if (!nowReached && done)
            {
                if (DoubleTrigger) phys.ForceSendUse();
                done = false;
                if (Light != null) Light.enabled = false;
            }
        }

        public bool Reached
        {
            get
            {
                if (Wheel == null) return false;
                Vector2 a = AngleToUnitVector(transform, TargetAngle).normalized;
                Vector2 b = Wheel.up.normalized;
                return Vector2.Dot(a, b) >= Tolerance;
            }
        }

        private bool AnglePassed(float from, float to, float target)
        {
            from = NormalizeAngle(from);
            to = NormalizeAngle(to);
            target = NormalizeAngle(transform.eulerAngles.z + target);
            float delta = Mathf.DeltaAngle(from, to);
            float deltaTarget = Mathf.DeltaAngle(from, target);
            return Mathf.Sign(delta) == Mathf.Sign(deltaTarget)
                && Mathf.Abs(deltaTarget) <= Mathf.Abs(delta);
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle < 0f) angle += 360f;
            return angle;
        }

        public static Vector2 AngleToUnitVector(Transform t, float degrees)
        {
            float total = degrees + t.eulerAngles.z;
            float rad = total * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        public static float AVtoRPM(Transform t)
        {
            var rb = t.GetComponent<Rigidbody2D>();
            if (rb == null) return 0f;
            return rb.angularVelocity / 360f * 60f;
        }
    }

    public class PointerModeSwitcher : MonoBehaviour
    {
        public PointerBehaviour Pointer;
        public bool IsCamFollower;

        public static void AddTo(GameObject go)
        {
            if (go.GetComponent<PointerModeSwitcher>() != null) return;
            var p = go.GetComponent<PointerBehaviour>();
            if (p == null) return;
            var s = go.AddComponent<PointerModeSwitcher>();
            s.Pointer = p;
        }

        private void Start()
        {
            // Re-find PointerBehaviour при загрузке (ссылки не сохраняются)
            if (Pointer == null) Pointer = GetComponent<PointerBehaviour>();
            UpdateButton();
        }

        public void Toggle()
        {
            IsCamFollower = !IsCamFollower;

            if (IsCamFollower)
            {
                if (Pointer != null) Pointer.enabled = false;
                if (gameObject.GetComponent<PointerCamFollowerBehaviour>() == null)
                    gameObject.AddComponent<PointerCamFollowerBehaviour>();
            }
            else
            {
                if (Pointer != null) Pointer.enabled = true;

                var caf = gameObject.GetComponent<PointerCamFollowerBehaviour>();
                if (caf != null)
                {
                    if (Pointer != null && Pointer.PhysicalBehaviour != null &&
                        Pointer.PhysicalBehaviour.ContextMenuOptions != null)
                    {
                        var btns = Pointer.PhysicalBehaviour.ContextMenuOptions.Buttons;
                        for (int i = btns.Count - 1; i >= 0; i--)
                            if (btns[i].Identity == "setCamAngleCaF" || btns[i].Identity == "switchTriggerModeCaF")
                                btns.RemoveAt(i);
                    }
                    Destroy(caf);
                }
            }
            UpdateButton();
        }

        private void UpdateButton()
        {
            if (Pointer == null || Pointer.PhysicalBehaviour == null ||
                Pointer.PhysicalBehaviour.ContextMenuOptions == null) return;
            var buttons = Pointer.PhysicalBehaviour.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "pointerSwitchMode")
                    buttons.RemoveAt(i);

            buttons.Add(new ContextMenuButton(
                "pointerSwitchMode",
                IsCamFollower ? "Switch mode: Pointer" : "Switch mode: Cam Follower",
                "Switch between Pointer and Cam Follower mode",
                new UnityAction[] { delegate () { Toggle(); } }
            ));
        }
    }

    [HarmonyPatch(typeof(PointerBehaviour), "Start")]
    public static class PointerModePatch
    {
        [HarmonyPostfix]
        static void Postfix(PointerBehaviour __instance)
        {
            PointerModeSwitcher.AddTo(__instance.gameObject);
        }
    }

    // ================= PISTON → SPRING MODE =================
    public class PistonSpringBehaviour : MonoBehaviour
    {
        public enum SpringDirection { Inner, Middle, Outer }

        public SliderJoint2D Slider;
        public Rigidbody2D HeadRb;
        public Transform PistonHead;

        public float SpringFrequency = 0f;
        public float SpringDamping = 0.5f;
        public SpringDirection Direction = SpringDirection.Inner;

        private SpringJoint2D _spring;
        private SpringDirection _prevDirection;
        private Vector2 _originalMinMax;
        private Vector3 _lastScale;

        private void Start()
        {
            var piston = GetComponent<PistonBehaviour>();
            if (piston == null) return;

            Slider = piston.Slider;
            PistonHead = piston.PistonHead;
            if (PistonHead == null || Slider == null) return;

            HeadRb = PistonHead.GetComponent<Rigidbody2D>();
            if (HeadRb == null) return;

            Slider.useMotor = false;
            Slider.useLimits = true;

            _prevDirection = Direction;
            _originalMinMax = new Vector2(Slider.limits.min, Slider.limits.max);
            _lastScale = transform.localScale;

            var pb = GetComponent<PhysicalBehaviour>();
            if (pb != null && pb.ContextMenuOptions != null)
            {
                var buttons = pb.ContextMenuOptions.Buttons;

                for (int i = buttons.Count - 1; i >= 0; i--)
                    if (buttons[i].Identity == "setSpringFrequency" ||
                        buttons[i].Identity == "switchSpringDirection")
                        buttons.RemoveAt(i);

                buttons.Add(new ContextMenuButton(
                    "setSpringFrequency",
                    "Set Spring Power",
                    "0 = free movement, higher = stronger spring",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<PistonSpringBehaviour>(
                            SpringFrequency, this,
                            (PistonSpringBehaviour b, float v) =>
                            {
                                b.SpringFrequency = Mathf.Max(0f, v);
                                if (b._spring != null)
                                    b._spring.frequency = Mathf.Max(0.01f, b.SpringFrequency);
                                else if (b.SpringFrequency > 0f)
                                    b.CreateSpring();
                            },
                            "Set Spring Power", "Frequency");
                    }}
                ));

                buttons.Add(new ContextMenuButton(
                    "switchSpringDirection",
                    () => Direction == SpringDirection.Inner ? "Switch spring: Middle" :
                          Direction == SpringDirection.Middle ? "Switch spring: Outer" :
                          "Switch spring: Inner",
                    "Cycle spring direction: Inner, Middle, Outer",
                    new UnityAction[] { delegate () { CycleDirection(); } }
                ));
            }

            if (SpringFrequency > 0f) CreateSpring();
        }

        private void CycleDirection()
        {
            Direction = Direction == SpringDirection.Inner ? SpringDirection.Middle :
                        Direction == SpringDirection.Middle ? SpringDirection.Outer :
                        SpringDirection.Inner;
        }

        private void Update()
        {
            if (Direction != _prevDirection)
            {
                _prevDirection = Direction;
                if (_spring != null) Destroy(_spring);
                _spring = null;
                if (SpringFrequency > 0f) CreateSpring();
            }

            if (transform.localScale != _lastScale)
            {
                _lastScale = transform.localScale;
                float scale = Mathf.Abs(transform.localScale.x);
                var limits = Slider.limits;
                limits.min = _originalMinMax.x * scale;
                limits.max = _originalMinMax.y * scale;
                Slider.limits = limits;

                if (_spring != null) Destroy(_spring);
                _spring = null;
                if (SpringFrequency > 0f) CreateSpring();
            }
        }

        private void CreateSpring()
        {
            if (HeadRb == null || PistonHead == null || Slider == null) return;

            var existing = GetComponent<SpringJoint2D>();
            if (existing != null) Destroy(existing);

            _spring = gameObject.AddComponent<SpringJoint2D>();
            _spring.connectedBody = HeadRb;
            _spring.autoConfigureDistance = false;
            _spring.dampingRatio = SpringDamping;
            _spring.frequency = Mathf.Max(0.01f, SpringFrequency);
            _spring.enableCollision = false;

            float minL = Slider.limits.min;
            float maxL = Slider.limits.max;

            switch (Direction)
            {
                case SpringDirection.Inner:
                    _spring.distance = Mathf.Min(minL, maxL);
                    break;
                case SpringDirection.Middle:
                    _spring.distance = (minL + maxL) / 2f;
                    break;
                case SpringDirection.Outer:
                    _spring.distance = Mathf.Max(minL, maxL);
                    break;
            }
        }

        private void OnDestroy()
        {
            if (_spring != null) Destroy(_spring);
        }
    }

    public class PistonModeSwitcher : MonoBehaviour
    {
        public PistonBehaviour Piston;
        public bool IsSpringMode;

        public static void AddTo(GameObject go)
        {
            if (go.GetComponent<PistonModeSwitcher>() != null) return;
            var p = go.GetComponent<PistonBehaviour>();
            if (p == null) return;
            var s = go.AddComponent<PistonModeSwitcher>();
            s.Piston = p;
        }

        private void Start()
        {
            // Re-find PistonBehaviour при загрузке (ссылки не сохраняются)
            if (Piston == null) Piston = GetComponent<PistonBehaviour>();
            UpdateButton();
        }

        public void Toggle()
        {
            IsSpringMode = !IsSpringMode;

            if (IsSpringMode)
            {
                if (Piston != null) Piston.enabled = false;
                if (gameObject.GetComponent<PistonSpringBehaviour>() == null)
                    gameObject.AddComponent<PistonSpringBehaviour>();
            }
            else
            {
                if (Piston != null) Piston.enabled = true;

                var spring = gameObject.GetComponent<PistonSpringBehaviour>();
                if (spring != null)
                {
                    if (Piston != null && Piston.PhysicalBehaviour != null &&
                        Piston.PhysicalBehaviour.ContextMenuOptions != null)
                    {
                        var btns = Piston.PhysicalBehaviour.ContextMenuOptions.Buttons;
                        for (int i = btns.Count - 1; i >= 0; i--)
                            if (btns[i].Identity == "setSpringFrequency" || btns[i].Identity == "switchSpringDirection")
                                btns.RemoveAt(i);
                    }
                    Destroy(spring);
                }
            }
            UpdateButton();
        }

        private void UpdateButton()
        {
            if (Piston == null || Piston.PhysicalBehaviour == null ||
                Piston.PhysicalBehaviour.ContextMenuOptions == null) return;
            var buttons = Piston.PhysicalBehaviour.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "pistonSwitchMode")
                    buttons.RemoveAt(i);

            buttons.Add(new ContextMenuButton(
                "pistonSwitchMode",
                IsSpringMode ? "Switch mode: Piston" : "Switch mode: Spring",
                "Switch between Piston and Spring mode",
                new UnityAction[] { delegate () { Toggle(); } }
            ));
        }
    }

    [HarmonyPatch(typeof(PistonBehaviour), "Start")]
    public static class PistonModePatch
    {
        [HarmonyPostfix]
        static void Postfix(PistonBehaviour __instance)
        {
            PistonModeSwitcher.AddTo(__instance.gameObject);
        }
    }
}