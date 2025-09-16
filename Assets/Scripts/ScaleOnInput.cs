/*
Project: Cavin-Baudat X Acrotec
File: ScaleOnInput.cs
Summary: Mise a l'echelle continue via une InputAction Vector2 (ex. joystick vertical).
Option: Vitesse parametrable, clamp min/max, gestion du debut/fin de contact pour eviter les sauts.

Author: Nicolas Vial
Company: Cavin-Baudat
Last modified: 20.08.2025

Unity: 2022.3.47f1
*/

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ajuste l'echelle locale en lisant l'axe Y d'une InputAction Vector2.
/// </summary>
public class ScaleOnInput : MonoBehaviour
{
    [Header("Scaling Settings")]
    [SerializeField] private InputActionReference scaleInputAction;
    [SerializeField] private float scaleSpeed = 1f;
    [SerializeField] private Vector3 minScale = Vector3.one * 0.2f;
    [SerializeField] private Vector3 maxScale = Vector3.one * 3f;

    // Propriétés publiques pour d'autres systèmes (TwoHandHandScaler)
    public Vector3 MinScale => minScale;
    public Vector3 MaxScale => maxScale;

    private Vector3 currentScale;
    private Vector2 lastTouchpadValue = Vector2.zero;
    private bool isTouching = false;
    private bool touchStarted = false;

    private void OnEnable()
    {
        if (scaleInputAction && scaleInputAction.action != null)
            scaleInputAction.action.Enable();
        currentScale = transform.localScale;
    }

    private void OnDisable()
    {
        if (scaleInputAction && scaleInputAction.action != null)
            scaleInputAction.action.Disable();
    }

    private void Update()
    {
        HandleScaling();
    }

    private void HandleScaling()
    {
        if (scaleInputAction == null || scaleInputAction.action == null) return;

        Vector2 touchpadValue = scaleInputAction.action.ReadValue<Vector2>();
        bool currentlyTouching = touchpadValue != Vector2.zero;

        // Detection debut/fin de contact
        if (currentlyTouching && !isTouching)
        {
            isTouching = true;
            touchStarted = true;
            lastTouchpadValue = touchpadValue;
            return;
        }
        else if (!currentlyTouching && isTouching)
        {
            isTouching = false;
            touchStarted = false;
            return;
        }

        if (isTouching && !touchStarted)
        {
            float deltaY = touchpadValue.y - lastTouchpadValue.y;

            if (Mathf.Abs(deltaY) > 0.01f)
            {
                float scaleFactor = 1f + deltaY * scaleSpeed * Time.deltaTime;
                currentScale *= scaleFactor;
                currentScale = ClampVector3(currentScale, minScale, maxScale);
                transform.localScale = currentScale;
            }

            lastTouchpadValue = touchpadValue;
        }

        if (touchStarted)
            touchStarted = false;
    }

    private Vector3 ClampVector3(Vector3 value, Vector3 min, Vector3 max) =>
        new Vector3(
            Mathf.Clamp(value.x, min.x, max.x),
            Mathf.Clamp(value.y, min.y, max.y),
            Mathf.Clamp(value.z, min.z, max.z)
        );
}