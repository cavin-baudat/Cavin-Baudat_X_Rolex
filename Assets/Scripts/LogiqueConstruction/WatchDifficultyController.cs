using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

public class WatchDifficultyController : MonoBehaviour
{
    [Header("Refs")]
    public WatchAssemblyManager manager;
    public WatchAssemblyDifficultyDatabase database;

    [Header("UI Buttons (optional)")]
    public Button easyButton;
    public Button normalButton;
    public Button hardButton;

    [Header("UI Colors")]
    public Color selectedColor = new Color(0.2f, 0.75f, 1f, 1f);
    public Color unselectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    [Range(0f, 1f)] public float highlightedBlend = 0.15f;
    [Range(0f, 1f)] public float pressedDarken = 0.25f;

    [Header("Options")]
    public AssemblyDifficulty initialDifficulty = AssemblyDifficulty.Normal;
    public bool applyOnStart = true;

    [Header("Manager Resolution")]
    [Tooltip("If true, will poll the scene to find a WatchAssemblyManager that spawns later.")]
    public bool autoResolveManager = true;
    [Tooltip("Polling interval in seconds.")]
    public float resolvePollInterval = 0.25f;

    public Action<AssemblyDifficulty> DifficultyChanged;

    AssemblyDifficulty current;
    bool appliedOnce;
    Coroutine resolveCoroutine;

    void Awake()
    {
        if (!manager) manager = FindObjectOfType<WatchAssemblyManager>();
    }

    void OnEnable()
    {
        StartResolveIfNeeded();
    }

    void OnDisable()
    {
        StopResolveIfRunning();
    }

    void Start()
    {
        if (applyOnStart)
        {
            SetDifficulty(initialDifficulty);
        }
        else
        {
            UpdateButtonsVisuals(current);
        }

        // If manager is already there after Start, ensure refresh
        if (manager != null && appliedOnce)
            RefreshManagerAfterDifficultyChange();
    }

    // UI friendly methods
    public void SetDifficultyByIndex(int index)
    {
        var d = (AssemblyDifficulty)Mathf.Clamp(index, 0, 2);
        SetDifficulty(d);
    }

    public void SetDifficultyEasy() { SetDifficulty(AssemblyDifficulty.Easy); }
    public void SetDifficultyNormal() { SetDifficulty(AssemblyDifficulty.Normal); }
    public void SetDifficultyHard() { SetDifficulty(AssemblyDifficulty.Hard); }

    public void CycleDifficulty()
    {
        var next = (int)current + 1;
        if (next > 2) next = 0;
        SetDifficulty((AssemblyDifficulty)next);
    }

    public void SetDifficulty(AssemblyDifficulty difficulty)
    {
        current = difficulty;
        appliedOnce = true;

        if (database != null)
            database.Apply(difficulty);

        UpdateButtonsVisuals(difficulty);
        DifficultyChanged?.Invoke(difficulty);

        RefreshManagerAfterDifficultyChange(); // best-effort; will noop if manager null
    }

    // Optional: allow spawner to push the reference when the prefab is created
    public void SetManager(WatchAssemblyManager newManager)
    {
        manager = newManager;
        if (appliedOnce)
            RefreshManagerAfterDifficultyChange();
    }

    void StartResolveIfNeeded()
    {
        if (!autoResolveManager) return;
        if (resolveCoroutine != null) return;
        resolveCoroutine = StartCoroutine(ResolveManagerLoop());
    }

    void StopResolveIfRunning()
    {
        if (resolveCoroutine != null)
        {
            StopCoroutine(resolveCoroutine);
            resolveCoroutine = null;
        }
    }

    System.Collections.IEnumerator ResolveManagerLoop()
    {
        var wait = new WaitForSeconds(resolvePollInterval);
        while (true)
        {
            if (manager == null)
            {
                manager = FindObjectOfType<WatchAssemblyManager>();
                if (manager != null && appliedOnce)
                {
                    // Manager just appeared; ensure runtime caches/ghost are rebuilt with current difficulty
                    RefreshManagerAfterDifficultyChange();
                }
            }
            else
            {
                // If the referenced manager got destroyed, clear it so we can find the next one
                if (!manager.gameObject) manager = null;
            }
            yield return wait;
        }
    }

    void UpdateButtonsVisuals(AssemblyDifficulty difficulty)
    {
        if (easyButton != null) SetButtonColors(easyButton, difficulty == AssemblyDifficulty.Easy);
        if (normalButton != null) SetButtonColors(normalButton, difficulty == AssemblyDifficulty.Normal);
        if (hardButton != null) SetButtonColors(hardButton, difficulty == AssemblyDifficulty.Hard);
    }

    void SetButtonColors(Button btn, bool selected)
    {
        var cb = btn.colors;
        var baseCol = selected ? selectedColor : unselectedColor;

        cb.normalColor = baseCol;
        cb.highlightedColor = Color.Lerp(baseCol, Color.white, highlightedBlend);
        cb.selectedColor = baseCol;
        cb.pressedColor = Color.Lerp(baseCol, Color.black, pressedDarken);
        btn.colors = cb;

        if (btn.targetGraphic != null)
            btn.targetGraphic.color = baseCol;
    }

    void RefreshManagerAfterDifficultyChange()
    {
        if (manager == null) manager = FindObjectOfType<WatchAssemblyManager>();
        if (manager == null) return;

        try
        {
            var type = manager.GetType();

            var fiAccepted = type.GetField("acceptedRotations", BindingFlags.Instance | BindingFlags.NonPublic);
            var list = fiAccepted != null ? fiAccepted.GetValue(manager) as IList<Quaternion> : null;
            var stepProp = type.GetProperty("CurrentStep", BindingFlags.Instance | BindingFlags.Public);
            var step = stepProp != null ? stepProp.GetValue(manager, null) as WatchAssemblyStep : null;

            if (list != null && step != null)
            {
                list.Clear();
                step.BuildAcceptedRotations(list as List<Quaternion>);
            }

            var miSetupGhost = type.GetMethod("SetupGhostForCurrentStep", BindingFlags.Instance | BindingFlags.NonPublic);
            miSetupGhost?.Invoke(manager, null);
        }
        catch (Exception)
        {
            // Best effort
        }
    }
}