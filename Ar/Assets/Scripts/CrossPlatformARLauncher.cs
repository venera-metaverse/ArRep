using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class CrossPlatformARLauncher : MonoBehaviour
{
    [Header("Launch")]
    [SerializeField] private Button startArButton;
    [SerializeField] private Camera sceneCameraToDisable;
    [SerializeField] private GameObject[] objectsToDisableOnStart;

    [Header("Placement")]
    [SerializeField] private GameObject placementPrefab;
    [SerializeField] private float fallbackCubeSizeMeters = 0.15f;
    [SerializeField] private bool placeOnFirstDetectedPlane = true;
    [SerializeField] private bool keepUpdatingPositionUntilFound = true;
    [SerializeField] private bool requireHorizontalUpwardPlane = true;
    [SerializeField] private bool allowFeaturePointReticleFallback = true;
    [SerializeField, Range(0.2f, 0.8f)] private float raycastViewportY = 0.38f;
    [SerializeField] private float minimumPlacementDistanceMeters = 0.2f;
    [SerializeField] private float maximumPlacementDistanceMeters = 4f;
    [SerializeField] private int stablePlaneFramesRequired = 6;
    [SerializeField] private float placementRotationYOffset = 180f;
    [SerializeField] private bool keepPlacedObjectFacingCameraOnY = false;

    [Header("Plane Visual")]
    [SerializeField] private bool showDetectedPlanes = true;
    [SerializeField] private bool hideDetectedPlanesAfterPlacement = true;
    [SerializeField] private Color planeFillColor = new Color(0.1f, 0.9f, 0.55f, 0.22f);
    [SerializeField] private Color planeOutlineColor = new Color(0.15f, 1f, 0.65f, 0.95f);
    [SerializeField] private float planeOutlineWidth = 0.018f;

    [Header("Performance")]
    [SerializeField] private bool disableVSync = true;
    [SerializeField] private bool useDisplayRefreshRate = true;
    [SerializeField] private int fallbackTargetFrameRate = 120;

    [Header("Events")]
    [SerializeField] private UnityEvent onArStarted;
    [SerializeField] private UnityEvent onObjectPlaced;
    [SerializeField] private UnityEvent onArUnsupported;

    private readonly List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();

    private GameObject arSessionObject;
    private GameObject xrOriginObject;
    private GameObject arCameraObject;
    private ARSession arSession;
    private XROrigin xrOrigin;
    private ARRaycastManager arRaycastManager;
    private ARPlaneManager arPlaneManager;
    private ARCameraManager arCameraManager;
    private GameObject placedObject;
    private GameObject planeVisualizationPrefab;
    private Material planeFillMaterial;
    private Material planeLineMaterial;
    private bool arStartRequested;
    private bool objectPlacementLocked;
    private bool didInvokeStarted;
    private int stablePlaneHitFrames;

    private GameObject reticleObject;
    private Material reticleMaterial;
    private float reticleAngle;

    private GameObject hintCanvasObject;
    private Text hintText;
    private RawImage centerRingImage;

    private float logTimer;

    private void Awake()
    {
        ApplyPerformanceSettings();
        BuildPlaneVisualizationPrefab();
        BuildARRig();
        BuildReticle();
        Debug.Log($"[AR] Awake. Initial state = {ARSession.state}");
    }

    private void OnEnable()
    {
        if (startArButton != null)
            startArButton.onClick.AddListener(StartAR);
    }

    private void OnDisable()
    {
        if (startArButton != null)
            startArButton.onClick.RemoveListener(StartAR);
    }

    private void Update()
    {
        if (!arStartRequested || arRaycastManager == null)
            return;

        logTimer += Time.deltaTime;
        if (logTimer >= 1f)
        {
            logTimer = 0f;
            Debug.Log($"[AR] State={ARSession.state} planeFrames={stablePlaneHitFrames}");
        }

        if (!didInvokeStarted &&
            (ARSession.state == ARSessionState.SessionInitializing ||
             ARSession.state == ARSessionState.SessionTracking))
        {
            didInvokeStarted = true;
            onArStarted?.Invoke();
            if (hintCanvasObject != null)
                hintCanvasObject.SetActive(true);
        }

        if (ARSession.state == ARSessionState.Unsupported)
        {
            onArUnsupported?.Invoke();
            arStartRequested = false;
            stablePlaneHitFrames = 0;
            SetReticleVisible(false);
            if (hintCanvasObject != null)
                hintCanvasObject.SetActive(false);
            return;
        }

        if (objectPlacementLocked)
        {
            UpdatePlacedObjectFacing();
            SetReticleVisible(false);
            if (hintCanvasObject != null)
                hintCanvasObject.SetActive(false);
            return;
        }

        if (xrOrigin == null || xrOrigin.Camera == null)
            return;

        Vector2 scanPoint = new Vector2(Screen.width * 0.5f, Screen.height * raycastViewportY);
        bool hasPlane = TryGetBestPlaneHit(scanPoint, out Pose planePose);

        if (!hasPlane && allowFeaturePointReticleFallback &&
            arRaycastManager.Raycast(scanPoint, raycastHits, TrackableType.FeaturePoint))
        {
            stablePlaneHitFrames = 0;
            UpdateReticle(raycastHits[0].pose, false);
            SetCenterRingColor(new Color(1f, 0.8f, 0.1f, 0.9f));
            SetHintText("Ищу ровную поверхность...\nНаведите камеру чуть ниже");
            return;
        }

        if (!hasPlane)
        {
            stablePlaneHitFrames = 0;
            SetReticleVisible(false);
            SetCenterRingColor(new Color(1f, 1f, 1f, 0.6f));
            SetHintText("Медленно поводите камерой\nнад полом или столом");
            return;
        }

        UpdateReticle(planePose, true);

        if (stablePlaneHitFrames < Mathf.Max(1, stablePlaneFramesRequired))
        {
            SetCenterRingColor(new Color(1f, 0.85f, 0.2f, 0.95f));
            SetHintText("Фиксирую поверхность...");
            return;
        }

        SetCenterRingColor(new Color(0.2f, 1f, 0.5f, 0.95f));
        SetHintText("Поверхность найдена");
        PlaceOrMoveObject(planePose);
    }

    public void StartAR()
    {
        if (arStartRequested)
            return;

        arStartRequested = true;
        didInvokeStarted = false;
        stablePlaneHitFrames = 0;
        ApplyPerformanceSettings();
        RestorePlaneVisualization();
        StartCoroutine(StartARRoutine());
    }

    public void StopAR()
    {
        arStartRequested = false;
        stablePlaneHitFrames = 0;
        arSessionObject?.SetActive(false);
        xrOriginObject?.SetActive(false);
        SetReticleVisible(false);
        RestorePlaneVisualization();
        if (hintCanvasObject != null)
            hintCanvasObject.SetActive(false);
    }

    public void ResetPlacement()
    {
        objectPlacementLocked = false;
        stablePlaneHitFrames = 0;
        RestorePlaneVisualization();
        if (placedObject != null)
        {
            Destroy(placedObject);
            placedObject = null;
        }
    }

    private IEnumerator StartARRoutine()
    {
#if UNITY_ANDROID
        yield return RequestCameraPermission();
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.LogWarning("[AR] Camera permission denied.");
            arStartRequested = false;
            yield break;
        }
#endif
        DisableSceneObjectsForAR();
        arSessionObject.SetActive(true);
        xrOriginObject.SetActive(true);
        if (hintCanvasObject != null)
            hintCanvasObject.SetActive(true);

        Debug.Log("[AR] Checking availability...");
        yield return ARSession.CheckAvailability();
        Debug.Log($"[AR] Availability state = {ARSession.state}");

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            Debug.Log("[AR] Installing XR support...");
            yield return ARSession.Install();
            Debug.Log($"[AR] State after install = {ARSession.state}");
        }

        if (ARSession.state == ARSessionState.Unsupported)
        {
            Debug.LogError("[AR] Device does not support AR.");
            onArUnsupported?.Invoke();
            arStartRequested = false;
            yield break;
        }

        float elapsed = 0f;
        while (ARSession.state != ARSessionState.SessionInitializing &&
               ARSession.state != ARSessionState.SessionTracking)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= 20f)
            {
                Debug.LogWarning($"[AR] Session did not start in time. Current state = {ARSession.state}");
                break;
            }

            yield return null;
        }

        Debug.Log($"[AR] Runtime session state = {ARSession.state}");
    }

#if UNITY_ANDROID
    private IEnumerator RequestCameraPermission()
    {
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
            yield break;

        bool responded = false;
        PermissionCallbacks callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => responded = true;
        callbacks.PermissionDenied += _ => responded = true;

        Permission.RequestUserPermission(Permission.Camera, callbacks);
        while (!responded)
            yield return null;
    }
#endif

    private void BuildARRig()
    {
        arSessionObject = new GameObject("AR Session");
        arSessionObject.SetActive(false);
        arSession = arSessionObject.AddComponent<ARSession>();
        arSessionObject.AddComponent<ARInputManager>();
        arSession.attemptUpdate = true;
        arSession.matchFrameRateRequested = false;

        xrOriginObject = new GameObject("XR Origin");
        xrOriginObject.SetActive(false);
        xrOrigin = xrOriginObject.AddComponent<XROrigin>();
        arRaycastManager = xrOriginObject.AddComponent<ARRaycastManager>();
        arPlaneManager = xrOriginObject.AddComponent<ARPlaneManager>();
        arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        arPlaneManager.planePrefab = showDetectedPlanes ? planeVisualizationPrefab : null;

        GameObject cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(xrOriginObject.transform, false);

        arCameraObject = new GameObject("AR Camera");
        arCameraObject.transform.SetParent(cameraOffset.transform, false);

        Camera arCamera = arCameraObject.AddComponent<Camera>();
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.black;
        arCamera.nearClipPlane = 0.05f;
        arCamera.farClipPlane = 20f;
        arCamera.tag = "MainCamera";

        UniversalAdditionalCameraData urpData = arCameraObject.GetComponent<UniversalAdditionalCameraData>();
        if (urpData == null)
            urpData = arCameraObject.AddComponent<UniversalAdditionalCameraData>();
        urpData.renderPostProcessing = false;

        arCameraObject.AddComponent<AudioListener>();
        arCameraManager = arCameraObject.AddComponent<ARCameraManager>();
        arCameraManager.autoFocusRequested = true;
        arCameraObject.AddComponent<ARCameraBackground>();

        TrackedPoseDriver trackedPoseDriver = arCameraObject.AddComponent<TrackedPoseDriver>();
        trackedPoseDriver.positionInput = new InputActionProperty(CreatePositionAction());
        trackedPoseDriver.rotationInput = new InputActionProperty(CreateRotationAction());
        trackedPoseDriver.trackingStateInput = new InputActionProperty(CreateTrackingStateAction());

        xrOrigin.CameraFloorOffsetObject = cameraOffset;
        xrOrigin.Camera = arCamera;
    }

    private void BuildReticle()
    {
        reticleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        reticleObject.name = "AR Reticle";
        if (reticleObject.TryGetComponent<Collider>(out Collider collider))
            Destroy(collider);

        reticleObject.transform.localScale = new Vector3(0.25f, 0.002f, 0.25f);
        Renderer reticleRenderer = reticleObject.GetComponent<Renderer>();
        reticleMaterial = CreateUnlitMaterial(new Color(0.15f, 1f, 0.45f), false);
        reticleRenderer.sharedMaterial = reticleMaterial;
        ApplyColor(reticleMaterial, new Color(0.15f, 1f, 0.45f));
        reticleObject.SetActive(false);

        hintCanvasObject = new GameObject("AR Hint Canvas");
        Canvas canvas = hintCanvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = hintCanvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);

        GameObject ringObject = new GameObject("Center Ring");
        ringObject.transform.SetParent(hintCanvasObject.transform, false);
        RectTransform ringRect = ringObject.AddComponent<RectTransform>();
        ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
        ringRect.sizeDelta = new Vector2(110f, 110f);
        ringRect.anchoredPosition = Vector2.zero;
        centerRingImage = ringObject.AddComponent<RawImage>();
        centerRingImage.texture = CreateRingTexture(128, 0.7f);
        centerRingImage.color = new Color(1f, 1f, 1f, 0.7f);

        AddCenteredRect(hintCanvasObject.transform, "Dot", new Vector2(10f, 10f))
            .gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);

        RectTransform bgRect = AddCenteredRect(hintCanvasObject.transform, "Hint BG", Vector2.zero);
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = new Vector2(1f, 0.11f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        bgRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        RectTransform textRect = AddCenteredRect(hintCanvasObject.transform, "Hint Text", Vector2.zero);
        textRect.anchorMin = new Vector2(0.05f, 0f);
        textRect.anchorMax = new Vector2(0.95f, 0.11f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        hintText = textRect.gameObject.AddComponent<Text>();
        hintText.text = "Медленно поводите камерой над горизонтальной поверхностью";
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.fontSize = 44;
        hintText.color = Color.white;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        hintCanvasObject.SetActive(false);
    }

    private void BuildPlaneVisualizationPrefab()
    {
        planeVisualizationPrefab = new GameObject("AR Plane Visual");
        planeVisualizationPrefab.hideFlags = HideFlags.HideAndDontSave;

        ARPlane plane = planeVisualizationPrefab.AddComponent<ARPlane>();
        planeVisualizationPrefab.AddComponent<ARPlaneMeshVisualizer>();

        planeVisualizationPrefab.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeVisualizationPrefab.AddComponent<MeshRenderer>();
        LineRenderer lineRenderer = planeVisualizationPrefab.AddComponent<LineRenderer>();

        planeFillMaterial = CreateUnlitMaterial(planeFillColor, true);
        planeLineMaterial = CreateUnlitMaterial(planeOutlineColor, true);

        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        meshRenderer.sharedMaterial = planeFillMaterial;

        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;
        lineRenderer.widthMultiplier = planeOutlineWidth;
        lineRenderer.positionCount = 0;
        lineRenderer.sharedMaterial = planeLineMaterial;
        lineRenderer.startColor = planeOutlineColor;
        lineRenderer.endColor = planeOutlineColor;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

    }

    private static Material CreateUnlitMaterial(Color color, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        ApplyColor(material, color);

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", transparent ? 1f : 0f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Off);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);

        if (transparent)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return material;
    }

    private bool TryGetBestPlaneHit(Vector2 screenPoint, out Pose pose)
    {
        pose = default;

        bool hitFound = arRaycastManager.Raycast(
            screenPoint,
            raycastHits,
            TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.PlaneWithinInfinity);

        if (!hitFound)
        {
            stablePlaneHitFrames = 0;
            return false;
        }

        for (int i = 0; i < raycastHits.Count; i++)
        {
            ARRaycastHit hit = raycastHits[i];
            if (hit.distance < minimumPlacementDistanceMeters || hit.distance > maximumPlacementDistanceMeters)
                continue;

            ARPlane plane = hit.trackable as ARPlane;
            if (plane == null)
                continue;

            if (requireHorizontalUpwardPlane && plane.alignment != PlaneAlignment.HorizontalUp)
                continue;

            pose = hit.pose;
            stablePlaneHitFrames++;
            return true;
        }

        stablePlaneHitFrames = 0;
        return false;
    }

    private static RectTransform AddCenteredRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rectTransform = go.AddComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = Vector2.zero;
        return rectTransform;
    }

    private static Texture2D CreateRingTexture(int size, float innerFraction)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size * 0.5f;
        float outerRadius = center - 0.5f;
        float innerRadius = center * innerFraction;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                texture.SetPixel(x, y, distance >= innerRadius && distance <= outerRadius ? Color.white : Color.clear);
            }
        }

        texture.Apply();
        return texture;
    }

    private void UpdateReticle(Pose pose, bool isPlane)
    {
        if (reticleObject == null)
            return;

        reticleObject.SetActive(true);
        reticleObject.transform.position = pose.position + Vector3.up * 0.003f;

        reticleAngle = (reticleAngle + 40f * Time.deltaTime) % 360f;
        reticleObject.transform.rotation = Quaternion.Euler(0f, reticleAngle, 0f);

        float scale = isPlane ? 1f : (0.88f + 0.12f * Mathf.Sin(Time.time * 5f));
        reticleObject.transform.localScale = new Vector3(0.25f * scale, 0.002f, 0.25f * scale);

        Color color = isPlane ? new Color(0.15f, 1f, 0.45f) : new Color(1f, 0.75f, 0.1f);
        ApplyColor(reticleMaterial, color);
    }

    private void SetReticleVisible(bool visible)
    {
        if (reticleObject != null)
            reticleObject.SetActive(visible);
    }

    private void SetHintText(string text)
    {
        if (hintText != null)
            hintText.text = text;
    }

    private void SetCenterRingColor(Color color)
    {
        if (centerRingImage != null)
            centerRingImage.color = color;
    }

    private static void ApplyColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private void DisableSceneObjectsForAR()
    {
        if (sceneCameraToDisable == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.gameObject != arCameraObject)
                sceneCameraToDisable = mainCamera;
        }

        if (sceneCameraToDisable != null && sceneCameraToDisable.gameObject != arCameraObject)
            sceneCameraToDisable.gameObject.SetActive(false);

        if (objectsToDisableOnStart == null)
            return;

        foreach (GameObject go in objectsToDisableOnStart)
        {
            if (go != null)
                go.SetActive(false);
        }
    }

    private void PlaceOrMoveObject(Pose hitPose)
    {
        if (placedObject == null)
        {
            placedObject = Instantiate(GetPlacementPrefab());
            placedObject.name = "AR Placement Object";
            onObjectPlaced?.Invoke();
        }

        placedObject.transform.SetPositionAndRotation(hitPose.position, BuildPlacementRotation());

        if (placementPrefab == null)
            placedObject.transform.localScale = Vector3.one * fallbackCubeSizeMeters;

        if (placeOnFirstDetectedPlane || !keepUpdatingPositionUntilFound)
        {
            objectPlacementLocked = true;
            if (hideDetectedPlanesAfterPlacement)
                SetPlaneVisualizationVisible(false);
        }
    }

    private GameObject GetPlacementPrefab()
    {
        if (placementPrefab != null)
            return placementPrefab;

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = Vector3.one * fallbackCubeSizeMeters;
        return cube;
    }

    private Quaternion BuildPlacementRotation()
    {
        if (xrOrigin == null || xrOrigin.Camera == null)
            return Quaternion.identity;

        Vector3 forward = xrOrigin.Camera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            return Quaternion.Euler(0f, placementRotationYOffset, 0f);

        return Quaternion.LookRotation(forward.normalized, Vector3.up) *
               Quaternion.Euler(0f, placementRotationYOffset, 0f);
    }

    private void UpdatePlacedObjectFacing()
    {
        if (!keepPlacedObjectFacingCameraOnY || placedObject == null)
            return;

        placedObject.transform.rotation = BuildPlacementRotation();
    }

    private static InputAction CreatePositionAction()
    {
        InputAction action = new InputAction("AR Camera Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        action.AddBinding("<HandheldARInputDevice>/devicePosition");
        return action;
    }

    private static InputAction CreateRotationAction()
    {
        InputAction action = new InputAction("AR Camera Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        action.AddBinding("<HandheldARInputDevice>/deviceRotation");
        return action;
    }

    private static InputAction CreateTrackingStateAction()
    {
        InputAction action = new InputAction("AR Camera Tracking State", binding: "<XRHMD>/trackingState", expectedControlType: "Integer");
        action.AddBinding("<HandheldARInputDevice>/trackingState");
        return action;
    }

    private void ApplyPerformanceSettings()
    {
        if (disableVSync)
            QualitySettings.vSyncCount = 0;

        OnDemandRendering.renderFrameInterval = 1;

        int targetFrameRate = fallbackTargetFrameRate;
        if (useDisplayRefreshRate)
        {
            float displayRefreshRate = (float)Screen.currentResolution.refreshRateRatio.value;
            if (displayRefreshRate >= 1f)
                targetFrameRate = Mathf.RoundToInt(displayRefreshRate);
        }

        if (targetFrameRate < 60)
            targetFrameRate = 60;

        Application.targetFrameRate = targetFrameRate;
    }

    private void RestorePlaneVisualization()
    {
        if (arPlaneManager == null)
            return;

        arPlaneManager.planePrefab = showDetectedPlanes ? planeVisualizationPrefab : null;
        SetPlaneVisualizationVisible(showDetectedPlanes);
    }

    private void SetPlaneVisualizationVisible(bool visible)
    {
        if (arPlaneManager == null)
            return;

        if (!visible)
            arPlaneManager.planePrefab = null;

        foreach (ARPlane plane in arPlaneManager.trackables)
        {
            ARPlaneMeshVisualizer visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
            if (visualizer != null)
                visualizer.enabled = visible;

            MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.enabled = visible;

            LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
            if (lineRenderer != null)
                lineRenderer.enabled = visible;
        }
    }
}
