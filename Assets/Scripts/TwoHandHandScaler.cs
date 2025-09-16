using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Mise à l'échelle bimanuel: la main droite tient déjà l'objet (via UnifiedPinchGrabXR),
/// la main gauche forme un pinch à proximité pour démarrer un mode de scaling.
/// Supporte les bornes de ScaleOnInput (si présent) + échelle uniforme.
/// </summary>
public class TwoHandHandScaler : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Script unifié de pinch de la main qui tient l'objet (main droite typiquement).")]
    public UnifiedPinchGrabXR unifiedGrab;
    [Tooltip("Pivot override (sinon l'attach du ray interactor, sinon le centre de l'objet).")]
    public Transform pivotOverride;

    [Header("Main secondaire (tracking)")]
    [Tooltip("true = main gauche, false = main droite (si tu inverses).")]
    public bool leftHand = true;
    [Tooltip("Distance pinch (ThumbTip–IndexTip) pour démarrer le scaling.")]
    public float pinchStartThreshold = 0.025f;
    [Tooltip("Distance pinch pour arrêter le scaling.")]
    public float pinchStopThreshold = 0.035f;
    [Tooltip("Rayon de proximité autour de l'objet pour autoriser le démarrage.")]
    public float startProximityRadius = 0.20f;

    [Header("Facteur & limites de fallback")]
    [Tooltip("Facteur min global (si aucun ScaleOnInput).")]
    public float minFactor = 0.2f;
    [Tooltip("Facteur max global (si aucun ScaleOnInput).")]
    public float maxFactor = 4.0f;

    [Header("Utiliser les bornes ScaleOnInput")]
    public bool useScaleOnInputBounds = true;
    [Tooltip("Temps de lissage (s). 0 = instantané.")]
    public float smoothing = 0.08f;

    [Header("Options")]
    [Tooltip("Réactiver ScaleOnInput quand on quitte le mode bimanuel.")]
    public bool autoToggleScaleOnInput = true;
    [Tooltip("Recalcule le pivot / offset à chaque nouveau cycle.")]
    public bool recalcPivotEachStart = true;
    [Tooltip("Exiger que l'objet soit tenu en mode Raycast uniquement (désactive scaling si physique).")]
    public bool onlyAllowWhenRaycastMode = false;

    [Header("Debug")]
    public bool debugDraw;
    public Color debugColor = new(0.1f, 0.8f, 0.3f, 0.6f);

    [Header("Uniformité")]
    [Tooltip("Force une mise à l'échelle strictement uniforme (recommandé).")]
    public bool uniformScale = true;

    // --- internes ---
    XRHandSubsystem _hands;
    bool _scaling;
    float _initialDistance;
    Vector3 _initialScale;
    Vector3 _pivotWorld;
    Vector3 _initialOffset;
    Transform _objTf;
    Rigidbody _objRb;
    ScaleOnInput _scaleOnInput;
    float _targetFactor = 1f;
    float _currentFactor = 1f;

    // Bornes dynamiques issues de ScaleOnInput
    float _dynMinFactor = 0f;
    float _dynMaxFactor = Mathf.Infinity;
    bool _hasDynBounds;

    void OnEnable()
    {
        _hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
    }

    void Update()
    {
        // 1. Validation grab actif
        if (unifiedGrab == null || !unifiedGrab.HasSelection)
        {
            if (_scaling) StopScaling();
            return;
        }

        if (onlyAllowWhenRaycastMode && unifiedGrab.Mode != UnifiedPinchGrabXR.GrabMode.Raycast)
        {
            if (_scaling) StopScaling();
            return;
        }

        var interactable = unifiedGrab.CurrentInteractable as XRBaseInteractable;
        if (interactable == null)
        {
            if (_scaling) StopScaling();
            return;
        }

        // 2. Changement d'objet ?
        if (_objTf == null || _objTf.gameObject != interactable.transform.gameObject)
        {
            if (_scaling) StopScaling();
            _objTf = interactable.transform;
            _objRb = _objTf.GetComponent<Rigidbody>();
            _scaleOnInput = _objTf.GetComponent<ScaleOnInput>();
        }

        // 3. Main secondaire (leftHand)
        if (_hands == null)
        {
            if (_scaling) StopScaling();
            return;
        }
        var hand = leftHand ? _hands.leftHand : _hands.rightHand;
        if (!hand.isTracked)
        {
            if (_scaling) StopScaling();
            return;
        }

        var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
        var index = hand.GetJoint(XRHandJointID.IndexTip);
        if (!thumb.TryGetPose(out var thumbPose) || !index.TryGetPose(out var indexPose))
        {
            if (_scaling) StopScaling();
            return;
        }

        Vector3 pinchMid = (thumbPose.position + indexPose.position) * 0.5f;
        float pinchDist = Vector3.Distance(thumbPose.position, indexPose.position);

        if (!_scaling)
        {
            float prox = Vector3.Distance(pinchMid, _objTf.position);
            if (pinchDist < pinchStartThreshold && prox <= startProximityRadius)
                StartScaling(pinchMid);
        }
        else
        {
            if (pinchDist > pinchStopThreshold)
            {
                StopScaling();
            }
            else
            {
                float dist = Vector3.Distance(_pivotWorld, pinchMid);
                if (_initialDistance > 0.0001f)
                {
                    float rawFactor = dist / _initialDistance;
                    float fMin = _hasDynBounds ? _dynMinFactor : minFactor;
                    float fMax = _hasDynBounds ? _dynMaxFactor : maxFactor;
                    _targetFactor = Mathf.Clamp(rawFactor, fMin, fMax);

                    if (smoothing > 0f)
                    {
                        float a = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.0001f, smoothing));
                        _currentFactor = Mathf.Lerp(_currentFactor, _targetFactor, a);
                    }
                    else _currentFactor = _targetFactor;

                    ApplyScale(_currentFactor, fMin, fMax);
                }
            }
        }

        if (debugDraw && _scaling)
            Debug.DrawLine(_pivotWorld, pinchMid, debugColor);
    }

    void StartScaling(Vector3 pinchMid)
    {
        _scaling = true;
        _initialScale = _objTf.localScale;
        _currentFactor = _targetFactor = 1f;

        // Pivot: override sinon attach du ray interactor sinon objet
        Transform pivot = pivotOverride;
        if (!pivot && unifiedGrab.sharedRayInteractor && unifiedGrab.sharedRayInteractor.attachTransform)
            pivot = unifiedGrab.sharedRayInteractor.attachTransform;
        if (!pivot) pivot = _objTf;
        _pivotWorld = pivot.position;

        if (recalcPivotEachStart || _initialOffset == Vector3.zero)
            _initialOffset = _objTf.position - _pivotWorld;

        _initialDistance = Vector3.Distance(_pivotWorld, pinchMid);
        if (_initialDistance < 0.005f) _initialDistance = 0.005f;

        // Bornes dynamiques (ScaleOnInput)
        _hasDynBounds = false;
        if (useScaleOnInputBounds && _scaleOnInput)
        {
            Vector3 minS = _scaleOnInput.MinScale;
            Vector3 maxS = _scaleOnInput.MaxScale;

            float fMinX = minS.x / Mathf.Max(0.0001f, _initialScale.x);
            float fMinY = minS.y / Mathf.Max(0.0001f, _initialScale.y);
            float fMinZ = minS.z / Mathf.Max(0.0001f, _initialScale.z);
            float fMaxX = maxS.x / Mathf.Max(0.0001f, _initialScale.x);
            float fMaxY = maxS.y / Mathf.Max(0.0001f, _initialScale.y);
            float fMaxZ = maxS.z / Mathf.Max(0.0001f, _initialScale.z);

            _dynMinFactor = Mathf.Max(fMinX, Mathf.Max(fMinY, fMinZ));
            _dynMaxFactor = Mathf.Min(fMaxX, Mathf.Min(fMaxY, fMaxZ));
            if (_dynMinFactor < 0f) _dynMinFactor = 0f;
            if (_dynMaxFactor < _dynMinFactor) _dynMaxFactor = _dynMinFactor + 0.0001f;
            _hasDynBounds = true;
        }

        if (_scaleOnInput && autoToggleScaleOnInput)
            _scaleOnInput.enabled = false;
    }

    void StopScaling()
    {
        _scaling = false;
        if (_scaleOnInput && autoToggleScaleOnInput)
            _scaleOnInput.enabled = true;
    }

    void ApplyScale(float factor, float fMin, float fMax)
    {
        if (uniformScale)
        {
            Vector3 targetScale = _initialScale * factor;

            if (_scaleOnInput)
            {
                Vector3 minS = _scaleOnInput.MinScale;
                Vector3 maxS = _scaleOnInput.MaxScale;
                float facMinFromBounds = Mathf.Max(
                    minS.x / Mathf.Max(0.0001f, _initialScale.x),
                    minS.y / Mathf.Max(0.0001f, _initialScale.y),
                    minS.z / Mathf.Max(0.0001f, _initialScale.z)
                );
                float facMaxFromBounds = Mathf.Min(
                    maxS.x / Mathf.Max(0.0001f, _initialScale.x),
                    maxS.y / Mathf.Max(0.0001f, _initialScale.y),
                    maxS.z / Mathf.Max(0.0001f, _initialScale.z)
                );
                factor = Mathf.Clamp(factor, facMinFromBounds, facMaxFromBounds);
                targetScale = _initialScale * factor;
                _currentFactor = factor;
            }

            Vector3 newPos = _pivotWorld + _initialOffset * factor;

            if (_objRb && !_objRb.isKinematic)
            {
                _objRb.MovePosition(newPos);
                _objTf.localScale = targetScale;
            }
            else
            {
                _objTf.position = newPos;
                _objTf.localScale = targetScale;
            }
            return;
        }

        // Mode non uniforme (rarement utile ici)
        Vector3 targetScaleNU = _initialScale * factor;
        if (_scaleOnInput)
        {
            Vector3 minS = _scaleOnInput.MinScale;
            Vector3 maxS = _scaleOnInput.MaxScale;
            targetScaleNU = new Vector3(
                Mathf.Clamp(targetScaleNU.x, minS.x, maxS.x),
                Mathf.Clamp(targetScaleNU.y, minS.y, maxS.y),
                Mathf.Clamp(targetScaleNU.z, minS.z, maxS.z)
            );
        }
        Vector3 newPos2 = _pivotWorld + _initialOffset * factor;
        if (_objRb && !_objRb.isKinematic)
        {
            _objRb.MovePosition(newPos2);
            _objTf.localScale = targetScaleNU;
        }
        else
        {
            _objTf.position = newPos2;
            _objTf.localScale = targetScaleNU;
        }
    }

    void OnDisable()
    {
        if (_scaling) StopScaling();
    }

    void OnDrawGizmos()
    {
        if (debugDraw && _scaling)
        {
            Gizmos.color = debugColor;
            Gizmos.DrawWireSphere(_pivotWorld, 0.01f);
        }
    }
}