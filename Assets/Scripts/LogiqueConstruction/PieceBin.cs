using System.Collections.Generic;
using UnityEngine;

public class PieceBin : MonoBehaviour
{
    public enum VisualState { Normal, Active, Completed }

    [Header("Identification (1 type par bac)")]
    [Tooltip("Identifiant de type de piece (doit matcher WatchAssemblyStep.stepId). Si vide et stepRef != null, on utilise stepRef.stepId.")]
    public string stepId;
    public WatchAssemblyStep stepRef;

    [Header("Renderers a teinter")]
    [Tooltip("Si vide: collecte automatique des MeshRenderer dans la hierarchie du bac.")]
    public MeshRenderer[] targetRenderers;

    [Header("Visuels")]
    public Color activeBlinkColor = new Color(0.2f, 0.55f, 1f, 1f);
    public Color completedColor = new Color(0.2f, 1f, 0.35f, 1f);
    [Tooltip("Frequence du blink actif (Hz).")]
    public float blinkFrequency = 1.5f;
    [Tooltip("Poids min/max du lerp de couleur pendant le blink.")]
    [Range(0f, 1f)] public float blinkLerpMin = 0.25f;
    [Range(0f, 1f)] public float blinkLerpMax = 0.85f;

    readonly List<MaterialPropertyBlock> mpbs = new List<MaterialPropertyBlock>(8);
    readonly List<Color> originalBaseColors = new List<Color>(8);
    int baseColorId;
    int colorId;

    VisualState currentState = VisualState.Normal;
    float blinkTimer;

    void Awake()
    {
        if (string.IsNullOrEmpty(stepId) && stepRef != null)
            stepId = stepRef.stepId;

        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<MeshRenderer>(true);

        baseColorId = Shader.PropertyToID("_BaseColor");
        colorId = Shader.PropertyToID("_Color");

        CaptureOriginals();
        ApplyStateImmediate(currentState);
    }

    void OnValidate()
    {
        if (string.IsNullOrEmpty(stepId) && stepRef != null)
            stepId = stepRef.stepId;
    }

    void CaptureOriginals()
    {
        mpbs.Clear();
        originalBaseColors.Clear();
        if (targetRenderers == null) return;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            var r = targetRenderers[i];
            if (!r) continue;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            // Tente _BaseColor puis _Color; fallback vers sharedMaterial.color
            Color c = Color.white;
            bool ok = false;

            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty(baseColorId))
                {
                    c = r.sharedMaterial.GetColor(baseColorId);
                    ok = true;
                }
                else if (r.sharedMaterial.HasProperty(colorId))
                {
                    c = r.sharedMaterial.GetColor(colorId);
                    ok = true;
                }
            }
            if (!ok)
            {
                // Tentative via MPB actuelle
                if (mpb.isEmpty) c = r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                else
                {
                    if (mpb.GetVector(baseColorId) is Vector4 v1) c = (Color)v1;
                    else if (mpb.GetVector(colorId) is Vector4 v2) c = (Color)v2;
                    else c = r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                }
            }

            mpbs.Add(mpb);
            originalBaseColors.Add(c);
        }
    }

    public string GetStepId() => stepId;

    public void SetState(VisualState state)
    {
        if (currentState == state) return;
        currentState = state;
        blinkTimer = 0f;
        ApplyStateImmediate(state);
    }

    public void Tick(float deltaTime)
    {
        if (currentState != VisualState.Active) return;
        blinkTimer += Mathf.Max(0f, deltaTime);
        float phase = 0.5f * (1f + Mathf.Sin(blinkTimer * blinkFrequency * Mathf.PI * 2f));
        float k = Mathf.Lerp(blinkLerpMin, blinkLerpMax, phase);
        ApplyBlink(k);
    }

    void ApplyStateImmediate(VisualState state)
    {
        switch (state)
        {
            case VisualState.Normal:
                ApplySolid(-1f); // -1 => restaurer
                break;
            case VisualState.Completed:
                ApplySolid(1f, completedColor);
                break;
            case VisualState.Active:
                ApplyBlink(blinkLerpMin);
                break;
        }
    }

    void ApplyBlink(float k)
    {
        if (targetRenderers == null) return;
        for (int i = 0; i < targetRenderers.Length && i < mpbs.Count && i < originalBaseColors.Count; i++)
        {
            var r = targetRenderers[i]; if (!r) continue;
            var mpb = mpbs[i];
            var baseC = originalBaseColors[i];
            var c = Color.Lerp(baseC, activeBlinkColor, Mathf.Clamp01(k));
            // Ecrit sur _BaseColor et _Color pour compat. URP/Standard
            mpb.SetColor(baseColorId, c);
            mpb.SetColor(colorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    void ApplySolid(float k, Color solid = default)
    {
        if (targetRenderers == null) return;
        for (int i = 0; i < targetRenderers.Length && i < mpbs.Count && i < originalBaseColors.Count; i++)
        {
            var r = targetRenderers[i]; if (!r) continue;
            var mpb = mpbs[i];
            Color c = (k < 0f) ? originalBaseColors[i] : solid;
            mpb.SetColor(baseColorId, c);
            mpb.SetColor(colorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}