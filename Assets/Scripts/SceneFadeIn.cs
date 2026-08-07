using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Put this in the Tutorial scene (or any scene that should fade in from white).
// Assign a full-screen white Image starting at alpha 1 — it fades to 0 on Start,
// completing the transition that StartMenuManager's fade-to-white begins.
public class SceneFadeIn : MonoBehaviour
{
    public Image fadeImage;
    public float fadeDuration = 1.5f;

    private void Start()
    {
        StartCoroutine(FadeFromWhite());
    }

    private IEnumerator FadeFromWhite()
    {
        Color c = fadeImage.color;
        c.a = 1f;
        fadeImage.color = c;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / fadeDuration;
            c.a = 1f - Mathf.Clamp01(t);
            fadeImage.color = c;
            yield return null;
        }

        fadeImage.gameObject.SetActive(false);
    }
}