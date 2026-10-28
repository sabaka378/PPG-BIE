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
        public static bool P_enable = false;
    }

    // ================= ПЛАГИН =================
    [BepInPlugin("com.sabaka.ppgmod", "PPG Mod", "1.16.0")]
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
                var harmony = new Harmony("com.sabaka.ppgmod");
                
                try
                {
                    harmony.PatchAll();
                }
                catch (Exception ex)
                {
                    Log.LogWarning("Some patches failed to apply: " + ex.Message);
                }
                
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
            KeyState.P_enable = Input.GetKey(KeyCode.P);
        }
    }

    // ================= ПЕРЕКЛЮЧЕНИЕ РЕЖИМОВ (ВКЛ/ВЫКЛ) =================
    [HarmonyPatch(typeof(PhysicalBehaviour), "Use")]
    public static class ModeTogglePatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null) return;

            var pointerCam = __instance.GetComponent<PointerCamFollowerBehaviour>();
            if (pointerCam != null && pointerCam.SuppressSelfToggle) return;

            var rotorMarker = __instance.GetComponent<RotorModeMarker>();
            if (rotorMarker != null && rotorMarker.SuppressSelfToggle) return;

            if (rotorMarker != null)
            {
                rotorMarker.Enabled = !rotorMarker.Enabled;
                Plugin.Log.LogInfo($"Rotor mode toggled: {(rotorMarker.Enabled ? "ON" : "OFF")}");
            }

            var servoMarker = __instance.GetComponent<ServoModeMarker>();
            if (servoMarker != null)
            {
                servoMarker.Enabled = !servoMarker.Enabled;
                Plugin.Log.LogInfo($"Servo mode toggled: {(servoMarker.Enabled ? "ON" : "OFF")}");
            }

            if (pointerCam != null)
            {
                pointerCam.Enabled = !pointerCam.Enabled;
                Plugin.Log.LogInfo($"Pointer mode toggled: {(pointerCam.Enabled ? "ON" : "OFF")}");
            }
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

            if (invisibleMarker != null && invisibleMarker.IsInvisible)
            {
                if (lr != null) lr.enabled = false;
            }
            else if (KeyState.K_enable)
            {
                if (invisibleMarker == null)
                    invisibleMarker = __instance.gameObject.AddComponent<InvisibleWireMarker>();
                invisibleMarker.IsInvisible = true;
                if (lr != null) lr.enabled = false;
            }
            else
            {
                if (lr != null)
                {
                    lr.enabled = true;
                    lr.startColor = Color.white;
                    lr.endColor = Color.white;
                }
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

    // ================= БЕСКОНЕЧНЫЕ ПРОВОДА =================
    public class InfiniteWireMarker : MonoBehaviour { }

    [HarmonyPatch(typeof(WireBehaviour), "Initialise")]
    public static class InfiniteWirePatch
    {
        [HarmonyPostfix]
        static void Postfix(WireBehaviour __instance)
        {
            if (__instance == null) return;
            if (!KeyState.P_enable) return;

            string typeName = __instance.GetType().Name;
            if (typeName.Contains("Fixed") || typeName.Contains("Rigid") ||
                typeName.Contains("Chain") || typeName.Contains("Spring"))
                return;

            if (__instance.GetComponent<InfiniteWireMarker>() == null)
                __instance.gameObject.AddComponent<InfiniteWireMarker>();

            var distanceJoint = __instance.GetComponent<DistanceJoint2D>();
            if (distanceJoint != null)
            {
                distanceJoint.enabled = false;
                distanceJoint.breakForce = float.MaxValue;
                distanceJoint.breakTorque = float.MaxValue;
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

    // ================= НЕВИДИМЫЕ РЕМНИ (ВИЗУАЛ) =================
    public class InvisibleBeltMarker : MonoBehaviour { }

    [HarmonyPatch(typeof(SerpentineBeltBehaviour))]
    public static class InvisibleBeltVisualPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        static void StartPostfix(SerpentineBeltBehaviour __instance)
        {
            if (__instance == null) return;
            if (KeyState.K_enable)
            {
                if (__instance.GetComponent<InvisibleBeltMarker>() == null)
                    __instance.gameObject.AddComponent<InvisibleBeltMarker>();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Update")]
        static void BeltInvisibleUpdatePatch(SerpentineBeltBehaviour __instance)
        {
            if (__instance == null) return;
            var marker = __instance.GetComponent<InvisibleBeltMarker>();
            if (marker == null) return;

            var lr = __instance.GetComponent<LineRenderer>();
            if (lr == null) lr = __instance.GetComponentInChildren<LineRenderer>(true);
            if (lr == null) return;

            Color transparent = new Color(0f, 0f, 0f, 0f);
            lr.startColor = transparent;
            lr.endColor = transparent;
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnMouseOverlapEvent")]
        static void PostfixOnMouseOverlap(SerpentineBeltBehaviour __instance, bool overlap)
        {
            if (__instance == null) return;
            var marker = __instance.GetComponent<InvisibleBeltMarker>();
            if (marker == null) return;

            var lr = __instance.GetComponent<LineRenderer>();
            if (lr == null) lr = __instance.GetComponentInChildren<LineRenderer>(true);
            if (lr == null) return;

            if (overlap && UserPreferenceManager.Current.ShowOutlines)
            {
                lr.startColor = Color.red;
                lr.endColor = Color.red;
            }
            else
            {
                Color transparent = new Color(0f, 0f, 0f, 0f);
                lr.startColor = transparent;
                lr.endColor = transparent;
            }
        }
    }

    // ================= РЕМЕНЬ БЕЗ СКОЛЬЖЕНИЯ (ВАНИЛЬНЫЙ ВИЗУАЛ, ФИЗИКА МОДА) =================
    public class BeltNoGearRatioMarker : MonoBehaviour { }

    [HarmonyPatch(typeof(SerpentineBeltBehaviour))]
    public static class SlipFreeBeltPatch
    {
        private static readonly Dictionary<SerpentineBeltBehaviour, RelativeJoint2D> Joints
            = new Dictionary<SerpentineBeltBehaviour, RelativeJoint2D>();
        private static readonly Dictionary<SerpentineBeltBehaviour, float> InitialRotations
            = new Dictionary<SerpentineBeltBehaviour, float>();

        private static readonly FieldInfo GearRatioField =
            AccessTools.Field(typeof(SerpentineBeltBehaviour), "gearRatio");

        private static void UpdateAngleOffset(SerpentineBeltBehaviour instance, bool force = false)
        {
            if (!Joints.TryGetValue(instance, out var joint) || joint == null) return;
            if (instance.Self == null || instance.Other == null) return;

            float gearRatio = 1f;
            if (GearRatioField != null)
            {
                gearRatio = (float)GearRatioField.GetValue(instance);
                if (gearRatio <= 0f) gearRatio = 1f;
            }

            if (instance.GetComponent<BeltNoGearRatioMarker>() != null)
                gearRatio = 1f;

            float gr = gearRatio - 1f;
            float d = (instance.Self.rigidbody.rotation) * gr;
            
            bool paused = Global.main != null && Global.main.Paused && Time.timeScale < 0.001f;
            
            if (Mathf.Approximately(gr, 0f)) 
            {
                if (paused || force)
                    InitialRotations[instance] = instance.Other.rigidbody.rotation - instance.Self.rigidbody.rotation;
                joint.angularOffset = InitialRotations[instance];
            }
            else 
            {
                if (paused || force)
                    InitialRotations[instance] = (instance.Other.rigidbody.rotation - instance.Self.rigidbody.rotation) - d;
                joint.angularOffset = InitialRotations[instance] + d;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        static void StartPostfix(SerpentineBeltBehaviour __instance)
        {
            if (__instance.Self == null || __instance.Other == null) return;
            if (__instance.Self == __instance.Other) return;
            if (__instance.Self.rigidbody == null || __instance.Other.rigidbody == null) return;

            if (KeyState.L_enable && __instance.GetComponent<BeltNoGearRatioMarker>() == null)
                __instance.gameObject.AddComponent<BeltNoGearRatioMarker>();

            var friction = __instance.GetComponent<FrictionJoint2D>();
            if (friction != null) UnityEngine.Object.Destroy(friction);
            var existingRel = __instance.GetComponent<RelativeJoint2D>();
            if (existingRel != null) UnityEngine.Object.Destroy(existingRel);

            var joint = __instance.Self.gameObject.AddComponent<RelativeJoint2D>();
            joint.connectedBody = __instance.Other.rigidbody;
            joint.autoConfigureOffset = false;
            joint.maxForce = 0f;
            joint.maxTorque = 50000f;
            joint.correctionScale = 0.5f;
            joint.enableCollision = false;

            Joints[__instance] = joint;
            InitialRotations[__instance] = 0f;

            UpdateAngleOffset(__instance, true); 
        }

        [HarmonyPrefix]
        [HarmonyPatch("Update")]
        static void UpdatePrefix(SerpentineBeltBehaviour __instance)
        {
            if (__instance.Self == null || __instance.Other == null) return;
            UpdateAngleOffset(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch("FixedUpdate")]
        static bool FixedUpdatePrefix(SerpentineBeltBehaviour __instance)
        {
            if (__instance.Self == null || __instance.Other == null) return true;
            if (__instance.Self == __instance.Other) return true;
            if (__instance.Self.rigidbody == null || __instance.Other.rigidbody == null) return true;

            float selfAV = __instance.Self.rigidbody.angularVelocity;
            float otherAV = __instance.Other.rigidbody.angularVelocity;

            bool hasMotor = __instance.Self.GetComponent<RotorBehaviour>() != null ||
                            __instance.Other.GetComponent<RotorBehaviour>() != null ||
                            __instance.Self.GetComponent<MotorisedHingeBehaviour>() != null ||
                            __instance.Other.GetComponent<MotorisedHingeBehaviour>() != null;

            if (!hasMotor)
            {
                float limit = 5000f; 
                if (Mathf.Abs(selfAV) > limit) __instance.Self.rigidbody.angularVelocity = Mathf.Sign(selfAV) * limit;
                if (Mathf.Abs(otherAV) > limit) __instance.Other.rigidbody.angularVelocity = Mathf.Sign(otherAV) * limit;
            }

            return false; 
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnDestroy")]
        static void OnDestroyPostfix(SerpentineBeltBehaviour __instance)
        {
            if (Joints.TryGetValue(__instance, out var joint))
            {
                if (joint != null) UnityEngine.Object.Destroy(joint);
                Joints.Remove(__instance);
            }
            InitialRotations.Remove(__instance);
        }
    }

    // ================= PULSE DRUM =================
    public class PulseDrumModeMarker : MonoBehaviour
    {
        public bool ExplodeMode = false;
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class PulseDrumModeMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null) return;
            var pd = __instance.GetComponent<PulseDrumBehaviour>();
            if (pd == null) return;

            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "pulseDrumMode") return;

            var marker = pd.GetComponent<PulseDrumModeMarker>();
            if (marker == null) marker = pd.gameObject.AddComponent<PulseDrumModeMarker>();

            var btn = new ContextMenuButton(
                "pulseDrumMode",
                () => marker.ExplodeMode ? "Switch mode: Vanilla" : "Switch mode: Explode",
                "Switch between vanilla pulse and explosive mode",
                new UnityAction[] { delegate () { marker.ExplodeMode = !marker.ExplodeMode; } }
            );
            btn.LabelWhenMultipleAreSelected = "Switch mode";
            buttons.Add(btn);
        }
    }

    [HarmonyPatch(typeof(PulseDrumBehaviour), "Shoot")]
    public static class PulseDrumFixPatch
    {
        private static readonly RaycastHit2D[] _buffer = new RaycastHit2D[32];

        [HarmonyPrefix]
        static bool Prefix(PulseDrumBehaviour __instance)
        {
            if (__instance == null) return true;
            if (__instance.PhysicalBehaviour == null) return true;
            if (__instance.PhysicalBehaviour.rigidbody == null) return true;

            if (__instance.Source != null && __instance.Clips != null && __instance.Clips.Length > 0)
                __instance.Source.PlayOneShot(__instance.Clips[UnityEngine.Random.Range(0, __instance.Clips.Length)]);

            var marker = __instance.GetComponent<PulseDrumModeMarker>();
            bool explodeMode = marker != null && marker.ExplodeMode;

            Vector2 pos = __instance.BarrelPosition;
            Vector2 dir = __instance.BarrelDirection;
            float force = __instance.BaseForce + __instance.PhysicalBehaviour.Charge * __instance.ChargeInfluence;

            __instance.PhysicalBehaviour.rigidbody.AddForceAtPosition(-force * dir, pos);

            if (explodeMode)
            {
                float explosionForce = 150f + __instance.PhysicalBehaviour.Charge * 60f;
                float radius = 2.5f + __instance.PhysicalBehaviour.Charge * 0.1f;
                int rayCount = 32;

                int mask = LayerMask.GetMask("Objects", "Bounds");
                if (mask == 0) mask = ~0;

                for (int i = 0; i < rayCount; i++)
                {
                    float angle = (360f / rayCount) * i;
                    Vector2 rayDir = Quaternion.Euler(0f, 0f, angle) * Vector2.right;

                    int hitCount = Physics2D.RaycastNonAlloc(pos, rayDir, _buffer, radius, mask);

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
                            if (pb.rigidbody != null && pb != __instance.PhysicalBehaviour)
                            {
                                Vector2 targetPos = pb.transform.position;
                                Vector2 checkDir = targetPos - pos;
                                float checkDist = checkDir.magnitude;
                                if (checkDist > 0.01f)
                                {
                                    var wallHit = Physics2D.Raycast(pos, checkDir.normalized, checkDist, mask);
                                    if (wallHit.collider != null && wallHit.collider != hit.collider)
                                        continue;
                                }

                                float falloff = 1f - (bestDist / radius);
                                Vector3 v = explosionForce * falloff * pb.rigidbody.mass * rayDir;
                                pb.rigidbody.AddForceAtPosition(v, hit.point);
                            }
                        }
                    }
                }

                return false;
            }

            float halfSpread = __instance.SpreadAngle / 2f;
            float halfEmitter = __instance.EmitterSize / 2f;
            int rayCountV = __instance.RayCount;
            float step = rayCountV > 1 ? (float)(rayCountV - 1) : 1f;

            int maskV = __instance.Layers;
            int boundsLayer = LayerMask.NameToLayer("Bounds");
            if (boundsLayer >= 0)
                maskV &= ~(1 << boundsLayer);

            for (int i = 0; i < rayCountV; i++)
            {
                float t = i / step;
                float angle = t * __instance.SpreadAngle - halfSpread;
                float offset = t * __instance.EmitterSize - halfEmitter;

                Vector3 rayDir = Quaternion.Euler(0f, 0f, angle) * dir;
                Vector2 origin = pos + Vector2.Perpendicular(dir) * offset;

                int hitCount = Physics2D.RaycastNonAlloc(origin, rayDir, _buffer, __instance.Range, maskV);

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

        private void Awake() { phys = GetComponent<PhysicalBehaviour>(); }

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
            else if (Tint != Color.white) SetTint(Tint);

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
            foreach (var o in overlays) if (o.overlay != null) Destroy(o.overlay.gameObject);
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
            if (blood != null && GetComponent<LimbBehaviour>() == null) SetCachedComputedColour(blood, color);

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
                if (display != null && display.TextMesh != null) display.TextMesh.color = color;
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

            var defaultMat = ModAPI.FindMaterial("Sprites-Default");
            if (defaultMat != null) overlay.sharedMaterial = defaultMat;

            overlay.color = color;
            overlay.enabled = color.a > 0.01f;

            overlays.Add(new Ovl { overlay = overlay, baseSR = baseSR, lastSprite = baseSR.sprite });
        }

        private void OnDestroy() { foreach (var o in overlays) if (o.overlay != null) Destroy(o.overlay.gameObject); }

        private static void SetCachedComputedColour(BloodContainer bc, Color color)
        {
            if (CachedColourField != null) CachedColourField.SetValue(bc, color);
        }
    }

    // ================= RPM DISPLAY =================
    public class RpmDisplay : MonoBehaviour
    {
        public TextMeshPro display;
        public Rigidbody2D rb;
        private GameObject holder;
        private PhysicalBehaviour _pb;
        public bool IsActive = true;

        private void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null) return;
            _pb = GetComponent<PhysicalBehaviour>();

            var holo = ModAPI.FindSpawnable("Holographic Display");
            if (holo != null && holo.Prefab != null)
            {
                var textT = holo.Prefab.transform.Find("Text");
                if (textT != null)
                {
                    holder = new GameObject("RpmHolder");
                    var cloned = Instantiate(textT.gameObject, holder.transform);
                    cloned.transform.localPosition = Vector3.zero;
                    display = cloned.GetComponent<TextMeshPro>();
                }
            }

            UpdateButtons();
            if (holder != null) holder.SetActive(IsActive);
        }

        public void UpdateButtons()
        {
            if (_pb == null || _pb.ContextMenuOptions == null) return;
            var buttons = _pb.ContextMenuOptions.Buttons;
            
            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "AddRpmDisplay" || buttons[i].Identity == "RemoveRpmDisplay") 
                    buttons.RemoveAt(i);

            if (IsActive)
            {
                buttons.Add(new ContextMenuButton(
                    "RemoveRpmDisplay", "Remove RPM Display", "Remove RPM Display",
                    new UnityAction[] { delegate () { 
                        IsActive = false; 
                        if (holder != null) holder.SetActive(false); 
                        UpdateButtons(); 
                    }}));
            }
            else
            {
                buttons.Add(new ContextMenuButton(
                    "AddRpmDisplay", "Add RPM Display", "Shows RPM and angular velocity",
                    new UnityAction[] { delegate () { 
                        IsActive = true; 
                        if (holder != null) holder.SetActive(true); 
                        UpdateButtons(); 
                    }}));
            }
        }

        private void Update()
        {
            if (IsActive && display != null && rb != null && holder != null)
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
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class RpmButtonPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            
            if (__instance.GetComponent<RpmDisplay>() != null) return;

            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "AddRpmDisplay") return;

            var btn = new ContextMenuButton(
                "AddRpmDisplay", "Add RPM Display", "Shows RPM and angular velocity above the object",
                new UnityAction[] { delegate () {
                    if (__instance.GetComponent<RpmDisplay>() == null) 
                    {
                        var rpm = __instance.gameObject.AddComponent<RpmDisplay>();
                        rpm.IsActive = true;
                    }
                }});
            btn.LabelWhenMultipleAreSelected = "Add RPM Display";
            buttons.Add(btn);
        }
    }

    // ================= CHANGE GRIP =================
    public class WheelGripMarker : MonoBehaviour { public float GripValue = 70f; }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class WheelGripMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            if (!__instance.gameObject.name.ToLower().Contains("wheel")) return;

            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            var marker = __instance.GetComponent<WheelGripMarker>();
            if (marker == null) marker = __instance.gameObject.AddComponent<WheelGripMarker>();

            ApplyGrip(__instance, marker.GripValue);

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "changeGrip") return;

            var btn = new ContextMenuButton(
                "changeGrip", "Change grip", "Set wheel grip. 70 = vanilla, 100 = maximum (no sliding)",
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
                }});
            btn.LabelWhenMultipleAreSelected = "Change grip";
            buttons.Add(btn);
        }

        static void ApplyGrip(PhysicalBehaviour pb, float value)
        {
            if (pb == null) return;
            var colliders = pb.GetComponents<Collider2D>();
            if (colliders == null || colliders.Length == 0) return;

            float friction = value >= 100f ? 100f : (value / 70f) * 2f;
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
    public class RotorPowerMarker : MonoBehaviour { public float Power = 10f; }

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
                "rotorSetPower", "Set rotor power",
                "Set rotor power (1 = weak, 100 = very strong). Default 10.",
                new UnityAction[] { delegate () { OpenPowerDialog(__instance); } }
            ) { LabelWhenMultipleAreSelected = "Set rotor power" };
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

    // ================= ROTOR MODES =================
    public class RotorModeMarker : MonoBehaviour
    {
        public enum Mode { Rotor, Dynamo, Limiter }
        public Mode CurrentMode = Mode.Rotor;
        public bool Enabled = false;
        public bool SuppressSelfToggle = false;

        public bool Reversed = false;
        public float MaxAV = 0f;
        public float MotorForce = 2.5f;        
        public float MotorEfficiency = 0.25f;   

        public float ThresholdAV = 500f;
        public bool DoubleTrigger = false;
        public bool SignalSent = false;
        public float OriginalDelay = -1f;

        private void OnEnable()
        {
            // Обновляем кнопки при загрузке
            var pb = GetComponent<PhysicalBehaviour>();
            if (pb != null)
            {
                RotorModeMenuPatch.UpdateModeButtons(pb, this);
            }
        }
    }

    [HarmonyPatch(typeof(RotorBehaviour), "Start")]
    public static class RotorModeMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(RotorBehaviour __instance)
        {
            if (__instance == null) return;
            var pb = __instance.PhysicalBehaviour;
            if (pb == null || pb.ContextMenuOptions == null) return;

            var buttons = pb.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "rotorMode" ||
                    buttons[i].Identity == "rotorMaxAV" ||
                    buttons[i].Identity == "rotorReverse" ||
                    buttons[i].Identity == "rotorLimiterThreshold" ||
                    buttons[i].Identity == "rotorLimiterTrigger")
                    buttons.RemoveAt(i);

            var marker = __instance.GetComponent<RotorModeMarker>();
            if (marker == null) marker = __instance.gameObject.AddComponent<RotorModeMarker>();

            var btnMode = new ContextMenuButton(
                "rotorMode",
                () =>
                {
                    switch (marker.CurrentMode)
                    {
                        case RotorModeMarker.Mode.Rotor: return "Switch mode: Dynamo";
                        case RotorModeMarker.Mode.Dynamo: return "Switch mode: Limiter";
                        case RotorModeMarker.Mode.Limiter: return "Switch mode: Rotor";
                        default: return "Switch mode";
                    }
                },
                "Cycle rotor mode: Rotor → Dynamo → Limiter. Press F to toggle ON/OFF.",
                new UnityAction[] { delegate () { CycleMode(__instance, marker); } }
            );
            btnMode.LabelWhenMultipleAreSelected = "Switch mode";
            buttons.Add(btnMode);

            UpdateModeButtons(pb, marker);
        }

        static void CycleMode(RotorBehaviour rb, RotorModeMarker marker)
        {
            if (marker.CurrentMode == RotorModeMarker.Mode.Rotor)
                marker.CurrentMode = RotorModeMarker.Mode.Dynamo;
            else if (marker.CurrentMode == RotorModeMarker.Mode.Dynamo)
                marker.CurrentMode = RotorModeMarker.Mode.Limiter;
            else
                marker.CurrentMode = RotorModeMarker.Mode.Rotor;

            var pb = rb.PhysicalBehaviour;
            if (pb != null)
            {
                if (marker.CurrentMode == RotorModeMarker.Mode.Limiter)
                {
                    if (marker.OriginalDelay < 0f)
                        marker.OriginalDelay = pb.ActivationPropagationDelay;
                    pb.ActivationPropagationDelay = 0f;
                }
                else
                {
                    if (marker.OriginalDelay >= 0f)
                        pb.ActivationPropagationDelay = marker.OriginalDelay;
                }
            }

            UpdateModeButtons(pb, marker);
        }

        public static void UpdateModeButtons(PhysicalBehaviour pb, RotorModeMarker marker)
        {
            if (pb == null || pb.ContextMenuOptions == null) return;
            var buttons = pb.ContextMenuOptions.Buttons;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "rotorMaxAV" ||
                    buttons[i].Identity == "rotorReverse" ||
                    buttons[i].Identity == "rotorLimiterThreshold" ||
                    buttons[i].Identity == "rotorLimiterTrigger")
                    buttons.RemoveAt(i);

            if (marker.CurrentMode == RotorModeMarker.Mode.Dynamo)
            {
                var btnMax = new ContextMenuButton(
                    "rotorMaxAV",
                    "Set max AV",
                    "Maximum angular velocity. Negative = reverse direction. 0 = unlimited.",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<RotorModeMarker>(
                            marker.MaxAV, marker,
                            (RotorModeMarker m, float v) =>
                            {
                                if (v < 0) { m.Reversed = true; m.MaxAV = Mathf.Abs(v); }
                                else { m.Reversed = false; m.MaxAV = v; }
                            },
                            "Set max AV", "Max AV (negative = reverse)");
                    }}
                );
                btnMax.LabelWhenMultipleAreSelected = "Set max AV";
                buttons.Add(btnMax);

                var btnRev = new ContextMenuButton(
                    "rotorReverse",
                    "Reverse direction",
                    "Reverse the direction of the dynamo motor",
                    new UnityAction[] { delegate () { marker.Reversed = !marker.Reversed; } }
                );
                btnRev.LabelWhenMultipleAreSelected = "Reverse direction";
                buttons.Add(btnRev);
            }
            else if (marker.CurrentMode == RotorModeMarker.Mode.Limiter)
            {
                var btnTh = new ContextMenuButton(
                    "rotorLimiterThreshold",
                    "Set threshold AV",
                    "Set angular velocity threshold for signal (absolute value)",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<RotorModeMarker>(
                            marker.ThresholdAV, marker,
                            (RotorModeMarker m, float v) => m.ThresholdAV = Mathf.Abs(v),
                            "Set threshold AV", "Threshold (absolute AV)");
                    }}
                );
                btnTh.LabelWhenMultipleAreSelected = "Set threshold AV";
                buttons.Add(btnTh);

                var btnTr = new ContextMenuButton(
                    "rotorLimiterTrigger",
                    () => marker.DoubleTrigger ? "Switch trigger: Single trigger" : "Switch trigger: Double trigger",
                    "Single = one signal on reach. Double = signal on reach and on release.",
                    new UnityAction[] { delegate () { marker.DoubleTrigger = !marker.DoubleTrigger; } }
                );
                btnTr.LabelWhenMultipleAreSelected = "Switch trigger";
                buttons.Add(btnTr);
            }
        }
    }

    [HarmonyPatch(typeof(RotorBehaviour), "FixedUpdate")]
    public static class RotorDynamoPatch
    {
        [HarmonyPrefix]
        static bool Prefix(RotorBehaviour __instance)
        {
            if (__instance == null) return true;
            var marker = __instance.GetComponent<RotorModeMarker>();
            if (marker == null || marker.CurrentMode != RotorModeMarker.Mode.Dynamo) return true;

            var pb = __instance.PhysicalBehaviour;
            if (pb == null || pb.rigidbody == null) return true;
            var hinge = __instance.Hinge;
            if (hinge == null || hinge.connectedBody == null) return true;

            if (!marker.Enabled)
            {
                hinge.useMotor = false;
                return false;
            }

            hinge.useMotor = false;
            var wheelRb = hinge.connectedBody;

            if (pb.Charge <= 0.01f) return false;

            float sign = marker.Reversed ? -1f : 1f;
            float currentAV = wheelRb.angularVelocity;
            float currentAVSigned = currentAV * sign;

            if (marker.MaxAV > 0.01f && currentAVSigned >= marker.MaxAV)
                return false;

            float torque = pb.Charge * marker.MotorForce * marker.MotorEfficiency;

            if (marker.MaxAV > 0.01f)
            {
                float remaining = marker.MaxAV - currentAVSigned;
                if (remaining <= 0f) return false;
                torque = Mathf.Min(torque, remaining * wheelRb.mass * 10f);
            }

            wheelRb.AddTorque(torque * sign);
            return false;
        }
    }

    [HarmonyPatch(typeof(RotorBehaviour), "FixedUpdate")]
    public static class RotorLimiterPatch
    {
        [HarmonyPrefix]
        static bool Prefix(RotorBehaviour __instance)
        {
            if (__instance == null) return true;
            var marker = __instance.GetComponent<RotorModeMarker>();
            if (marker == null || marker.CurrentMode != RotorModeMarker.Mode.Limiter) return true;

            var pb = __instance.PhysicalBehaviour;
            if (pb == null || pb.rigidbody == null) return true;
            var hinge = __instance.Hinge;
            if (hinge == null || hinge.connectedBody == null) return true;

            if (!marker.Enabled)
            {
                hinge.useMotor = false;
                return false;
            }

            hinge.useMotor = false;

            var wheelRb = hinge.connectedBody;
            float absAV = Mathf.Abs(wheelRb.angularVelocity);

            bool reached = absAV >= marker.ThresholdAV;

            if (!marker.SignalSent && reached)
            {
                marker.SignalSent = true;
                marker.SuppressSelfToggle = true;
                pb.ForceSendUse();
                marker.SuppressSelfToggle = false;
            }
            else if (marker.SignalSent && !reached)
            {
                if (marker.DoubleTrigger)
                {
                    marker.SuppressSelfToggle = true;
                    pb.ForceSendUse();
                    marker.SuppressSelfToggle = false;
                }
                marker.SignalSent = false;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(RotorBehaviour), "FixedUpdate")]
    public static class RotorPowerFixedUpdatePatch
    {
        [HarmonyPrefix]
        static bool Prefix(RotorBehaviour __instance)
        {
            if (__instance == null) return true;

            var mode = __instance.GetComponent<RotorModeMarker>();
            if (mode != null && mode.CurrentMode != RotorModeMarker.Mode.Rotor)
                return true;

            if (__instance.Hinge == null) return true;
            if (__instance.PhysicalBehaviour == null) return true;

            var hinge = __instance.Hinge;

            if (mode != null && !mode.Enabled)
            {
                hinge.useMotor = false;
                return false;
            }

            hinge.useMotor = true;

            float powerMul = 1f;
            var marker = __instance.GetComponent<RotorPowerMarker>();
            if (marker != null) powerMul = Mathf.Clamp(marker.Power / 10f, 0.1f, 10f);

            JointMotor2D motor = hinge.motor;
            motor.maxMotorTorque = (100f + __instance.PhysicalBehaviour.Charge * 4f) * powerMul;
            motor.motorSpeed = -__instance.GetScaledSpeed(); 
            hinge.motor = motor;

            return false;
        }
    }

    // ================= ACCUMULATOR =================
    public class CustomAccumulatorBehaviour : MonoBehaviour
    {
        public PhysicalBehaviour phys;
        public float targetEnergy = 1000f;
        public bool isActive = false;
        public bool isBroken = false;

        private DamagableMachineryBehaviour _damagable;

        private void Start()
        {
            phys = GetComponent<PhysicalBehaviour>();
            if (phys == null) return;

            targetEnergy = phys.Charge;

            if (phys.ContextMenuOptions != null)
            {
                phys.ContextMenuOptions.Buttons.Add(new ContextMenuButton(
                    "setAccumulatorEnergy",
                    "Set energy",
                    "Set the target energy for this accumulator. Active accumulator will maintain this level. Max 2000.",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<CustomAccumulatorBehaviour>(
                            targetEnergy, this,
                            (CustomAccumulatorBehaviour b, float v) =>
                            {
                                b.targetEnergy = Mathf.Clamp(v, 0f, 2000f);
                                if (b.isActive && b.phys != null && !b.isBroken)
                                    b.phys.Charge = b.targetEnergy;
                            },
                            "Set energy", "Energy (0 - 2000)");
                    }})
                );
            }

            _damagable = gameObject.GetComponent<DamagableMachineryBehaviour>();
            if (_damagable == null)
            {
                _damagable = gameObject.AddComponent<DamagableMachineryBehaviour>();
            }

            _damagable.TemperatureThreshold = 100f;

            if (_damagable.OnDamaged == null) _damagable.OnDamaged = new UnityEvent();
            if (_damagable.OnRepaired == null) _damagable.OnRepaired = new UnityEvent();

            _damagable.OnDamaged.AddListener(() =>
            {
                isBroken = true;
                isActive = false;
                Plugin.Log.LogInfo("Accumulator broken! Disabling energy output.");
            });

            _damagable.OnRepaired.AddListener(() =>
            {
                isBroken = false;
                Plugin.Log.LogInfo("Accumulator repaired! Ready to be turned on.");
            });

            if (isBroken) isActive = false;
        }

        private void FixedUpdate()
        {
            if (phys == null || isBroken) return;

            if (isActive)
            {
                phys.Charge = targetEnergy;
            }
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Use")]
    public static class AccumulatorTogglePatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null) return;
            var marker = __instance.GetComponent<CustomAccumulatorBehaviour>();
            if (marker != null && !marker.isBroken)
            {
                marker.isActive = !marker.isActive;
                Plugin.Log.LogInfo($"Accumulator toggled: {marker.isActive}");
            }
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class AccumulatorInitPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null) return;

            string objName = __instance.gameObject.name.ToLower();
            
            if (objName.Contains("accumulator") && !objName.Contains("battery"))
            {
                if (__instance.GetComponent<CustomAccumulatorBehaviour>() == null)
                {
                    __instance.gameObject.AddComponent<CustomAccumulatorBehaviour>();
                }
            }
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class RemoveBatteryEditEnergyPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            
            string objName = __instance.gameObject.name.ToLower();
            
            if (objName.Contains("battery"))
            {
                var buttons = __instance.ContextMenuOptions.Buttons;
                if (buttons == null) return;
                
                for (int i = buttons.Count - 1; i >= 0; i--)
                {
                    if (buttons[i].Identity == "setAccumulatorEnergy")
                    {
                        buttons.RemoveAt(i);
                    }
                }
            }
        }
    }

    // ================= SERVO → GENERATOR / BRAKE MODE =================
    public class ServoModeMarker : MonoBehaviour
    {
        public enum ServoMode { Servo, Generator, Brake }
        public ServoMode CurrentMode = ServoMode.Servo;
        public bool Enabled = false;

        public float FrictionMult = 0.1f;
        public float PowerMult = 0.05f;
        public float MinSpeed = 360f;

        public float BrakeMaxCharge = 4.5f; // Ванильный лимит 4.5
        public float BrakeForceMult = 5f;

        private void OnEnable()
        {
            var pb = GetComponent<PhysicalBehaviour>();
            if (pb != null)
            {
                ServoGeneratorMenuPatch.UpdateModeButtons(pb, this);
            }
        }
    }

    [HarmonyPatch(typeof(MotorisedHingeBehaviour), "Start")]
    public static class ServoGeneratorMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(MotorisedHingeBehaviour __instance)
        {
            if (__instance == null) return;
            var pb = __instance.PhysicalBehaviour;
            if (pb == null || pb.ContextMenuOptions == null) return;

            var buttons = pb.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "servoMode" || buttons[i].Identity == "servoBrakeMaxCharge")
                    buttons.RemoveAt(i);

            var marker = __instance.GetComponent<ServoModeMarker>();
            if (marker == null) marker = __instance.gameObject.AddComponent<ServoModeMarker>();

            var btn = new ContextMenuButton(
                "servoMode",
                () =>
                {
                    switch (marker.CurrentMode)
                    {
                        case ServoModeMarker.ServoMode.Servo: return "Switch mode: Generator";
                        case ServoModeMarker.ServoMode.Generator: return "Switch mode: Brake";
                        case ServoModeMarker.ServoMode.Brake: return "Switch mode: Servo";
                        default: return "Switch mode";
                    }
                },
                "Cycle servo mode: Servo → Generator → Brake. Press F to toggle ON/OFF.",
                new UnityAction[] { delegate () { CycleMode(__instance, marker); } }
            );
            btn.LabelWhenMultipleAreSelected = "Switch mode";
            buttons.Add(btn);

            UpdateModeButtons(pb, marker);
        }

        static void CycleMode(MotorisedHingeBehaviour servo, ServoModeMarker marker)
        {
            if (marker.CurrentMode == ServoModeMarker.ServoMode.Servo)
                marker.CurrentMode = ServoModeMarker.ServoMode.Generator;
            else if (marker.CurrentMode == ServoModeMarker.ServoMode.Generator)
                marker.CurrentMode = ServoModeMarker.ServoMode.Brake;
            else
                marker.CurrentMode = ServoModeMarker.ServoMode.Servo;

            UpdateModeButtons(servo.PhysicalBehaviour, marker);
        }

        public static void UpdateModeButtons(PhysicalBehaviour pb, ServoModeMarker marker)
        {
            if (pb == null || pb.ContextMenuOptions == null) return;
            var buttons = pb.ContextMenuOptions.Buttons;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "servoBrakeMaxCharge")
                    buttons.RemoveAt(i);

            if (marker.CurrentMode == ServoModeMarker.ServoMode.Brake)
            {
                var btnCharge = new ContextMenuButton(
                    "servoBrakeMaxCharge",
                    "Set max charge",
                    "Maximum charge required for maximum braking force. Default 4.75.",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<ServoModeMarker>(
                            marker.BrakeMaxCharge, marker,
                            (ServoModeMarker m, float v) => m.BrakeMaxCharge = Mathf.Max(0.1f, v),
                            "Set max charge", "Max charge");
                    }}
                );
                btnCharge.LabelWhenMultipleAreSelected = "Set max charge";
                buttons.Add(btnCharge);
            }
        }
    }

    [HarmonyPatch(typeof(MotorisedHingeBehaviour), "FixedUpdate")]
    public static class ServoGeneratorPatch
    {
        private static readonly Dictionary<MotorisedHingeBehaviour, float> LockedAngles
            = new Dictionary<MotorisedHingeBehaviour, float>();

        [HarmonyPrefix]
        static bool Prefix(MotorisedHingeBehaviour __instance)
        {
            if (__instance == null) return true;
            var marker = __instance.GetComponent<ServoModeMarker>();
            if (marker == null || marker.CurrentMode == ServoModeMarker.ServoMode.Servo) return true;

            var pb = __instance.PhysicalBehaviour;
            if (pb == null) return true;
            var hinge = __instance.Hinge;
            if (hinge == null) return true;

            float angVel = hinge.connectedBody != null ? hinge.connectedBody.angularVelocity : 0f;
            float angVelAbs = Mathf.Abs(angVel);

            if (marker.CurrentMode == ServoModeMarker.ServoMode.Generator)
            {
                hinge.useMotor = false;
                if (!marker.Enabled) return false;
                
                if (angVelAbs > marker.MinSpeed && hinge.connectedBody != null)
                {
                    float chargeVal = (angVelAbs / 360f) * marker.PowerMult * hinge.connectedBody.mass;
                    pb.Charge = Mathf.Min(pb.Charge + chargeVal, 1000f);
                    hinge.connectedBody.AddTorque(-angVel * marker.FrictionMult);
                }
                return false;
            }
            
            else if (marker.CurrentMode == ServoModeMarker.ServoMode.Brake)
            {
                // Если выключен или нет заряда — свободное вращение
                if (!marker.Enabled || pb.Charge <= 0.01f)
                {
                    hinge.useMotor = false;
                    LockedAngles.Remove(__instance);
                    return false;
                }

                float chargeRatio = Mathf.Clamp01(pb.Charge / marker.BrakeMaxCharge);

                // Если заряд >= 100% (или больше)
                if (chargeRatio >= 0.99f)
                {
                    if (!LockedAngles.ContainsKey(__instance))
                    {
                        // Запоминаем текущий угол
                        LockedAngles[__instance] = hinge.jointAngle;
                        Plugin.Log.LogInfo($"Brake locked at angle: {hinge.jointAngle}");
                    }

                    // === ЖЕСТКАЯ ФИКСАЦИЯ УГЛА ===
                    hinge.useMotor = true;
                    
                    JointMotor2D motor = hinge.motor;
                    float angleDiff = Mathf.DeltaAngle(hinge.jointAngle, LockedAngles[__instance]);
                    
                    // Пропорциональное управление для возврата к углу
                    motor.motorSpeed = angleDiff * 10f; 
                    
                    motor.maxMotorTorque = 100000f * Mathf.Abs(angleDiff);
                    if (motor.maxMotorTorque < 5000f) motor.maxMotorTorque = 5000f;

                    hinge.motor = motor;

                    // Гасим скорость
                    if (hinge.connectedBody != null)
                    {
                        hinge.connectedBody.angularVelocity *= 0.1f;
                        if (Mathf.Abs(hinge.connectedBody.angularVelocity) < 2f)
                            hinge.connectedBody.angularVelocity = 0f;
                    }
                }
                else
                {
                    // Если заряд меньше 100% — просто тормозим (вязкое трение)
                    hinge.useMotor = false;
                    LockedAngles.Remove(__instance);

                    if (hinge.connectedBody != null)
                    {
                        float frictionFactor = 1.0f - (chargeRatio * 0.99f);
                        hinge.connectedBody.angularVelocity *= frictionFactor;
                        if (Mathf.Abs(hinge.connectedBody.angularVelocity) < 5f)
                            hinge.connectedBody.angularVelocity = 0f;
                    }
                }

                return false;
            }

            return true;
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

            int mask = LayerMask.GetMask("Objects");
            if (mask == 0) return true;

            var hit = Physics2D.Raycast(origin, dir.normalized, dist, mask);
            if (hit.collider != null && hit.collider != collider) return false;
            return true;
        }
    }
#pragma warning restore 0612

    // ================= ВЕС ОБЪЕКТА =================
    public class WeightMarker : MonoBehaviour { public float Value = -1f; }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class ObjectPropertiesMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;

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

            var btn = new ContextMenuButton(
                "setWeight", "Set Weight", "Set object weight in kilograms (1000 = 1 ton)",
                new UnityAction[] { delegate ()
                {
                    if (__instance.rigidbody == null) return;
                    float currentKg = __instance.rigidbody.mass * 25f;

                    Utils.OpenFloatInputDialog<PhysicalBehaviour>(
                        currentKg, __instance,
                        (PhysicalBehaviour pb, float v) =>
                        {
                            if (pb == null || pb.rigidbody == null) return;
                            float mass = Mathf.Max(0.00001f, v / 25f);
                            pb.rigidbody.mass = mass;
                            pb.TrueInitialMass = mass;
                            pb.InitialMass = mass;
                            var m = pb.GetComponent<WeightMarker>();
                            if (m == null) m = pb.gameObject.AddComponent<WeightMarker>();
                            m.Value = mass;
                        },
                        "Set Weight", "Weight (kg)");
                }});
            btn.LabelWhenMultipleAreSelected = "Set Weight";
            buttons.Add(btn);
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
                if (buttons[i].Identity == "flipObject") return;

            var btn = new ContextMenuButton(
                "flipObject", "Flip", "Mirror this object horizontally",
                new UnityAction[] { delegate () { FlipObject(__instance); } });
            btn.LabelWhenMultipleAreSelected = "Flip";
            buttons.Add(btn);
        }

        static void FlipObject(PhysicalBehaviour pb)
        {
            if (pb == null) return;

            var selection = SelectionController.Main?.SelectedObjects;
            if (selection != null && selection.Count > 1)
            {
                if (selection[0] != pb) return;

                float centerX = 0f;
                foreach (var obj in selection) if (obj != null) centerX += obj.transform.position.x;
                centerX /= selection.Count;

                foreach (var obj in selection)
                {
                    if (obj == null) continue;
                    FlipSingle(obj.transform, centerX);
                }
            }
            else
            {
                FlipSingle(pb.transform, pb.transform.position.x);
            }
        }

        static void FlipSingle(Transform t, float centerX)
        {
            Vector3 pos = t.position;
            pos.x = centerX - (pos.x - centerX);
            t.position = pos;

            Vector3 s = t.localScale;
            s.x *= -1f;
            t.localScale = s;

            Vector3 e = t.eulerAngles;
            float newZ = 180f - e.z;
            newZ = (newZ % 360f + 360f) % 360f;
            t.eulerAngles = new Vector3(e.x, e.y, newZ);
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
            if (f != frameCounter) { frameCounter = f; clipsThisFrame = 0; }
            if (clipsThisFrame >= MaxClipsPerFrame) return false;
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

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "setDetectorRange") buttons.RemoveAt(i);

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "setDetectorRangeCustom") return;

            var btn = new ContextMenuButton(
                "setDetectorRangeCustom", "Set range", "Set detector range from 0.05 to 10 meters",
                new UnityAction[] { delegate ()
                {
                    Utils.OpenFloatInputDialog<DetectorBehaviour>(
                        __instance.Range, __instance,
                        (DetectorBehaviour d, float v) => { d.Range = Mathf.Clamp(v, 0.05f, 10f); },
                        "Set detector range", "Range (0.05 - 10)");
                }});
            btn.LabelWhenMultipleAreSelected = "Set range";
            buttons.Add(btn);
        }
    }

    // ================= AERO COOLING =================
    public class AeroCoolingMarker : MonoBehaviour
    {
        public float CoolingMultiplier = 1f;
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class AeroCoolingInitPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<AeroCoolingMarker>() != null) return;
            __instance.gameObject.AddComponent<AeroCoolingMarker>();
        }
    }

    [HarmonyPatch(typeof(PhysicalBehaviour), "ManagedFixedUpdate")]
    public static class AeroCoolingPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.rigidbody == null) return;
            if (!__instance.SimulateTemperature) return;

            var marker = __instance.GetComponent<AeroCoolingMarker>();
            if (marker == null) return;

            float linearSpeed = Mathf.Clamp(__instance.rigidbody.velocity.magnitude, 0f, 12000f);
            float angularSpeed = Mathf.Clamp(Mathf.Abs(__instance.rigidbody.angularVelocity), 0f, 12000f);

            if (linearSpeed < 0.5f && angularSpeed < 1f) return;

            float ambient = PhysicalBehaviour.AmbientTemperature;
            float diff = __instance.Temperature - ambient;
            if (Mathf.Abs(diff) < 0.1f) return;

            float heatCapacity = __instance.GetHeatCapacity();
            if (heatCapacity <= 0f) heatCapacity = 1f;

            float speedFactor = (linearSpeed + angularSpeed) * 0.00137f;
            float cooling = speedFactor * marker.CoolingMultiplier * Time.fixedDeltaTime / heatCapacity;

            if (diff > 0)
                __instance.Temperature = Mathf.Max(ambient, __instance.Temperature - cooling);
            else
                __instance.Temperature = Mathf.Min(ambient, __instance.Temperature + cooling);
        }
    }

    // ================= POINTER → CAM FOLLOWER =================
    public class PointerCamFollowerBehaviour : MonoBehaviour
    {
        public float TargetAngle = 90f;
        public float Tolerance = 0.97f;
        public bool DoubleTrigger = false;
        public bool Enabled = false;
        public bool SuppressSelfToggle = false;
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
            if (phys != null) { _originalDelay = phys.ActivationPropagationDelay; phys.ActivationPropagationDelay = 0; }

            var pointer = GetComponent<PointerBehaviour>();
            if (pointer != null) Wheel = pointer.RotorWheel;

            var lightT = transform.Find("Active light");
            if (lightT != null) Light = lightT.GetComponent<SpriteRenderer>();

            if (Wheel != null) currentDegree = Wheel.eulerAngles.z + 90f;

            if (phys != null && phys.ContextMenuOptions != null)
            {
                var buttons = phys.ContextMenuOptions.Buttons;
                for (int i = buttons.Count - 1; i >= 0; i--)
                    if (buttons[i].Identity == "setCamAngleCaF" || buttons[i].Identity == "switchTriggerModeCaF")
                        buttons.RemoveAt(i);

                var btnA = new ContextMenuButton(
                    "setCamAngleCaF", "Set angle CaF", "Set the angle at which the signal is triggered",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<PointerCamFollowerBehaviour>(
                            TargetAngle, this,
                            (PointerCamFollowerBehaviour b, float v) => b.TargetAngle = v,
                            "Set angle CaF", "Angle");
                    }});
                btnA.LabelWhenMultipleAreSelected = "Set angle CaF";
                buttons.Add(btnA);

                var btnT = new ContextMenuButton(
                    "switchTriggerModeCaF",
                    () => DoubleTrigger ? "Switch trigger: Single trigger" : "Switch trigger: Double trigger",
                    "Single = one signal on reach. Double = signal on reach and on release.",
                    new UnityAction[] { delegate () { DoubleTrigger = !DoubleTrigger; } });
                btnT.LabelWhenMultipleAreSelected = "Switch trigger";
                buttons.Add(btnT);
            }

            var switcher = GetComponent<PointerModeSwitcher>();
            if (switcher != null) switcher.UpdateButton();
        }

        private void OnDestroy() { if (phys != null) phys.ActivationPropagationDelay = _originalDelay; }

        private void FixedUpdate()
        {
            if (!Enabled) return;
            if (Wheel == null || phys == null) return;

            lastDegree = currentDegree;
            currentDegree = Wheel.eulerAngles.z + 90f;

            float rpm = Mathf.Abs(AVtoRPM(Wheel));
            bool fastSpin = rpm > 80f;

            bool nowReached = fastSpin ? AnglePassed(lastDegree, currentDegree, TargetAngle) : Reached;

            if (!done && nowReached)
            {
                done = true;
                SuppressSelfToggle = true;
                phys.ForceSendUse();
                SuppressSelfToggle = false;
                if (Light != null) Light.enabled = true;
            }
            else if (!nowReached && done)
            {
                if (DoubleTrigger)
                {
                    SuppressSelfToggle = true;
                    phys.ForceSendUse();
                    SuppressSelfToggle = false;
                }
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
            return Mathf.Sign(delta) == Mathf.Sign(deltaTarget) && Mathf.Abs(deltaTarget) <= Mathf.Abs(delta);
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

        private void Start() { if (Pointer == null) Pointer = GetComponent<PointerBehaviour>(); UpdateButton(); }

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
                    if (Pointer != null && Pointer.PhysicalBehaviour != null && Pointer.PhysicalBehaviour.ContextMenuOptions != null)
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

        public void UpdateButton()
        {
            if (Pointer == null || Pointer.PhysicalBehaviour == null || Pointer.PhysicalBehaviour.ContextMenuOptions == null) return;
            var buttons = Pointer.PhysicalBehaviour.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "pointerSwitchMode") buttons.RemoveAt(i);

            var btn = new ContextMenuButton(
                "pointerSwitchMode",
                IsCamFollower ? "Switch mode: Pointer" : "Switch mode: Cam Follower",
                "Switch between Pointer and Cam Follower mode. Press F to toggle ON/OFF.",
                new UnityAction[] { delegate () { Toggle(); } });
            btn.LabelWhenMultipleAreSelected = "Switch mode";
            buttons.Add(btn);
        }
    }

    [HarmonyPatch(typeof(PointerBehaviour), "Start")]
    public static class PointerModePatch
    {
        [HarmonyPostfix]
        static void Postfix(PointerBehaviour __instance) { PointerModeSwitcher.AddTo(__instance.gameObject); }
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
                    if (buttons[i].Identity == "setSpringFrequency" || buttons[i].Identity == "switchSpringDirection")
                        buttons.RemoveAt(i);

                var btnF = new ContextMenuButton(
                    "setSpringFrequency", "Set Spring Power", "0 = free movement, higher = stronger spring",
                    new UnityAction[] { delegate ()
                    {
                        Utils.OpenFloatInputDialog<PistonSpringBehaviour>(
                            SpringFrequency, this,
                            (PistonSpringBehaviour b, float v) =>
                            {
                                b.SpringFrequency = Mathf.Max(0f, v);
                                if (b._spring != null) b._spring.frequency = Mathf.Max(0.01f, b.SpringFrequency);
                                else if (b.SpringFrequency > 0f) b.CreateSpring();
                            },
                            "Set Spring Power", "Frequency");
                    }});
                btnF.LabelWhenMultipleAreSelected = "Set Spring Power";
                buttons.Add(btnF);

                var btnD = new ContextMenuButton(
                    "switchSpringDirection",
                    () => Direction == SpringDirection.Inner ? "Switch spring: Middle" :
                          Direction == SpringDirection.Middle ? "Switch spring: Outer" : "Switch spring: Inner",
                    "Cycle spring direction: Inner, Middle, Outer",
                    new UnityAction[] { delegate () { CycleDirection(); } });
                btnD.LabelWhenMultipleAreSelected = "Switch spring direction";
                buttons.Add(btnD);
            }

            if (SpringFrequency > 0f) CreateSpring();
        }

        private void CycleDirection()
        {
            Direction = Direction == SpringDirection.Inner ? SpringDirection.Middle :
                        Direction == SpringDirection.Middle ? SpringDirection.Outer : SpringDirection.Inner;
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
                case SpringDirection.Inner: _spring.distance = Mathf.Min(minL, maxL); break;
                case SpringDirection.Middle: _spring.distance = (minL + maxL) / 2f; break;
                case SpringDirection.Outer: _spring.distance = Mathf.Max(minL, maxL); break;
            }
        }

        private void OnDestroy() { if (_spring != null) Destroy(_spring); }
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

        private void Start() { if (Piston == null) Piston = GetComponent<PistonBehaviour>(); UpdateButton(); }

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
                    if (Piston != null && Piston.PhysicalBehaviour != null && Piston.PhysicalBehaviour.ContextMenuOptions != null)
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
            if (Piston == null || Piston.PhysicalBehaviour == null || Piston.PhysicalBehaviour.ContextMenuOptions == null) return;
            var buttons = Piston.PhysicalBehaviour.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = buttons.Count - 1; i >= 0; i--)
                if (buttons[i].Identity == "pistonSwitchMode") buttons.RemoveAt(i);

            var btn = new ContextMenuButton(
                "pistonSwitchMode",
                IsSpringMode ? "Switch mode: Piston" : "Switch mode: Spring",
                "Switch between Piston and Spring mode",
                new UnityAction[] { delegate () { Toggle(); } });
            btn.LabelWhenMultipleAreSelected = "Switch mode";
            buttons.Add(btn);
        }
    }

    [HarmonyPatch(typeof(PistonBehaviour), "Start")]
    public static class PistonModePatch
    {
        [HarmonyPostfix]
        static void Postfix(PistonBehaviour __instance) { PistonModeSwitcher.AddTo(__instance.gameObject); }
    }

    // ================= FLIP 90° =================
    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class Flip90MenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;

            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "flip90Object") return;

            var btn = new ContextMenuButton(
                "flip90Object", "Flip 90°", "Rotate this object +90 degrees from its current rotation",
                new UnityAction[] { delegate () { Flip90Object(__instance); } });
            btn.LabelWhenMultipleAreSelected = "Flip 90°";
            buttons.Add(btn);
        }

        static void Flip90Object(PhysicalBehaviour pb)
        {
            if (pb == null) return;

            var selection = SelectionController.Main?.SelectedObjects;
            if (selection != null && selection.Count > 1)
            {
                if (selection[0] != pb) return;
                foreach (var obj in selection) if (obj != null) obj.transform.Rotate(0, 0, 90f);
            }
            else
            {
                pb.transform.Rotate(0, 0, 90f);
            }
        }
    }

    // ================= SET FRICTION =================
    public class FrictionMarker : MonoBehaviour
    {
        public float Value = -1f;
        public PhysicsMaterial2D OriginalMaterial;
        public float OriginalFriction = 0.4f;

        private void Start()
        {
            var pb = GetComponent<PhysicalBehaviour>();
            if (pb != null && Value >= 0)
            {
                // Принудительно применяем сохраненное трение при загрузке
                FrictionMenuPatch.ApplyFriction(pb, this);
            }
        }
    }
    
    [HarmonyPatch(typeof(PhysicalBehaviour), "Start")]
    public static class FrictionMenuPatch
    {
        [HarmonyPostfix]
        static void Postfix(PhysicalBehaviour __instance)
        {
            if (__instance == null || __instance.ContextMenuOptions == null) return;
            if (__instance.gameObject.name.ToLower().Contains("wheel")) return;
    
            var buttons = __instance.ContextMenuOptions.Buttons;
            if (buttons == null) return;
    
            var marker = __instance.GetComponent<FrictionMarker>();
            if (marker == null)
            {
                marker = __instance.gameObject.AddComponent<FrictionMarker>();
                var col = __instance.GetComponent<Collider2D>();
                if (col != null && col.sharedMaterial != null)
                {
                    marker.OriginalMaterial = col.sharedMaterial;
                    marker.OriginalFriction = col.sharedMaterial.friction;
                    marker.Value = marker.OriginalFriction * 100f;
                }
                else
                {
                    marker.OriginalMaterial = null;
                    marker.OriginalFriction = 0.4f;
                    marker.Value = 40f;
                }
            }
    
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Identity == "setFriction") return;
    
            var btn = new ContextMenuButton(
                "setFriction", "Set friction", "0 = maximum sliding, 100 = maximum grip",
                new UnityAction[] { delegate ()
                {
                    var m = __instance.GetComponent<FrictionMarker>();
                    if (m == null) return;
                    Utils.OpenFloatInputDialog<FrictionMarker>(
                        m.Value, m,
                        (FrictionMarker mk, float v) =>
                        {
                            mk.Value = Mathf.Clamp(v, 0f, 100f);
                            ApplyFriction(__instance, mk);
                        },
                        "Set friction", "Friction (0 - 100)");
                }});
            btn.LabelWhenMultipleAreSelected = "Set friction";
            buttons.Add(btn);
        }
    
        public static void ApplyFriction(PhysicalBehaviour pb, FrictionMarker marker)
        {
            if (pb == null) return;
            var colliders = pb.GetComponents<Collider2D>();
            if (colliders == null || colliders.Length == 0) return;
    
            if (Mathf.Abs(marker.Value - marker.OriginalFriction * 100f) < 1f)
            {
                foreach (var col in colliders)
                {
                    col.sharedMaterial = marker.OriginalMaterial;
                    col.enabled = false;
                    col.enabled = true;
                }
                return;
            }
    
            float friction = (marker.Value / 100f) * 5f;
    
            foreach (var col in colliders)
            {
                if (col.sharedMaterial == null || !col.sharedMaterial.name.Contains("Friction_Custom"))
                {
                    var mat = new PhysicsMaterial2D("Friction_Custom_" + pb.GetInstanceID());
                    mat.bounciness = 0f;
                    col.sharedMaterial = mat;
                }
    
                col.sharedMaterial.friction = friction;
                col.enabled = false;
                col.enabled = true;
    
                if (pb.rigidbody != null) pb.rigidbody.WakeUp();
            }
        }
    }
}