using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Attach to the next-level trigger object. Needs a Collider2D (forced to isTrigger
// in Awake). When the player enters, fades a full-screen white Image in from 0 to 1,
// then loads nextSceneName.
[RequireComponent(typeof(Collider2D))]
public class NextLevelTrigger : MonoBehaviour
{
    [Header("Trigger")]
    public string playerTag = "Player";

    [Header("Fade To White")]
    [Tooltip("Full-screen white Image, alpha 0. Left inactive until the trigger fires.")]
    public Image fadeImage;
    public float fadeDuration = 1.5f;

    [Header("Scene")]
    public string nextSceneName;

    private bool triggered;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;

        if (fadeImage != null) fadeImage.gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggered) return; // guard against firing twice (e.g. overlapping colliders)
        if (!other.CompareTag(playerTag)) return;

        triggered = true;
        StartCoroutine(FadeToWhiteThenLoad());
    }

    private IEnumerator FadeToWhiteThenLoad()
    {
        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);

            Color c = fadeImage.color;
            c.a = 0f;
            fadeImage.color = c;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / fadeDuration;
                c.a = Mathf.Clamp01(t);
                fadeImage.color = c;
                yield return null;
            }
        }

        SceneManager.LoadScene(nextSceneName);
    }
}