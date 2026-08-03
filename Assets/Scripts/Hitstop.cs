using System.Collections;
using UnityEngine;

/// <summary>
/// Global hitstop / impact-pause utility. Call HitStop.Trigger(duration) from
/// anywhere (a hitbox, an ability, etc.) to freeze the whole game briefly for
/// impact feedback, then resume automatically.
///
/// Implemented as a static class backed by a hidden, auto-created runner
/// GameObject, so it works no matter which object triggers it and keeps
/// running even if that object (e.g. a hitbox that hit something) gets
/// destroyed mid-pause. Uses WaitForSecondsRealtime so the pause duration
/// itself isn't affected by the timeScale it's setting.
///
/// Assumes the game otherwise runs at Time.timeScale == 1 (i.e. there's no
/// separate pause-menu system also touching timeScale). If you add one later,
/// have it check HitStop before changing timeScale, or extend this to track
/// nested pauses.
/// </summary>
public static class HitStop
{
    private class Runner : MonoBehaviour { }

    private static Runner runner;

    public static void Trigger(float duration)
    {
        GetRunner().StartCoroutine(Routine(duration));
    }

    private static Runner GetRunner()
    {
        if (runner == null)
        {
            var go = new GameObject("HitStopRunner");
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }
        return runner;
    }

    private static IEnumerator Routine(float duration)
    {
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
    }
}