/*
Project: Cavin-Baudat X Acrotec
File: SpawnAndGrab.cs
Summary: Spawne un prefab choisi (via SpawnerTarget vis�) puis force sa s�lection � une distance fixe du contr�leur (grab � distance stable).
Author: Nicolas Vial
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class SpawnAndGrab : MonoBehaviour
{
    public enum SpawnReference
    {
        ScriptTransform,         // this.transform (racine contr�leur)
        RayInteractorTransform,  // rayInteractor.transform
        RayOriginTransform,      // rayInteractor.rayOriginTransform
        AttachTransform          // ATTENTION: souvent coll� au hit
    }

    [Header("XR")]
    [SerializeField] private XRRayInteractor rayInteractor;
    [SerializeField] private XRInteractionManager interactionManager;

    [Header("Input")]
    [SerializeField] private List<InputActionReference> spawnAction;

    [Header("Spawn logique")]
    [Tooltip("Distance en m�tres devant la r�f�rence choisie.")]
    public float spawnDistanceFromController = 0.15f;

    [Tooltip("R�f�rence utilis�e pour calculer la position de spawn.")]
    public SpawnReference spawnReference = SpawnReference.RayOriginTransform;

    [Tooltip("Transform explicite (optionnel) prioritaire pour l'origine.")]
    public Transform spawnOriginOverride;

    [Tooltip("Forcer la rotation � celle du contr�leur (ScriptTransform) m�me si autre base.")]
    public bool forceControllerRotation = true;

    [Tooltip("D�tecter et �viter un attach coll� au hit (cursor).")]
    public bool enableCursorFallback = true;

    [Header("Auto-fallback anti-cursor")]
    [Tooltip("Si ref choisie trop proche du hit (< seuil), on retombe sur un transform stable.")]
    public float distanceFromHitFallback = 0.05f;
    public bool fallbackToRayOriginFirst = true;

    [Header("Attach handling")]
    [Tooltip("Cr�er un attachTransform si absent.")]
    public bool autoCreateAttachTransform = true;
    public string autoAttachName = "DynamicRayAttach";

    [Header("Anti-rebond input")]
    [SerializeField] private float pressCooldown = 0.1f;

    [Header("Debug")]
    public bool logVerbose = false;

    bool _cooldown;
    Transform _cachedAttach;
    GameObject _lastSpawned;

    void Reset()
    {
        if (!rayInteractor) rayInteractor = GetComponent<XRRayInteractor>();
        if (!interactionManager) interactionManager = FindObjectOfType<XRInteractionManager>();
    }

    void OnEnable()
    {
        foreach (var a in spawnAction)
            if (a != null && a.action != null) a.action.Enable();
    }

    void OnDisable()
    {
        foreach (var a in spawnAction)
            if (a != null && a.action != null) a.action.Disable();
    }

    void Update()
    {
        if (_cooldown || !rayInteractor || !interactionManager) return;

        for (int i = 0; i < spawnAction.Count; i++)
        {
            var a = spawnAction[i];
            if (a?.action == null) continue;
            if (a.action.WasPressedThisFrame())
            {
                TrySpawnAndSelect();
                if (pressCooldown > 0f) StartCoroutine(Cooldown());
                break;
            }
        }
    }

    System.Collections.IEnumerator Cooldown()
    {
        _cooldown = true;
        yield return new WaitForSeconds(pressCooldown);
        _cooldown = false;
    }

    Transform ResolveBaseReference()
    {
        if (spawnOriginOverride) return spawnOriginOverride;
        if (!rayInteractor) return transform;

        switch (spawnReference)
        {
            case SpawnReference.AttachTransform:
                if (rayInteractor.attachTransform) return rayInteractor.attachTransform;
                break;
            case SpawnReference.RayOriginTransform:
                if (rayInteractor.rayOriginTransform) return rayInteractor.rayOriginTransform;
                break;
            case SpawnReference.RayInteractorTransform:
                return rayInteractor.transform;
            case SpawnReference.ScriptTransform:
            default:
                return transform;
        }
        return transform;
    }

    Transform ResolveStableFallback()
    {
        if (!rayInteractor) return transform;
        if (fallbackToRayOriginFirst)
        {
            if (rayInteractor.rayOriginTransform) return rayInteractor.rayOriginTransform;
            if (rayInteractor.transform) return rayInteractor.transform;
        }
        else
        {
            if (rayInteractor.transform) return rayInteractor.transform;
            if (rayInteractor.rayOriginTransform) return rayInteractor.rayOriginTransform;
        }
        return transform;
    }

    void EnsureAttach()
    {
        if (!rayInteractor) return;
        if (!rayInteractor.attachTransform && autoCreateAttachTransform)
        {
            var go = new GameObject(autoAttachName);
            go.transform.SetParent(rayInteractor.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            rayInteractor.attachTransform = go.transform;
        }
        _cachedAttach = rayInteractor.attachTransform;
    }

    void TrySpawnAndSelect()
    {
        if (!rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            return;

        var target = hit.collider ? hit.collider.GetComponentInParent<SpawnerTarget>() : null;
        if (!target || !target.prefab) return;

        Transform chosen = ResolveBaseReference();
        bool cursorLike = false;

        if (enableCursorFallback && chosen)
        {
            float distToHit = Vector3.Distance(chosen.position, hit.point);
            if (distToHit < distanceFromHitFallback)
            {
                cursorLike = true;
                chosen = ResolveStableFallback();
            }
        }

        Vector3 desiredPos = chosen.position + chosen.forward * Mathf.Max(0f, spawnDistanceFromController);
        Quaternion desiredRot = forceControllerRotation ? transform.rotation : chosen.rotation;

        // Instancie l'objet (avant s�lection)
        GameObject go = Instantiate(target.prefab, desiredPos, desiredRot);
        _lastSpawned = go;

        var grab = go.GetComponent<XRGrabInteractable>();
        if (!grab) grab = go.AddComponent<DynamicGrabInteractable>();

        if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = false;

        // Pr�pare attach temporairement
        EnsureAttach();
        Transform attach = _cachedAttach;

        Vector3 savedAttachPos = Vector3.zero;
        Quaternion savedAttachRot = Quaternion.identity;
        bool restore = false;

        if (attach)
        {
            savedAttachPos = attach.position;
            savedAttachRot = attach.rotation;
            attach.position = desiredPos;
            attach.rotation = desiredRot;
            restore = true;
        }

        // S�lection imm�diate
        if (grab && interactionManager && rayInteractor)
        {
            interactionManager.SelectEnter(rayInteractor as IXRSelectInteractor, grab as IXRSelectInteractable);
        }

        // Restaure attach world pose d'origine (l'objet garde offset relative, donc reste en main)
        if (restore)
        {
            attach.position = savedAttachPos;
            attach.rotation = savedAttachRot;
        }

        if (logVerbose)
        {
            Debug.Log($"[SpawnAndGrab] SpawnRef={spawnReference} cursorLike={cursorLike} pos={desiredPos} rot={desiredRot.eulerAngles} chosen={chosen.name} hit={hit.point}");
        }
    }
}