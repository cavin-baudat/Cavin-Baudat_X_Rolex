/*
Project: Cavin-Baudat X Acrotec
File: DynamicGrabInteractable.cs
Summary: XRGrabInteractable avec attach dynamique au point d'impact, et support d'un premier grab decale (FirstGrabOffsetFromInteractor).
Option: Cache la ligne du ray pendant le grab; detruit l'attach dynamique au release.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// - Si FirstGrabOffsetFromInteractor est present et non consomme: place l'objet devant l'attach de l'interactor puis consomme.
/// - Sinon: cree un attach dynamique au point d'impact et selectionne normalement.
/// </summary>
public class DynamicGrabInteractable : XRGrabInteractable
{
    private Transform originalAttachTransform;

    protected override void Awake()
    {
        base.Awake();
        originalAttachTransform = attachTransform;
    }

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        var firstGrab = GetComponent<FirstGrabOffsetFromInteractor>();
        if (firstGrab != null && !firstGrab.consumed)
        {
            Transform interTf = (args.interactorObject as Component)?.transform;
            Transform attachTf = interTf;

            var xrBase = args.interactorObject as XRBaseInteractor;
            if (xrBase != null && xrBase.attachTransform != null)
                attachTf = xrBase.attachTransform;

            if (attachTf != null)
            {
                Vector3 targetPos = attachTf.position + attachTf.forward * firstGrab.distance;
                Quaternion targetRot = attachTf.rotation;

                if (TryGetComponent<Rigidbody>(out var rb) && rb != null)
                {
                    if (rb.isKinematic)
                    {
                        rb.position = targetPos;
                        rb.rotation = targetRot;
                    }
                    else
                    {
                        rb.MovePosition(targetPos);
                        rb.MoveRotation(targetRot);
                    }
                }
                else
                {
                    transform.position = targetPos;
                    transform.rotation = targetRot;
                }

                var rayA = args.interactorObject as XRRayInteractor;
                if (rayA != null) ToggleRayLine(rayA, false);

                firstGrab.consumed = true;

                base.OnSelectEntering(args);
                return;
            }
        }

        // Comportement par defaut: attach dynamique au point d'impact
        var rayInteractor = args.interactorObject as XRRayInteractor;
        if (rayInteractor != null)
        {
            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                var newAttach = new GameObject("Dynamic Attach Point");
                newAttach.transform.position = hit.point;
                newAttach.transform.rotation = args.interactorObject.transform.rotation;
                attachTransform = newAttach.transform;
            }
            ToggleRayLine(rayInteractor, false);
        }

        base.OnSelectEntering(args);
    }

    protected override void OnSelectExiting(SelectExitEventArgs args)
    {
        base.OnSelectExiting(args);

        if (attachTransform != null && attachTransform != originalAttachTransform)
            Destroy(attachTransform.gameObject);

        attachTransform = originalAttachTransform;

        var rayInteractor = args.interactorObject as XRRayInteractor;
        if (rayInteractor != null) ToggleRayLine(rayInteractor, true);
    }

    private void ToggleRayLine(XRRayInteractor rayInteractor, bool isVisible)
    {
        if (rayInteractor == null) return;

        var lineVisual = rayInteractor.GetComponent<XRInteractorLineVisual>();
        if (lineVisual != null) lineVisual.enabled = isVisible;

        var lineRenderer = rayInteractor.GetComponent<LineRenderer>();
        if (lineRenderer != null) lineRenderer.enabled = isVisible;
    }
}