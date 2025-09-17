using UnityEngine;
using UnityEngine.InputSystem;

public class LoupeManager : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;            // AR main camera (headset)
    public Camera loupeCamera;           // secondary camera that renders the zoom
    public Transform loupeObject;        // grabbable loupe (quad/disc center)
    public RenderTexture loupeRT;        // RT assigned to the loupe material (ARGB32)
    public MeshRenderer loupeRenderer;   // renderer of the quad/disc that shows the RT
    public GameObject loupeRoot;

    [Header("Loupe Camera Settings")]
    public float fovMin = 10f;           // strongest zoom
    public float fovMax = 30f;           // weakest zoom
    public float zoomSpeedDegPerSec = 20f;
    public float camForwardOffset = 0.005f;
    public float loupeNear = 0.01f;
    public float loupeFar = 10f;
    public LayerMask renderMask = ~0;
    public bool clearTransparent = true;

    [Header("Input (Magic Leap trackpad)")]
    public InputActionReference zoomInput; // Vector2, use Y as increment
    public InputActionReference toggleInput;

    private float targetFOV;
    private bool loupeEnabled = true;

    void OnEnable()
    {
        if (zoomInput != null) zoomInput.action.Enable();

        if (toggleInput != null)
        {
            toggleInput.action.Enable();
            toggleInput.action.performed += OnTogglePerformed;
        }
    }

    void OnDisable()
    {
        if (zoomInput != null) zoomInput.action.Disable();

        if (toggleInput != null)
        {
            toggleInput.action.performed -= OnTogglePerformed;
            toggleInput.action.Disable();
        }
    }

    void Start()
    {
        if (!mainCamera) mainCamera = Camera.main;

        if (loupeCamera != null)
        {
            loupeCamera.targetTexture = loupeRT;
            loupeCamera.nearClipPlane = loupeNear;
            loupeCamera.farClipPlane = loupeFar;
            loupeCamera.cullingMask = renderMask;

            if (clearTransparent)
            {
                loupeCamera.clearFlags = CameraClearFlags.SolidColor;
                loupeCamera.backgroundColor = new Color(0, 0, 0, 0);
            }

            targetFOV = Mathf.Clamp(loupeCamera.fieldOfView, fovMin, fovMax);
            loupeCamera.fieldOfView = targetFOV;
        }

        // Initialize on/off state from current root active state (if provided)
        loupeEnabled = loupeRoot ? loupeRoot.activeSelf : loupeEnabled;
        SetLoupeEnabled(loupeEnabled);
    }

    void LateUpdate()
    {
        if (!loupeEnabled) return;

        if (mainCamera == null || loupeCamera == null || loupeObject == null) return;

        // 1) Camera forward = casque -> loupe (regarder "au travers")
        Vector3 fwd = loupeObject.position - mainCamera.transform.position;
        if (fwd.sqrMagnitude < 1e-6f) fwd = mainCamera.transform.forward;
        fwd.Normalize();

        // Up mondial stable (evite le roll quand tu tournes la tete)
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.99f)
            up = mainCamera.transform.up;

        // Pose de la LoupeCam
        loupeCamera.transform.position = loupeObject.position + fwd * camForwardOffset;
        loupeCamera.transform.rotation = Quaternion.LookRotation(fwd, up);

        // Exclure la loupe elle-meme
        int loupeLayer = loupeObject.gameObject.layer;
        loupeCamera.cullingMask = renderMask & ~(1 << loupeLayer);

        // 2) Zoom incremental via trackpad (Y)
        if (zoomInput != null)
        {
            Vector2 pad = zoomInput.action.ReadValue<Vector2>();
            float inc = pad.y; // up=+1, down=-1
            if (Mathf.Abs(inc) > 0.1f)
            {
                targetFOV -= inc * zoomSpeedDegPerSec * Time.deltaTime;
                targetFOV = Mathf.Clamp(targetFOV, fovMin, fovMax);
            }
        }
        loupeCamera.fieldOfView = targetFOV;

        // 3) Compensation d'orientation EN ESPACE VUE (view-space roll)
        // Axe de vue = du quad vers la camera (ce qui definit la rotation visible a l'ecran)
        if (loupeRenderer != null)
        {
            Vector3 viewAxis = (mainCamera.transform.position - loupeObject.position);
            if (viewAxis.sqrMagnitude < 1e-6f) viewAxis = -fwd; // fallback
            viewAxis.Normalize();

            // "Up" de reference = up monde projete sur le plan ecran (perp. a viewAxis)
            Vector3 refUp = Vector3.ProjectOnPlane(Vector3.up, viewAxis).normalized;
            if (refUp.sqrMagnitude < 1e-6f)
            {
                // fallback si up monde presque // a viewAxis
                refUp = Vector3.ProjectOnPlane(mainCamera.transform.up, viewAxis).normalized;
                if (refUp.sqrMagnitude < 1e-6f)
                    refUp = Vector3.Cross(viewAxis, mainCamera.transform.right).normalized;
            }

            // "Up" de l'objet projete pareil
            Vector3 objUp = Vector3.ProjectOnPlane(loupeObject.up, viewAxis).normalized;
            if (objUp.sqrMagnitude < 1e-6f) objUp = refUp;

            // Angle de roll vu a l'ecran (autour de viewAxis)
            float angleDeg = Vector3.SignedAngle(refUp, objUp, viewAxis);
            // Envoyer l'angle au shader (en radians). Le shader applique la rotation inverse.
            loupeRenderer.material.SetFloat("_AngleCorrection", angleDeg * Mathf.Deg2Rad);
        }
    }

    // Toggle handlers
    void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        ToggleLoupe();
    }

    public void ToggleLoupe()
    {
        SetLoupeEnabled(!loupeEnabled);
    }

    public void SetLoupeEnabled(bool enabled)
    {
        loupeEnabled = enabled;

        if (loupeRoot != null)
        {
            loupeRoot.SetActive(enabled);
        }
        else
        {
            // Fallback if no root is provided
            if (loupeRenderer != null) loupeRenderer.enabled = enabled;
            if (loupeCamera != null) loupeCamera.enabled = enabled;
        }
    }
}