using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WatchAssemblySequence", menuName = "Watch/Assembly Sequence", order = 11)]
public class WatchAssemblySequence : ScriptableObject
{
    public List<WatchAssemblyStep> steps = new List<WatchAssemblyStep>();

    public int StepCount => steps != null ? steps.Count : 0;

    public WatchAssemblyStep GetStep(int index)
    {
        if (steps == null || index < 0 || index >= steps.Count) return null;
        return steps[index];
    }

    public int IndexOf(WatchAssemblyStep step)
    {
        if (steps == null) return -1;
        return steps.IndexOf(step);
    }
}