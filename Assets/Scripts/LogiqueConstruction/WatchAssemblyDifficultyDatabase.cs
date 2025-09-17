using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WatchAssemblyDifficultyDatabase", menuName = "Watch/Assembly Difficulty Database", order = 12)]
public class WatchAssemblyDifficultyDatabase : ScriptableObject
{
    [Serializable]
    public struct DifficultyParams
    {
        [Tooltip("Max distance in meters to validate placement.")]
        public float positionTolerance;
        [Tooltip("Max angle in degrees (Quaternion.Angle).")]
        public float rotationToleranceDeg;

        [Tooltip("If true, yaw axis is free.")]
        public bool freeYaw;
        [Tooltip("If true, pitch axis is free.")]
        public bool freePitch;
        [Tooltip("If true, roll axis is free.")]
        public bool freeRoll;

        [Tooltip("Alternative local Euler offsets, added to base target rotation.")]
        public Vector3[] alternativeEulerOffsets;
    }

    [Serializable]
    public class StepEntry
    {
        public WatchAssemblyStep step;

        [Header("Easy")]
        public DifficultyParams easy;
        [Header("Normal")]
        public DifficultyParams normal;
        [Header("Hard")]
        public DifficultyParams hard;
    }

    [Tooltip("Per-step difficulty parameters.")]
    public List<StepEntry> entries = new List<StepEntry>();

    public void Apply(AssemblyDifficulty difficulty)
    {
        if (entries == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || e.step == null) continue;

            var p = GetParams(e, difficulty);

            // Apply values directly to the step asset at runtime
            e.step.positionTolerance = p.positionTolerance;
            e.step.rotationToleranceDeg = p.rotationToleranceDeg;

            e.step.freeYaw = p.freeYaw;
            e.step.freePitch = p.freePitch;
            e.step.freeRoll = p.freeRoll;

            // Copy array defensively
            if (p.alternativeEulerOffsets != null && p.alternativeEulerOffsets.Length > 0)
            {
                var copy = new Vector3[p.alternativeEulerOffsets.Length];
                Array.Copy(p.alternativeEulerOffsets, copy, copy.Length);
                e.step.alternativeEulerOffsets = copy;
            }
            else
            {
                e.step.alternativeEulerOffsets = Array.Empty<Vector3>();
            }
        }
    }

    static DifficultyParams GetParams(StepEntry e, AssemblyDifficulty d)
    {
        switch (d)
        {
            case AssemblyDifficulty.Easy: return e.easy;
            case AssemblyDifficulty.Hard: return e.hard;
            case AssemblyDifficulty.Normal:
            default: return e.normal;
        }
    }
}