using Unity.Jobs;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;

[DefaultExecutionOrder(110)]
public class UnifiedPinchGrabXR : MonoBehaviour
{
    public enum GrabMode { Raycast, Physical }

    [Header("Mode")]
    public GrabMode initialMode = GrabMode.Raycast;
    public bool allowModeSwitch = true;

    [Header("Références XR")]
    public XRRayInteractor sharedRayInteractor;
    public XRInteractionManager interactionManager;
    public bool rightHand = true;

    [Header("Seuils Raycast")]
    public float rayGrabThreshold = 0.020f;
    public float rayReleaseThreshold = 0.028f;
    public float rayPressDwell = 0.06f;
    public float rayReleaseDwell = 0.10f;
    public float rayTrackingGrace = 0.25f;
    public bool rayKeepDuringShortLoss = true;

    [Header("Seuils Physical")]
    public float physGrabCloseThreshold = 0.020f;
    public float physReleaseOpenThreshold = 0.030f;
    public float physPressDwell = 0.06f;
    public float physReleaseDwell = 0.10f;
    public float physTrackingGrace = 0.25f;

    [Header("Physical - Volume sélection")]
    public LayerMask physGrabbableMask = ~0;
    public float physMaxObjectRadius = 0.15f;
    public float physCapsuleRadius = 0.025f;

    [Header("Raycast - Sélection / spawn")]
    public LayerMask grabbableLayerMask = ~0;
    public bool spawnFromSpawnerTargetOnPinch = true;
    public float spawnDistanceFromInteractor = 0.15f;

    [Header("Raycast - État ligne pendant le hold")]
    public bool overrideRayWhileHolding = true;
    public LayerMask raycastMaskWhileHolding = 0;
    public bool disableUIWhileHolding = true;
    public bool hideLineWhileHolding = false;
    public XRInteractorLineVisual lineVisual;

    [Header("Mode simplifié (distance seule)")]
    public bool useSimplePinchLogic = true;
    public bool simpleUsePalmOrientation = true;
    public float distanceSmoothing = 0.02f;
    public float simpleMinHoldTimeBeforeRelease = 0.12f;
    public float simpleHardOpenMultiplier = 2.0f;
    public float simpleTrackingGrace = 0.3f;

    [Header("Mode simplifié - Suivi physique")]
    public bool simpleAdaptiveSmoothing = true;
    [Range(0f, 30f)] public float simpleFollowFrequency = 14f;
    public float simpleMaxLinearSpeed = 6f;
    public float simpleMaxAngularSpeedDeg = 900f;
    public float simpleLowSpeed = 0.01f;
    public float simpleHighSpeed = 0.2f;

    [Header("Mode simplifié - Robustesse rotation")]
    public float simpleRelativeReleaseMultiplier = 1.75f;
    public float simpleAdditiveReleaseMargin = 0.010f;
    public float simpleRotationSpeedGrace = 130f;
    public float simpleRotationGraceTime = 0.25f;
    public float simpleOrientationFastBlend = 0.3f;
    public float simpleFreezeOrientationSpeed = 0f;
    public float simpleReleaseStableTime = 0.05f;
    public float simpleReleaseDerivativeEpsilon = 0.0015f;
    public float simpleSecondaryDistanceSmoothing = 0.06f;
    public float immediateReleaseOpenDistance = 0.055f;

    [Header("Debug / Watchdog")]
    public bool enableWatchdog = true;
    public bool debugDraw;
    public bool logEvents;
    public bool enableDebugTelemetry = false;
    public float telemetryInterval = 0.5f;
    public Color gizmoCapsuleColor = new(0, 0.7f, 1f, 0.25f);
    public Color gizmoPinchColor = new(0.2f, 0.8f, 1f, 1f);

    public GrabMode Mode { get; private set; }
    public IXRSelectInteractable CurrentInteractable => _current;
    public bool HasSelection => _current != null;

    // Etat machine simplifiée
    enum SimplePinchState { Idle, Holding, Releasing }
    SimplePinchState _simpleState = SimplePinchState.Idle;

    // Distances & timers
    float _smoothedDist = float.PositiveInfinity;
    float _rawDist = float.PositiveInfinity;
    float _simpleStateTimer;
    float _simpleHoldElapsed;
    float _simpleLostTrackingTimer;

    // Sélection / suivi
    IXRSelectInteractable _current;
    Rigidbody _physCurrentRb;
    Vector3 _simpleGripPosFiltered;
    Quaternion _simpleGripRotFiltered;
    bool _simpleFirstFilter = true;

    // Joints / poses main
    XRHandSubsystem _hands;
    Pose _thumbPose, _indexPose, _palmPose;
    bool _haveThumb, _haveIndex, _havePalm;

    // Ray state sauvegarde
    LayerMask _savedMask; bool _savedMaskValid;
    bool _savedUI; bool _savedUIValid;

    // Rotation / release adaptatif
    Quaternion _lastPalmRot; bool _haveLastPalmRot;
    float _rotationGraceTimer;
    float _dynamicBaselineDist;
    float _minDistSinceHold;
    float _releaseStableTimer;
    float _lastEvalDist;
    float _releaseDeriv;
    float _secondarySmoothedDist;

    // Debug
    float _telemetryTimer;

    void OnValidate()
    {
        if (rayReleaseThreshold < rayGrabThreshold) rayReleaseThreshold = rayGrabThreshold + 0.005f;
        if (physReleaseOpenThreshold < physGrabCloseThreshold) physReleaseOpenThreshold = physGrabCloseThreshold + 0.005f;
        if (sharedRayInteractor) sharedRayInteractor.keepSelectedTargetValid = true;
    }

    void Awake()
    {
        if (!interactionManager) interactionManager = FindObjectOfType<XRInteractionManager>();
        if (!sharedRayInteractor) sharedRayInteractor = GetComponentInChildren<XRRayInteractor>(true);
        if (sharedRayInteractor)
        {
            sharedRayInteractor.keepSelectedTargetValid = true;
            if (!lineVisual) lineVisual = sharedRayInteractor.GetComponent<XRInteractorLineVisual>();
        }
        SetMode(initialMode, true);
    }

    void OnEnable()
    {
        _hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
        if (sharedRayInteractor != null)
        {
            sharedRayInteractor.selectEntered.AddListener(OnSelectEntered);
            sharedRayInteractor.selectExited.AddListener(OnSelectExited);
        }
    }

    void OnDisable()
    {
        Release(true);
        RestoreRayState();
        if (sharedRayInteractor != null)
        {
            sharedRayInteractor.selectEntered.RemoveListener(OnSelectEntered);
            sharedRayInteractor.selectExited.RemoveListener(OnSelectExited);
        }
    }

    public void SetMode(GrabMode m, bool force = false)
    {
        if (!allowModeSwitch && !force) return;
        if (Mode == m && !force) return;
        if (logEvents) Debug.Log($"[UnifiedPinchGrabXR] Switch mode -> {m}");
        Release(true);
        Mode = m;
        ResetSimpleState();
    }

    void ResetSimpleState()
    {
        _simpleState = SimplePinchState.Idle;
        _simpleStateTimer = 0f;
        _simpleHoldElapsed = 0f;
        _simpleLostTrackingTimer = 0f;
        _simpleFirstFilter = true;
        _haveLastPalmRot = false;
        _rotationGraceTimer = 0f;
        _dynamicBaselineDist = 0f;
        _minDistSinceHold = float.PositiveInfinity;
        _releaseStableTimer = 0f;
        _lastEvalDist = float.PositiveInfinity;
        _releaseDeriv = 0f;
        _secondarySmoothedDist = float.PositiveInfinity;
    }

    void Update()
    {
        if (_hands == null || interactionManager == null) return;

        var hand = rightHand ? _hands.rightHand : _hands.leftHand;
        bool tracked = hand.isTracked;

        var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
        var index = hand.GetJoint(XRHandJointID.IndexTip);
        _haveThumb = thumb.TryGetPose(out _thumbPose);
        _haveIndex = index.TryGetPose(out _indexPose);

        if (simpleUsePalmOrientation)
        {
            var palm = hand.GetJoint(XRHandJointID.Palm);
            _havePalm = palm.TryGetPose(out _palmPose);
        }
        else _havePalm = false;

        _rawDist = (_haveThumb && _haveIndex)
            ? Vector3.Distance(_thumbPose.position, _indexPose.position)
            : float.PositiveInfinity;

        float dt = Time.unscaledDeltaTime;

        if (distanceSmoothing > 0f)
        {
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, distanceSmoothing));
            _smoothedDist = float.IsInfinity(_smoothedDist) ? _rawDist : Mathf.Lerp(_smoothedDist, _rawDist, a);
        }
        else _smoothedDist = _rawDist;

        if (simpleSecondaryDistanceSmoothing > 0f)
        {
            float a2 = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, simpleSecondaryDistanceSmoothing));
            _secondarySmoothedDist = float.IsInfinity(_secondarySmoothedDist) ? _smoothedDist : Mathf.Lerp(_secondarySmoothedDist, _smoothedDist, a2);
        }
        else _secondarySmoothedDist = _smoothedDist;

        if (useSimplePinchLogic)
            UpdateSimpleLogic(dt, tracked);
    }

    void UpdateSimpleLogic(float dt, bool tracked)
    {
        if (tracked && _haveThumb && _haveIndex) _simpleLostTrackingTimer = 0f;
        else _simpleLostTrackingTimer += dt;

        float grabThreshold = (Mode == GrabMode.Raycast) ? rayGrabThreshold : physGrabCloseThreshold;
        float absoluteReleaseThreshold = (Mode == GrabMode.Raycast) ? rayReleaseThreshold : physReleaseOpenThreshold;
        float pressDwell = (Mode == GrabMode.Raycast) ? rayPressDwell : physPressDwell;
        float releaseDwell = (Mode == GrabMode.Raycast) ? rayReleaseDwell : physReleaseDwell;
        float trackingGrace = (Mode == GrabMode.Raycast) ? rayTrackingGrace : simpleTrackingGrace;

        // Vitesse angulaire paume
        float angularSpeed = 0f;
        if (_havePalm)
        {
            if (_haveLastPalmRot)
            {
                Quaternion delta = _palmPose.rotation * Quaternion.Inverse(_lastPalmRot);
                delta.ToAngleAxis(out float angle, out _);
                if (angle > 180f) angle = 360f - angle;
                angularSpeed = angle / Mathf.Max(dt, 1e-5f);
            }
            _lastPalmRot = _palmPose.rotation;
            _haveLastPalmRot = true;
        }
        else _haveLastPalmRot = false;

        if (angularSpeed > simpleRotationSpeedGrace)
            _rotationGraceTimer = simpleRotationGraceTime;
        else
            _rotationGraceTimer = Mathf.Max(0f, _rotationGraceTimer - dt);

        float evalDist = _secondarySmoothedDist;

        bool pinchClosed = evalDist < grabThreshold;
        bool hardOpen = _rawDist > absoluteReleaseThreshold * simpleHardOpenMultiplier || _rawDist > immediateReleaseOpenDistance;

        switch (_simpleState)
        {
            case SimplePinchState.Idle:
                _simpleStateTimer = pinchClosed && tracked && _haveThumb && _haveIndex ? _simpleStateTimer + dt : 0f;
                if (_simpleStateTimer >= pressDwell)
                {
                    if (TryAcquireSelectionSimple())
                    {
                        _simpleState = SimplePinchState.Holding;
                        _simpleHoldElapsed = 0f;
                        _dynamicBaselineDist = evalDist;
                        _minDistSinceHold = evalDist;
                        if (logEvents) Debug.Log("[SimplePinch] -> Holding (baseline=" + _dynamicBaselineDist.ToString("F3") + ")");
                    }
                    else
                    {
                        _simpleStateTimer = pressDwell * 0.5f;
                    }
                }
                break;

            case SimplePinchState.Holding:
                _simpleHoldElapsed += dt;
                _minDistSinceHold = Mathf.Min(_minDistSinceHold, evalDist);

                // Seuil dynamique
                float dynamicReleaseThreshold = Mathf.Max(
                    absoluteReleaseThreshold,
                    _dynamicBaselineDist * simpleRelativeReleaseMultiplier,
                    _minDistSinceHold + simpleAdditiveReleaseMargin
                );

                // Dérivée pour stabilité
                _releaseDeriv = float.IsInfinity(_lastEvalDist) ? 0f : (evalDist - _lastEvalDist) / Mathf.Max(dt, 1e-5f);
                _lastEvalDist = evalDist;

                bool candidateOpen = evalDist > dynamicReleaseThreshold;

                if (hardOpen)
                {
                    if (logEvents) Debug.Log("[SimplePinch] Hard open release");
                    Release();
                    _simpleState = SimplePinchState.Idle;
                }
                else if (!tracked || !_haveThumb || !_haveIndex)
                {
                    if (_simpleLostTrackingTimer > trackingGrace &&
                        !(rayKeepDuringShortLoss && Mode == GrabMode.Raycast))
                    {
                        if (logEvents) Debug.Log("[SimplePinch] Tracking lost release");
                        Release();
                        _simpleState = SimplePinchState.Idle;
                    }
                }
                else if (candidateOpen && _simpleHoldElapsed >= simpleMinHoldTimeBeforeRelease)
                {
                    // Protection rotation: pas de transition si rotation grace encore active
                    if (_rotationGraceTimer <= 0f)
                    {
                        // Vérifie stabilité (distance ne monte plus trop vite)
                        if (Mathf.Abs(_releaseDeriv) < simpleReleaseDerivativeEpsilon)
                            _releaseStableTimer += dt;
                        else
                            _releaseStableTimer = 0f;

                        if (_releaseStableTimer >= simpleReleaseStableTime)
                        {
                            _simpleState = SimplePinchState.Releasing;
                            _simpleStateTimer = 0f;
                            if (logEvents) Debug.Log($"[SimplePinch] -> Releasing dynThr={dynamicReleaseThreshold:F3} dist={evalDist:F3} deriv={_releaseDeriv:F4}");
                        }
                    }
                    else
                    {
                        // Toujours retenir pendant la grâce rotation
                        _releaseStableTimer = 0f;
                    }
                }
                else
                {
                    // Reset du stable timer si on revient sous seuil
                    _releaseStableTimer = 0f;
                }
                break;

            case SimplePinchState.Releasing:
                _simpleStateTimer += dt;
                if (pinchClosed)
                {
                    _simpleState = SimplePinchState.Holding;
                    _releaseStableTimer = 0f;
                    if (logEvents) Debug.Log("[SimplePinch] Release annulé -> Holding");
                }
                else if (_simpleStateTimer >= releaseDwell)
                {
                    if (logEvents) Debug.Log("[SimplePinch] Release confirmée");
                    Release();
                    _simpleState = SimplePinchState.Idle;
                }
                break;
        }

        // Suivi physique si on tient quelque chose
        if (_current != null && Mode == GrabMode.Physical && _simpleState == SimplePinchState.Holding)
            UpdateSimplePhysicalFollow(dt, angularSpeed);

        // Watchdog sélection XR (optionnel)
        if (enableWatchdog && _current != null && sharedRayInteractor && !sharedRayInteractor.hasSelection)
        {
            if (logEvents) Debug.Log("[SimplePinch] Watchdog lost selection -> release");
            Release();
            _simpleState = SimplePinchState.Idle;
        }

        if (enableDebugTelemetry)
        {
            _telemetryTimer += dt;
            if (_telemetryTimer >= telemetryInterval)
            {
                _telemetryTimer = 0f;
                Debug.Log($"[SimplePinch] State={_simpleState} raw={_rawDist:F3} eval={evalDist:F3} angSpd={angularSpeed:F1} grace={_rotationGraceTimer:F2}");
            }
        }
    }

    bool TryAcquireSelectionSimple()
    {
        if (_current != null) return true;
        return (Mode == GrabMode.Raycast) ? TrySelectRaySimple() : TrySelectPhysicalSimple();
    }

    bool TrySelectRaySimple()
    {
        if (sharedRayInteractor == null || interactionManager == null) return false;
        if (sharedRayInteractor.hasSelection) return true;
        if (!sharedRayInteractor.TryGetCurrent3DRaycastHit(out var hit)) return false;

        var spawner = hit.collider ? hit.collider.GetComponentInParent<SpawnerTarget>() : null;
        if (spawner && spawnFromSpawnerTargetOnPinch && spawner.prefab)
        {
            Transform baseTf =
                sharedRayInteractor.attachTransform ? sharedRayInteractor.attachTransform :
                (sharedRayInteractor.rayOriginTransform ? sharedRayInteractor.rayOriginTransform : sharedRayInteractor.transform);

            Vector3 pos = baseTf.position + baseTf.forward * Mathf.Max(0f, spawnDistanceFromInteractor);
            Quaternion rot = baseTf.rotation;

            GameObject go = Instantiate(spawner.prefab, pos, rot);
            var grab = go.GetComponent<XRGrabInteractable>() ?? go.AddComponent<DynamicGrabInteractable>();
            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = false;

            interactionManager.SelectEnter(sharedRayInteractor, grab);
            _current = grab;
            ApplyRayStateWhileHolding();
            if (logEvents) Debug.Log("[SimplePinch] Ray spawn + select");
            return true;
        }

        if ((grabbableLayerMask.value & (1 << hit.collider.gameObject.layer)) == 0) return false;

        var target = hit.collider.GetComponentInParent<XRGrabInteractable>();
        if (!target) return false;

        interactionManager.SelectEnter(sharedRayInteractor, target);
        _current = target;
        ApplyRayStateWhileHolding();
        if (logEvents) Debug.Log("[SimplePinch] Ray select");
        return true;
    }

    bool TrySelectPhysicalSimple()
    {
        if (!_haveThumb || !_haveIndex) return false;
        if (sharedRayInteractor && sharedRayInteractor.hasSelection) return false;

        Vector3 p0 = _thumbPose.position;
        Vector3 p1 = _indexPose.position;
        int count = Physics.OverlapCapsuleNonAlloc(p0, p1, physCapsuleRadius, _physOverlapBuffer, physGrabbableMask, QueryTriggerInteraction.Ignore);
        if (count == 0) return false;

        XRGrabInteractable best = null;
        float bestScore = float.PositiveInfinity;
        Vector3 mid = (p0 + p1) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var col = _physOverlapBuffer[i];
            if (!col) continue;
            var grab = col.GetComponentInParent<XRGrabInteractable>();
            if (!grab) continue;
            if (col.bounds.extents.magnitude > physMaxObjectRadius) continue;
            float s = Vector3.SqrMagnitude(grab.transform.position - mid);
            if (s < bestScore) { bestScore = s; best = grab; }
        }
        if (!best) return false;

        IXRSelectInteractor inter = sharedRayInteractor ? (IXRSelectInteractor)sharedRayInteractor : null;
        interactionManager.SelectEnter(inter, best);
        _current = best;
        _physCurrentRb = best.GetComponent<Rigidbody>();
        _simpleFirstFilter = true;
        if (logEvents) Debug.Log("[SimplePinch] Physical acquire");
        return true;
    }

    // Buffer réutilisé (déplacé ici pour compacité)
    readonly Collider[] _physOverlapBuffer = new Collider[16];

    void UpdateSimplePhysicalFollow(float dt, float angularSpeed)
    {
        if (_physCurrentRb == null) return;

        Vector3 mid = (_thumbPose.position + _indexPose.position) * 0.5f;
        Quaternion targetRot;

        if (_havePalm && simpleUsePalmOrientation)
        {
            Quaternion palmRot = _palmPose.rotation;
            if (simpleFreezeOrientationSpeed > 0f && angularSpeed > simpleFreezeOrientationSpeed)
                targetRot = _physCurrentRb.rotation;
            else if (angularSpeed > simpleRotationSpeedGrace)
                targetRot = Quaternion.Slerp(palmRot, _physCurrentRb.rotation, simpleOrientationFastBlend);
            else
                targetRot = palmRot;
        }
        else
        {
            Vector3 thumbToIndex = (_indexPose.position - _thumbPose.position);
            Vector3 forward = thumbToIndex.sqrMagnitude > 1e-6f ? thumbToIndex.normalized : _physCurrentRb.transform.forward;
            targetRot = Quaternion.LookRotation(forward, Vector3.up);
        }

        float alpha = 1f - Mathf.Exp(-dt * simpleFollowFrequency);

        if (simpleAdaptiveSmoothing)
        {
            float v = (_physCurrentRb.position - _simpleGripPosFiltered).magnitude / Mathf.Max(dt, 1e-5f);
            float t = Mathf.InverseLerp(simpleLowSpeed, simpleHighSpeed, v);
            alpha *= Mathf.Lerp(0.2f, 1f, t);
        }

        if (_simpleFirstFilter)
        {
            _simpleGripPosFiltered = mid;
            _simpleGripRotFiltered = targetRot;
            _simpleFirstFilter = false;
        }
        else
        {
            _simpleGripPosFiltered = Vector3.Lerp(_simpleGripPosFiltered, mid, alpha);
            _simpleGripRotFiltered = Quaternion.Slerp(_simpleGripRotFiltered, targetRot, alpha);
        }

        Vector3 toTarget = _simpleGripPosFiltered - _physCurrentRb.position;
        Vector3 step = Vector3.ClampMagnitude(toTarget, simpleMaxLinearSpeed * dt);
        _physCurrentRb.MovePosition(_physCurrentRb.position + step);

        Quaternion delta = _simpleGripRotFiltered * Quaternion.Inverse(_physCurrentRb.rotation);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f) angleDeg -= 360f;
        float maxAngStep = simpleMaxAngularSpeedDeg * dt;
        float clamped = Mathf.Clamp(angleDeg, -maxAngStep, maxAngStep);
        if (Mathf.Abs(clamped) > 0.01f && axis != Vector3.zero)
            _physCurrentRb.MoveRotation(Quaternion.AngleAxis(clamped, axis) * _physCurrentRb.rotation);
    }

    public void Release(bool immediate = false)
    {
        if (_current != null && interactionManager != null)
        {
            IXRSelectInteractor inter = sharedRayInteractor ? (IXRSelectInteractor)sharedRayInteractor : null;
            interactionManager.SelectExit(inter, _current);
        }
        _current = null;
        _physCurrentRb = null;
        ResetSimpleState();
        if (Mode == GrabMode.Raycast)
            RestoreRayState();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (args.interactorObject == (IXRSelectInteractor)sharedRayInteractor)
        {
            _current = args.interactableObject;
            if (useSimplePinchLogic)
            {
                float grabRef = (Mode == GrabMode.Raycast) ? rayGrabThreshold : physGrabCloseThreshold;
                bool pinchClosed = _smoothedDist < grabRef * 1.1f;
                _simpleState = pinchClosed ? SimplePinchState.Holding : SimplePinchState.Idle;
                if (pinchClosed && logEvents) Debug.Log("[SimplePinch] Adoption sélection externe -> Holding");
            }
        }
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactorObject == (IXRSelectInteractor)sharedRayInteractor)
        {
            _current = null;
            if (Mode == GrabMode.Raycast) RestoreRayState();
            ResetSimpleState();
        }
    }

    public void ForceRelease() => Release();

    void ApplyRayStateWhileHolding()
    {
        if (!sharedRayInteractor) return;
        if (overrideRayWhileHolding)
        {
            _savedMask = sharedRayInteractor.raycastMask; _savedMaskValid = true;
            sharedRayInteractor.raycastMask = raycastMaskWhileHolding;
        }
        if (disableUIWhileHolding)
        {
            _savedUI = sharedRayInteractor.enableUIInteraction; _savedUIValid = true;
            sharedRayInteractor.enableUIInteraction = false;
        }
        if (hideLineWhileHolding && lineVisual) lineVisual.enabled = false;
    }

    void RestoreRayState()
    {
        if (!sharedRayInteractor) return;
        if (_savedMaskValid) { sharedRayInteractor.raycastMask = _savedMask; _savedMaskValid = false; }
        if (_savedUIValid) { sharedRayInteractor.enableUIInteraction = _savedUI; _savedUIValid = false; }
        if (lineVisual && hideLineWhileHolding) lineVisual.enabled = true;
    }

    void OnDrawGizmos()
    {
        if (!debugDraw) return;
        if (_haveThumb)
        {
            Gizmos.color = gizmoPinchColor;
            Gizmos.DrawWireSphere(_thumbPose.position, 0.006f);
        }
        if (_haveIndex)
        {
            Gizmos.color = gizmoPinchColor;
            Gizmos.DrawWireSphere(_indexPose.position, 0.006f);
        }
        if (_haveThumb && _haveIndex)
        {
            Gizmos.DrawLine(_thumbPose.position, _indexPose.position);
            if (Mode == GrabMode.Physical)
            {
                Gizmos.color = gizmoCapsuleColor;
                Gizmos.DrawWireSphere(_thumbPose.position, physCapsuleRadius);
                Gizmos.DrawWireSphere(_indexPose.position, physCapsuleRadius);
            }
        }
    }
}